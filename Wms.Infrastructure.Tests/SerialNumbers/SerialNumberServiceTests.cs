using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.SerialNumbers;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.SerialNumbers;

namespace Wms.Infrastructure.Tests.SerialNumbers;

public sealed class SerialNumberServiceTests : IAsyncLifetime, IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IClock> _clock = new();
    private WmsDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private SerialNumberService _service = null!;

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
        _service = new SerialNumberService(
            _unitOfWork,
            _auditWriter.Object,
            _clock.Object,
            NullLogger<SerialNumberService>.Instance);
    }

    [Fact]
    public async Task ReceiptResolutionCreatesNormalizedIdentityAndRejectsDuplicate()
    {
        var item = new Item("SERIAL-ITEM", "Serial item", "EA", requiresSerial: true);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var first = await _service.ResolveForReceiptAsync(item, " sn-001 ", null);

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.Number.Should().Be("SN-001");
        first.Value.Id.Should().BeGreaterThan(0);

        var duplicate = await _service.ResolveForReceiptAsync(item, "SN-001", null);

        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("serial.duplicate_active");
        (await _context.SerialNumbers.CountAsync()).Should().Be(1);
        _auditWriter.Verify(writer => writer.RecordAsync(
                It.Is<AuditRecord>(record => record.Action == WmsAuditActions.SerialCreated),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MovementResolutionRequiresCurrentLocationAndAllocationEligibility()
    {
        var warehouse = new Warehouse("SERIAL-WH", "Serial warehouse");
        var item = new Item("SERIAL-ITEM", "Serial item", "EA", requiresSerial: true);
        _context.Warehouses.Add(warehouse);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var location = new Location("SERIAL-BIN", "Serial bin", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var serial = (await _service.ResolveForReceiptAsync(item, "SN-001", null)).Value;
        serial.RecordReceipt(warehouse.Id, location.Id, null, "PO-1", false, _clock.Object.UtcNow.UtcDateTime);
        await _context.SaveChangesAsync();

        var resolved = await _service.ResolveForMovementAsync(
            item,
            "sn-001",
            null,
            location.Id,
            requireAllocationEligibility: true);

        resolved.IsSuccess.Should().BeTrue(resolved.Error);
        resolved.Value.Id.Should().Be(serial.Id);

        var wrongLocation = await _service.ResolveForMovementAsync(
            item,
            "SN-001",
            null,
            location.Id + 100,
            requireAllocationEligibility: true);
        wrongLocation.ErrorCode.Should().Be("serial.location_conflict");
    }

    [Fact]
    public async Task TraceabilityAndStatusChangeExposeSerialLifecycle()
    {
        var warehouse = new Warehouse("SERIAL-WH", "Serial warehouse");
        var item = new Item("SERIAL-ITEM", "Serial item", "EA", requiresSerial: true);
        _context.Warehouses.Add(warehouse);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var location = new Location("SERIAL-BIN", "Serial bin", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var serial = (await _service.ResolveForReceiptAsync(item, "SN-001", null)).Value;
        serial.RecordReceipt(warehouse.Id, location.Id, null, "PO-1", false, _clock.Object.UtcNow.UtcDateTime);
        _context.Movements.Add(Movement.CreateReceipt(
            item.Id,
            location.Id,
            new Wms.Domain.ValueObjects.Quantity(1),
            "user-1",
            serialNumber: serial.Number,
            timestampUtc: _clock.Object.UtcNow.UtcDateTime,
            serialNumberId: serial.Id));
        _context.Stock.Add(new Stock(
            item.Id,
            location.Id,
            new Wms.Domain.ValueObjects.Quantity(1),
            serialNumber: serial.Number,
            serialNumberId: serial.Id));
        await _context.SaveChangesAsync();

        var status = await _service.ChangeStatusAsync(
            serial.Id,
            SerialStatus.Hold,
            "quality review",
            "user-1");
        status.IsSuccess.Should().BeTrue(status.Error);

        var trace = await _service.GetTraceabilityAsync(serial.Id);
        trace.IsSuccess.Should().BeTrue(trace.Error);
        trace.Value.Serial.Status.Should().Be(SerialStatus.Hold);
        trace.Value.Stock.Should().ContainSingle(row => row.QuantityAvailable == 1);
        trace.Value.Movements.Should().ContainSingle(row => row.Type == MovementType.Receipt);
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
