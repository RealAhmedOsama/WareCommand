using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.InventoryStatuses;
using Wms.Application.Quality;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Quality;

namespace Wms.Infrastructure.Tests.Quality;

public sealed class QualityInspectionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IInventoryStatusService> _inventoryStatuses = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly QualityInspectionService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _location;
    private readonly InventoryStatus _available;
    private readonly Receipt _receipt;
    private readonly ReceiptLine _line;
    private readonly Movement _movement;

    public QualityInspectionServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new QualityInspectionService(
            _context,
            _access.Object,
            _inventoryStatuses.Object,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<QualityInspectionService>.Instance);

        _warehouse = new Warehouse("QUALITY-WH", "Quality Warehouse");
        _item = new Item("QUALITY-ITEM", "Quality Widget", "EA");
        _available = new InventoryStatus(
            InventoryStatusCodes.Available,
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            isSystem: true);
        _context.AddRange(
            _warehouse,
            _item,
            new UnitOfMeasure("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"),
            _available);
        _context.SaveChanges();
        _location = new Location(
            "QUALITY-RECEIVE",
            "Quality receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _context.Add(_location);
        _context.SaveChanges();

        _receipt = new Receipt(
            "QUALITY-RECEIPT",
            _warehouse.Id,
            _warehouse.Code,
            supplierId: null,
            supplierCodeSnapshot: null,
            supplierNameSnapshot: null,
            purchaseOrderId: null,
            advanceShippingNoticeId: null,
            dockLocationId: null,
            receivingLocationId: _location.Id,
            sourceType: "MANUAL",
            createdByUserId: "receiver");
        _line = new ReceiptLine(
            1,
            _warehouse.Id,
            _item.Id,
            _item.Sku,
            _item.Name,
            "EA",
            10m,
            "EA",
            10m,
            1m,
            0,
            QuantityRoundingMode.Reject,
            0m,
            "BASE",
            string.Empty,
            receivingLocationId: _location.Id,
            inventoryStatusId: _available.Id,
            inventoryStatusCodeSnapshot: _available.Code,
            inventoryStatusNameSnapshot: _available.Name);
        _receipt.AddLine(_line);
        _receipt.Open("receiver", DateTime.UtcNow);
        _context.Add(_receipt);
        _context.SaveChanges();

        _movement = Movement.CreateReceipt(
            _item.Id,
            _location.Id,
            new Quantity(10m),
            "receiver",
            timestampUtc: DateTime.UtcNow,
            inventoryStatusId: _available.Id);
        _movement.LinkReceipt(_receipt.Id, _line.Id);
        _context.Add(_movement);
        _context.SaveChanges();
    }

    [Fact]
    public async Task EnsureForReceipt_IsIdempotentPerReceiptLineAndLicensePlate()
    {
        var profile = new QualityProfile(
            "QUALITY-REQUIRED",
            "Required inbound quality",
            "فحص وارد إلزامي",
            QualityRiskLevel.High,
            QualitySamplingMethod.FullInspection,
            0m,
            warehouseId: _warehouse.Id,
            itemId: _item.Id);
        _context.QualityProfiles.Add(profile);
        await _context.SaveChangesAsync();

        var first = await _service.EnsureForReceiptAsync(
            _receipt.Id,
            _line.Id,
            _movement,
            "receiver");
        await _context.SaveChangesAsync();
        var second = await _service.EnsureForReceiptAsync(
            _receipt.Id,
            _line.Id,
            _movement,
            "receiver");

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.Should().NotBeNull();
        second.IsSuccess.Should().BeTrue(second.Error);
        var persistedId = await _context.QualityInspections.Select(value => value.Id).SingleAsync();
        second.Value!.Id.Should().Be(persistedId);
        (await _context.QualityInspections.CountAsync()).Should().Be(1);
        first.Value!.SampleBaseQuantity.Should().Be(10m);
        first.Value.InventoryStatusId.Should().Be(_available.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
