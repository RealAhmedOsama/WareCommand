using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Lots;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Lots;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Lots;

public sealed class LotServiceTests : IAsyncLifetime, IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IClock> _clock = new();
    private WmsDbContext _context = null!;
    private LotService _service = null!;
    private UnitOfWork _unitOfWork = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _context.Database.EnsureCreatedAsync();

        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clock
            .SetupGet(clock => clock.UtcNow)
            .Returns(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        _unitOfWork = new UnitOfWork(_context, _warehouseAccess.Object);
        _service = new LotService(
            _unitOfWork,
            _auditWriter.Object,
            _clock.Object,
            NullLogger<LotService>.Instance);
    }

    [Fact]
    public async Task ReceiptResolutionReusesNormalizedLotAndPreservesOmittedMetadata()
    {
        var item = new Item("LOT-ITEM", "Lot item", "EA", requiresLot: true);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var first = await _service.ResolveForReceiptAsync(
            item,
            " lot-a ",
            new LotDetailsRequest(
                ExpiryDate: new DateTime(2026, 9, 30),
                ManufacturedDate: new DateTime(2026, 8, 1),
                SupplierLotNumber: "SUP-1"),
            "user-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        var firstLot = first.Value;
        firstLot.Number.Should().Be("LOT-A");
        firstLot.SupplierLotNumber.Should().Be("SUP-1");

        var second = await _service.ResolveForReceiptAsync(
            item,
            "LOT-A",
            new LotDetailsRequest(),
            "user-1");

        second.IsSuccess.Should().BeTrue(second.Error);
        second.Value.Id.Should().Be(firstLot.Id);
        second.Value.ExpiryDate.Should().Be(firstLot.ExpiryDate);
        second.Value.SupplierLotNumber.Should().Be("SUP-1");
        (await _context.Lots.CountAsync()).Should().Be(1);
        _auditWriter.Verify(writer => writer.RecordAsync(
                It.Is<AuditRecord>(record => record.Action == WmsAuditActions.LotCreated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReceiptResolutionRejectsControlledMetadataChangeAfterMovementHistory()
    {
        var item = new Item("LOT-ITEM", "Lot item", "EA", requiresLot: true);
        var warehouse = new Warehouse("LOT-WH", "Lot warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var location = new Location("LOT-BIN", "Lot bin", warehouse.Id);
        _context.Locations.Add(location);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var lot = (await _service.ResolveForReceiptAsync(
            item,
            "LOT-A",
            new LotDetailsRequest(ExpiryDate: new DateTime(2026, 9, 30)),
            "user-1")).Value;

        _context.Movements.Add(Movement.CreateReceipt(
            item.Id,
            location.Id,
            new Wms.Domain.ValueObjects.Quantity(1),
            "user-1",
            lot.Id));
        await _context.SaveChangesAsync();

        var result = await _service.ResolveForReceiptAsync(
            item,
            "LOT-A",
            new LotDetailsRequest(ExpiryDate: new DateTime(2026, 10, 1)),
            "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("lot.metadata_locked");
    }

    [Fact]
    public async Task StatusChangeAndTraceabilityExposeRecallAndMovementHistory()
    {
        var warehouse = new Warehouse("LOT-WH", "Lot warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var location = new Location("LOT-BIN", "Lot bin", warehouse.Id);
        var item = new Item("LOT-ITEM", "Lot item", "EA", requiresLot: true);
        _context.Locations.Add(location);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var lot = (await _service.ResolveForReceiptAsync(
            item,
            "LOT-A",
            new LotDetailsRequest(ExpiryDate: new DateTime(2026, 9, 30)),
            "user-1")).Value;

        var movement = Movement.CreateReceipt(
            item.Id,
            location.Id,
            new Wms.Domain.ValueObjects.Quantity(4),
            "user-1",
            lot.Id);
        _context.Movements.Add(movement);
        _context.Stock.Add(new Stock(item.Id, location.Id, new Wms.Domain.ValueObjects.Quantity(4), lot.Id));
        await _context.SaveChangesAsync();

        var status = await _service.ChangeStatusAsync(
            lot.Id,
            LotStatus.Recalled,
            "supplier recall",
            "user-1");
        status.IsSuccess.Should().BeTrue(status.Error);

        var trace = await _service.GetTraceabilityAsync(lot.Id);
        trace.IsSuccess.Should().BeTrue(trace.Error);
        trace.Value.Lot.Status.Should().Be(LotStatus.Recalled);
        trace.Value.Stock.Should().ContainSingle(row => row.QuantityAvailable == 4);
        trace.Value.Movements.Should().ContainSingle(row => row.Type == MovementType.Receipt);
    }

    [Fact]
    public async Task MovementResolutionRejectsLotIdentityForNonLotControlledItem()
    {
        var item = new Item("NON-LOT-ITEM", "Non-lot item", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var lot = new Lot("LOT-A", item.Id);
        _context.Lots.Add(lot);
        await _context.SaveChangesAsync();

        var result = await _service.ResolveForMovementAsync(
            item,
            lot.Number,
            requireAllocationEligibility: false,
            new DateOnly(2026, 9, 20));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("lot.not_required");
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _connection.Dispose();
    }
}
