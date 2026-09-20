using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryReplenishmentPolicyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly CapturingLogger<InventoryReplenishmentPolicyService> _logger = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly InventoryReplenishmentPolicyService _service;
    private readonly Warehouse _warehouse;
    private readonly Warehouse _otherWarehouse;
    private readonly Location _location;
    private readonly Location _otherLocation;
    private readonly Item _item;
    private readonly Item _zeroItem;
    private readonly InventoryStatus _availableStatus;
    private readonly InventoryStatus _holdStatus;

    public InventoryReplenishmentPolicyServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new InventoryReplenishmentPolicyService(
            _context,
            _warehouseAccess.Object,
            _auditWriter.Object,
            _clock,
            _logger);

        _warehouse = new Warehouse("REP-WH", "Replenishment Warehouse");
        _otherWarehouse = new Warehouse("REP-WH-2", "Other Replenishment Warehouse");
        _item = new Item("REP-ITEM", "Replenishment Widget", "EA");
        _zeroItem = new Item("REP-ZERO", "Zero Widget", "EA");
        _availableStatus = new InventoryStatus(
            "REP_AVAILABLE",
            "Replenishment available",
            "متاح للتجديد",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _holdStatus = new InventoryStatus(
            "REP_HOLD",
            "Replenishment hold",
            "معلق للتجديد",
            isAvailable: false,
            isAllocatable: false,
            isPickable: false,
            isShippable: false,
            isCountable: true);

        _context.AddRange(
            _warehouse,
            _otherWarehouse,
            _item,
            _zeroItem,
            _availableStatus,
            _holdStatus);
        _context.SaveChanges();

        _location = new Location("REP-A", "Replenishment Pick Face", _warehouse.Id);
        _otherLocation = new Location("REP-B", "Other Warehouse Face", _otherWarehouse.Id);
        _context.AddRange(_location, _otherLocation);
        _context.SaveChanges();

        AddBalance(_warehouse, _location, _item, _availableStatus, 4m, 1m);
        AddBalance(_warehouse, _location, _item, _holdStatus, 8m, 0m);
        AddBalance(_otherWarehouse, _otherLocation, _item, _availableStatus, 30m, 0m);
        _context.SaveChanges();
    }

    [Fact]
    public async Task SavePersistsPolicyAndWritesAudit()
    {
        var result = await _service.SaveAsync(
            null,
            CreateInput(
                _item.Id,
                _warehouse.Id,
                _location.Id,
                InventoryPolicyQuantityBasis.AvailableToPromise,
                reorderPoint: 5m),
            "user-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ItemSku.Should().Be(_item.Sku);
        result.Value.LocationCode.Should().Be(_location.Code);
        result.Value.QuantityBasis.Should().Be(InventoryPolicyQuantityBasis.AvailableToPromise);
        (await _context.InventoryReplenishmentPolicies.CountAsync()).Should().Be(1);
        _auditWriter.Verify(writer => writer.RecordAsync(
                It.Is<AuditRecord>(record =>
                    record.Action == WmsAuditActions.ReplenishmentPolicyChanged &&
                    record.EntityType == WmsAuditEntityTypes.ReplenishmentPolicy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveRejectsOverlappingEffectivePoliciesForSameScope()
    {
        var first = await _service.SaveAsync(
            null,
            CreateInput(_item.Id, _warehouse.Id, null, InventoryPolicyQuantityBasis.OnHand),
            "user-1");
        first.IsSuccess.Should().BeTrue(first.Error);

        var overlapping = await _service.SaveAsync(
            null,
            CreateInput(
                _item.Id,
                _warehouse.Id,
                null,
                InventoryPolicyQuantityBasis.OnHand,
                effectiveFrom: _clock.UtcNow.UtcDateTime.AddDays(5)),
            "user-1");

        overlapping.IsFailure.Should().BeTrue();
        overlapping.ErrorCode.Should().Be("inventory.policy_effective_overlap");
        (await _context.InventoryReplenishmentPolicies.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SignalsUseConfiguredBasisAndExcludeNonAllocatableStockFromAtp()
    {
        var atpPolicy = await _service.SaveAsync(
            null,
            CreateInput(
                _item.Id,
                _warehouse.Id,
                _location.Id,
                InventoryPolicyQuantityBasis.AvailableToPromise,
                reorderPoint: 5m,
                maximum: 20m),
            "user-1");
        var zeroPolicy = await _service.SaveAsync(
            null,
            CreateInput(
                _zeroItem.Id,
                _warehouse.Id,
                null,
                InventoryPolicyQuantityBasis.OnHand,
                reorderPoint: 2m,
                maximum: 10m),
            "user-1");
        atpPolicy.IsSuccess.Should().BeTrue(atpPolicy.Error);
        zeroPolicy.IsSuccess.Should().BeTrue(zeroPolicy.Error);

        var result = await _service.GetSignalsAsync(new InventoryReplenishmentSignalQuery(
            WarehouseId: _warehouse.Id));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().HaveCount(2);
        var atpSignal = result.Value.Single(signal => signal.PolicyId == atpPolicy.Value.Id);
        atpSignal.SignalKind.Should().Be(InventoryReplenishmentSignalKind.LowStock);
        atpSignal.EvaluatedQuantity.Should().Be(3m);
        atpSignal.OnHandQuantity.Should().Be(12m);
        atpSignal.ReservedQuantity.Should().Be(1m);
        atpSignal.AvailableToPromiseQuantity.Should().Be(3m);

        result.Value.Single(signal => signal.PolicyId == zeroPolicy.Value.Id)
            .SignalKind.Should().Be(InventoryReplenishmentSignalKind.OutOfStock);
    }

    [Fact]
    public async Task LimitedWarehouseScopeDoesNotExposeAnotherWarehouse()
    {
        var policy = await _service.SaveAsync(
            null,
            CreateInput(
                _item.Id,
                _otherWarehouse.Id,
                _otherLocation.Id,
                InventoryPolicyQuantityBasis.OnHand,
                reorderPoint: 40m,
                maximum: 50m),
            "user-1");
        policy.IsSuccess.Should().BeTrue(
            $"{policy.ErrorCode}: {policy.Error}; {_logger.LastException}");

        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(
                false,
                new HashSet<int> { _warehouse.Id }));

        var result = await _service.GetSignalsAsync(new InventoryReplenishmentSignalQuery());

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().BeEmpty();
    }

    private InventoryReplenishmentPolicyInput CreateInput(
        int itemId,
        int warehouseId,
        int? locationId,
        InventoryPolicyQuantityBasis quantityBasis,
        decimal reorderPoint = 4m,
        decimal maximum = 20m,
        DateTime? effectiveFrom = null) =>
        new(
            itemId,
            warehouseId,
            locationId,
            MinimumQuantity: 1m,
            MaximumQuantity: maximum,
            SafetyStockQuantity: 2m,
            ReorderPointQuantity: reorderPoint,
            TargetQuantity: Math.Max(reorderPoint, 8m),
            quantityBasis,
            effectiveFrom ?? _clock.UtcNow.UtcDateTime,
            EffectiveToUtc: _clock.UtcNow.UtcDateTime.AddDays(30),
            PreferredSource: "supplier-a",
            LeadTimeDays: 3);

    private void AddBalance(
        Warehouse warehouse,
        Location location,
        Item item,
        InventoryStatus status,
        decimal onHand,
        decimal reserved)
    {
        var balance = new InventoryBalance(new InventoryBalanceKey(
            warehouse.Id,
            location.Id,
            item.Id,
            null,
            null,
            null,
            null,
            status.Id,
            item.UnitOfMeasure));
        balance.Apply(onHand, reserved, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull =>
            NullLogger<T>.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LastException = exception;
        }
    }
}
