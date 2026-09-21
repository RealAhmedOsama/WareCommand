using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryClassificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Warehouse _warehouse;
    private readonly Warehouse _otherWarehouse;
    private readonly Location _location;
    private readonly Location _otherLocation;
    private readonly Item _itemA;
    private readonly Item _itemB;
    private readonly Item _itemC;
    private readonly Item _noDataItem;
    private readonly InventoryStatus _availableStatus;
    private readonly InventoryClassificationService _service;

    public InventoryClassificationServiceTests()
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

        _warehouse = new Warehouse("ABC-WH", "ABC warehouse");
        _otherWarehouse = new Warehouse("ABC-WH-2", "Other ABC warehouse");
        _itemA = new Item("ABC-A", "High velocity item", "EA");
        _itemB = new Item("ABC-B", "Medium velocity item", "EA");
        _itemC = new Item("ABC-C", "Low velocity item", "EA");
        _noDataItem = new Item("ABC-Z", "No data item", "EA");
        _availableStatus = new InventoryStatus(
            "ABC_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(
            _warehouse,
            _otherWarehouse,
            _itemA,
            _itemB,
            _itemC,
            _noDataItem,
            _availableStatus);
        _context.SaveChanges();

        _location = new Location(
            "ABC-BIN",
            "ABC source",
            _warehouse.Id,
            type: LocationType.Bin,
            isReceivable: false);
        _otherLocation = new Location(
            "ABC-BIN-2",
            "Other ABC source",
            _otherWarehouse.Id,
            type: LocationType.Bin,
            isReceivable: false);
        _context.AddRange(_location, _otherLocation);
        _context.SaveChanges();

        _service = new InventoryClassificationService(
            _context,
            _access.Object,
            _audit.Object,
            _clock,
            NullLogger<InventoryClassificationService>.Instance);
    }

    [Fact]
    public async Task RecalculateUsesDeterministicBoundariesAndPersistsNoDataAsUnclassified()
    {
        AddShipment(_itemA, 80m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        AddShipment(_itemB, 15m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        AddShipment(_itemC, 5m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        _context.SaveChanges();

        var policy = await SavePolicyAsync(_warehouse.Id);
        var first = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                policy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.ItemsExamined.Should().Be(4);
        first.Value.ClassificationsChanged.Should().Be(4);

        var rows = await _context.InventoryClassifications
            .AsNoTracking()
            .ToDictionaryAsync(row => row.ItemId);
        rows[_itemA.Id].Classification.Should().Be(InventoryClassificationClass.A);
        rows[_itemB.Id].Classification.Should().Be(InventoryClassificationClass.B);
        rows[_itemC.Id].Classification.Should().Be(InventoryClassificationClass.C);
        rows[_noDataItem.Id].Classification.Should().Be(InventoryClassificationClass.Unclassified);
        (await _context.InventoryClassificationHistories.CountAsync()).Should().Be(4);

        var repeated = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                policy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner-1");

        repeated.IsSuccess.Should().BeTrue(repeated.Error);
        repeated.Value.ClassificationsChanged.Should().Be(0);
        (await _context.InventoryClassificationHistories.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task RecalculateIsWarehouseSpecificAndDryRunDoesNotWrite()
    {
        AddShipment(_itemA, 1m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        AddShipment(
            _itemB,
            100m,
            _clock.UtcNow.UtcDateTime.AddDays(-1),
            _otherLocation);
        _context.Add(new InventoryBalance(new Wms.Domain.Inventory.InventoryBalanceKey(
            _otherWarehouse.Id,
            _otherLocation.Id,
            _itemB.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _itemB.UnitOfMeasure)));
        _context.SaveChanges();

        var firstPolicy = await SavePolicyAsync(_warehouse.Id);
        var otherPolicy = await SavePolicyAsync(_otherWarehouse.Id, "OTHER-ABC");
        var first = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                firstPolicy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner-1");
        first.IsSuccess.Should().BeTrue(first.Error);

        var dryRun = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _otherWarehouse.Id,
                otherPolicy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20,
                DryRun: true),
            "planner-1");

        dryRun.IsSuccess.Should().BeTrue(dryRun.Error);
        dryRun.Value.DryRun.Should().BeTrue();
        dryRun.Value.ClassificationsChanged.Should().Be(4);
        (await _context.InventoryClassifications
            .CountAsync(row => row.WarehouseId == _otherWarehouse.Id))
            .Should().Be(0);

        var other = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _otherWarehouse.Id,
                otherPolicy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner-1");
        other.IsSuccess.Should().BeTrue(other.Error);
        (await _context.InventoryClassifications
            .SingleAsync(row => row.WarehouseId == _otherWarehouse.Id && row.ItemId == _itemB.Id))
            .Classification.Should().Be(InventoryClassificationClass.A);
        (await _context.InventoryClassifications
            .AnyAsync(row => row.WarehouseId == _warehouse.Id && row.ItemId == _itemB.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ActiveManualOverrideIsNotOverwrittenUntilExpiryAndHistoryIsAudited()
    {
        AddShipment(_itemA, 80m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        AddShipment(_itemB, 15m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        AddShipment(_itemC, 5m, _clock.UtcNow.UtcDateTime.AddDays(-1));
        _context.SaveChanges();
        var policy = await SavePolicyAsync(_warehouse.Id);
        var initial = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                policy.Id,
                _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner-1");
        initial.IsSuccess.Should().BeTrue(initial.Error);

        var overrideResult = await _service.OverrideAsync(
            new InventoryClassificationOverrideInput(
                _warehouse.Id,
                _itemB.Id,
                InventoryClassificationClass.A,
                "Promotion campaign requires temporary priority",
                _clock.UtcNow.UtcDateTime.AddDays(7)),
            "supervisor-1");
        overrideResult.IsSuccess.Should().BeTrue(overrideResult.Error);
        var historyAfterOverride = await _context.InventoryClassificationHistories.CountAsync();

        var protectedRecalculation = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                policy.Id,
                _clock.UtcNow.UtcDateTime.AddDays(1),
                Limit: 20),
            "planner-1");
        protectedRecalculation.IsSuccess.Should().BeTrue(protectedRecalculation.Error);
        protectedRecalculation.Value.ManualOverridesSkipped.Should().Be(1);
        (await _context.InventoryClassifications
            .SingleAsync(row => row.WarehouseId == _warehouse.Id && row.ItemId == _itemB.Id))
            .Classification.Should().Be(InventoryClassificationClass.A);
        (await _context.InventoryClassificationHistories.CountAsync())
            .Should().Be(historyAfterOverride);

        var expiredRecalculation = await _service.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                _warehouse.Id,
                policy.Id,
                _clock.UtcNow.UtcDateTime.AddDays(8),
                Limit: 20),
            "planner-1");
        expiredRecalculation.IsSuccess.Should().BeTrue(expiredRecalculation.Error);
        (await _context.InventoryClassifications
            .SingleAsync(row => row.WarehouseId == _warehouse.Id && row.ItemId == _itemB.Id))
            .Classification.Should().Be(InventoryClassificationClass.B);
        (await _context.InventoryClassificationHistories.CountAsync())
            .Should().Be(historyAfterOverride + 1);
        _audit.Verify(writer => writer.RecordAsync(
                It.Is<AuditRecord>(record =>
                    record.Action == WmsAuditActions.InventoryClassificationOverrideChanged),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private async Task<InventoryClassificationPolicyDto> SavePolicyAsync(
        int warehouseId,
        string policyKey = "ABC-DEFAULT")
    {
        var result = await _service.SavePolicyAsync(
            null,
            new InventoryClassificationPolicyInput(
                warehouseId,
                policyKey,
                InventoryClassificationMethod.ShippedQuantity,
                LookbackDays: 30,
                AThresholdPercent: 80m,
                BThresholdPercent: 95m,
                MinimumActivityValue: 0m,
                _clock.UtcNow.UtcDateTime.AddDays(-1)),
            "planner-1");
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value;
    }

    private void AddShipment(
        Item item,
        decimal quantity,
        DateTime timestampUtc,
        Location? location = null)
    {
        _context.Movements.Add(Movement.CreateShip(
            item.Id,
            (location ?? _location).Id,
            new Quantity(quantity),
            "scanner-1",
            timestampUtc: timestampUtc,
            inventoryStatusId: _availableStatus.Id));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
