using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class InventoryReconciliationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly CapturingLogger<InventoryReconciliationService> _logger = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly InventoryReconciliationService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _location;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;

    public InventoryReconciliationServiceTests()
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

        _service = new InventoryReconciliationService(
            _context,
            _warehouseAccess.Object,
            _clock,
            _logger);

        _warehouse = new Warehouse("REC-WH", "Reconciliation Warehouse");
        _item = new Item("REC-ITEM", "Reconciliation Item", "EA");
        _availableStatus = new InventoryStatus(
            "REC_AVAILABLE",
            "Reconciliation available",
            "متاح للمطابقة",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _location = new Location("REC-A", "Reconciliation Location", _warehouse.Id);
        _context.Add(_location);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CleanDeepReportCoversLedgerBalanceAndReservationAllocationSources()
    {
        await SeedLedgerAndReservationAsync();

        var result = await _service.ReconcileAsync(new InventoryReconciliationQuery(
            WarehouseId: _warehouse.Id,
            Deep: true,
            BatchSize: 1));

        result.IsSuccess.Should().BeTrue($"{result.ErrorCode}: {result.Error}; {_logger.LastException}");
        result.Value.IsClean.Should().BeTrue(result.Value.Issues.ToString());
        result.Value.IssueCount.Should().Be(0);
        result.Value.BalancesScanned.Should().Be(1);
        result.Value.TransactionsScanned.Should().Be(1);
        result.Value.ReservationsScanned.Should().Be(1);
        result.Value.AllocationsScanned.Should().Be(1);
        result.Value.Issues.Should().BeEmpty();
        result.Value.BatchesRead.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReportDetectsBalanceWithoutLedgerHistoryAndBoundsIssueOutput()
    {
        var balance = new InventoryBalance(CreateKey());
        balance.Apply(3m, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
        await _context.SaveChangesAsync();

        var result = await _service.ReconcileAsync(new InventoryReconciliationQuery(
            WarehouseId: _warehouse.Id,
            Deep: false,
            BatchSize: 1,
            MaxIssues: 1));

        result.IsSuccess.Should().BeTrue($"{result.ErrorCode}: {result.Error}; {_logger.LastException}");
        result.Value.IsClean.Should().BeFalse();
        result.Value.IssueCount.Should().BeGreaterThanOrEqualTo(1);
        result.Value.Issues.Should().ContainSingle();
        result.Value.IssuesTruncated.Should().BeFalse();
        result.Value.Issues.Single().Code.Should().Be("inventory_transactions_missing");
    }

    [Fact]
    public async Task InvalidDateRangeIsRejectedBeforeReadingData()
    {
        var result = await _service.ReconcileAsync(new InventoryReconciliationQuery(
            FromUtc: _clock.UtcNow,
            ToUtc: _clock.UtcNow.AddMinutes(-1)));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inventory.reconciliation_date_range_invalid");
    }

    private async Task SeedLedgerAndReservationAsync()
    {
        var balance = new InventoryBalance(CreateKey());
        balance.Apply(5m, 2m, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
        await _context.SaveChangesAsync();

        var transaction = new InventoryTransaction(
            InventoryTransactionType.Receipt,
            CreateKey(),
            quantityDelta: 5m,
            quantityBefore: 0m,
            quantityAfter: 5m,
            reservedQuantityDelta: 2m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 2m,
            actorUserId: "reconcile-test",
            occurredAtUtc: _clock.UtcNow.UtcDateTime,
            correlationId: "reconcile-correlation",
            idempotencyKey: "reconcile-ledger-1",
            transactionGroupId: "reconcile-group-1",
            entrySequence: 1,
            referenceType: "Receipt",
            referenceId: "REC-1");
        _context.InventoryTransactions.Add(transaction);

        var reservation = new InventoryReservation(
            "SalesOrder",
            "SO-REC-1",
            1,
            _warehouse.Id,
            _item.Id,
            2m,
            InventoryReservationMode.Hard,
            1,
            null,
            "reconcile-test",
            "reconcile-reservation");
        _context.InventoryReservations.Add(reservation);
        await _context.SaveChangesAsync();

        _context.InventoryReservationAllocations.Add(new InventoryReservationAllocation(
            reservation.Id,
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure,
            2m));
        await _context.SaveChangesAsync();
    }

    private InventoryBalanceKey CreateKey() => new(
        _warehouse.Id,
        _location.Id,
        _item.Id,
        null,
        null,
        null,
        null,
        _availableStatus.Id,
        _item.UnitOfMeasure);

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
