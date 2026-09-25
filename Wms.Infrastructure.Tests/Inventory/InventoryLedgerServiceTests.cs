using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Logging;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Services;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryLedgerServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _sourceLocation;
    private readonly Location _destinationLocation;
    private readonly Item _item;

    public InventoryLedgerServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _unitOfWork = new UnitOfWork(_context, _warehouseAccess.Object);
        _service = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _warehouseAccess.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)));

        _warehouse = new Warehouse("LEDGER-WH", "Ledger Warehouse");
        _item = new Item("LEDGER-ITEM", "Ledger Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();
        _sourceLocation = new Location("LEDGER-A", "Ledger Source", _warehouse.Id);
        _destinationLocation = new Location("LEDGER-B", "Ledger Destination", _warehouse.Id);
        _context.AddRange(_sourceLocation, _destinationLocation);
        _context.SaveChanges();
    }

    [Fact]
    public async Task RecordAsyncMaintainsBalancesAndBalancedMoveLegs()
    {
        var sourceKey = CreateKey(_sourceLocation, InventoryStatusSystemIds.Available);
        var destinationKey = CreateKey(_destinationLocation, InventoryStatusSystemIds.Available);

        var receipt = await _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    sourceKey,
                    10m,
                    ActorUserId: "user-1",
                    ReferenceType: "Receipt",
                    ReferenceId: "RCV-1",
                    IdempotencyKey: "receipt-1",
                    TransactionGroupId: "receipt-group-1")
            });
        await _context.SaveChangesAsync();

        var move = await _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Transfer,
                    sourceKey,
                    -4m,
                    ActorUserId: "user-1",
                    ReferenceType: "Transfer",
                    ReferenceId: "MOVE-1",
                    IdempotencyKey: "move-1",
                    TransactionGroupId: "move-group-1",
                    EntrySequence: 1),
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Transfer,
                    destinationKey,
                    4m,
                    ActorUserId: "user-1",
                    ReferenceType: "Transfer",
                    ReferenceId: "MOVE-1",
                    IdempotencyKey: "move-1",
                    TransactionGroupId: "move-group-1",
                    EntrySequence: 2)
            });
        await _context.SaveChangesAsync();

        receipt.Should().ContainSingle();
        move.Should().HaveCount(2);
        (await _context.InventoryBalances.SingleAsync(balance => balance.LocationId == _sourceLocation.Id))
            .OnHandQuantity.Should().Be(6m);
        (await _context.InventoryBalances.SingleAsync(balance => balance.LocationId == _destinationLocation.Id))
            .OnHandQuantity.Should().Be(4m);

        var persistedMove = await _context.InventoryTransactions
            .Where(transaction => transaction.ReferenceId == "MOVE-1")
            .OrderBy(transaction => transaction.EntrySequence)
            .ToListAsync();
        persistedMove[0].QuantityBefore.Should().Be(10m);
        persistedMove[0].QuantityAfter.Should().Be(6m);
        persistedMove[1].QuantityBefore.Should().Be(0m);
        persistedMove[1].QuantityAfter.Should().Be(4m);
        persistedMove.Sum(transaction => transaction.QuantityDelta).Should().Be(0m);

        var report = await _service.ReconcileAsync();
        report.IsBalanced.Should().BeTrue();
        report.TransactionCount.Should().Be(3);
    }

    [Fact]
    public async Task RecordAsyncResolvesWarehouseScopeOnceForAllLedgerReads()
    {
        await _service.RecordAsync(
        [
            new InventoryLedgerEntryRequest(
                InventoryTransactionType.Receipt,
                CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                3m,
                ActorUserId: "user-1",
                IdempotencyKey: "scope-once-1",
                TransactionGroupId: "scope-once-group-1"),
            new InventoryLedgerEntryRequest(
                InventoryTransactionType.Receipt,
                CreateKey(_destinationLocation, InventoryStatusSystemIds.Available),
                4m,
                ActorUserId: "user-1",
                IdempotencyKey: "scope-once-2",
                TransactionGroupId: "scope-once-group-2")
        ]);

        _warehouseAccess.Verify(
            service => service.GetScopeAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordAsyncRejectsWarehouseOutsideResolvedScope()
    {
        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(
                false,
                new HashSet<int> { _warehouse.Id + 1 }));

        var act = () => _service.RecordAsync(
        [
            new InventoryLedgerEntryRequest(
                InventoryTransactionType.Receipt,
                CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                3m,
                ActorUserId: "user-1",
                IdempotencyKey: "outside-scope-1",
                TransactionGroupId: "outside-scope-group-1")
        ]);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _context.InventoryBalances.CountAsync()).Should().Be(0);
        (await _context.InventoryTransactions.CountAsync()).Should().Be(0);
        _warehouseAccess.Verify(
            service => service.GetScopeAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordAsyncKeepsCompanyAndExternalOwnerBalancesSeparate()
    {
        var externalOwner = new InventoryOwner(
            "EXT-LEDGER-1",
            InventoryOwnerKind.ExternalOwner,
            "External ledger owner",
            externalOwnerReference: "EXT-LEDGER-1");
        _context.InventoryOwners.Add(externalOwner);
        await _context.SaveChangesAsync();

        var companyKey = CreateKey(_sourceLocation, InventoryStatusSystemIds.Available);
        var externalKey = new InventoryBalanceKey(
            _warehouse.Id,
            _sourceLocation.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            InventoryStatusSystemIds.Available,
            _item.UnitOfMeasure,
            InventoryOwnerKind.ExternalOwner,
            externalOwner.Id,
            externalOwner.OwnerCode);

        await _service.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    companyKey,
                    5m,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: "owner-company-receipt",
                    TransactionGroupId: "owner-company-group"),
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    externalKey,
                    7m,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: "owner-external-receipt",
                    TransactionGroupId: "owner-external-group")
            ]);
        await _context.SaveChangesAsync();

        var balances = await _context.InventoryBalances
            .OrderBy(balance => balance.OwnerKind)
            .ToListAsync();
        balances.Should().HaveCount(2);
        balances.Single(balance => balance.OwnerKind == InventoryOwnerKind.CompanyOwned)
            .OnHandQuantity.Should().Be(5m);
        balances.Single(balance => balance.OwnerKind == InventoryOwnerKind.ExternalOwner)
            .OnHandQuantity.Should().Be(7m);
        (await _service.ReconcileAsync()).IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task RecordAsyncCarriesLegacyStockIntoOpeningLedgerBeforeNewDelta()
    {
        _context.Stock.Add(new Stock(
            _item.Id,
            _sourceLocation.Id,
            new Wms.Domain.ValueObjects.Quantity(7m),
            inventoryStatusId: InventoryStatusSystemIds.Available));
        await _context.SaveChangesAsync();

        var result = await _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Adjustment,
                    CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                    2m,
                    ActorUserId: "user-1",
                    ReferenceType: "Adjustment",
                    ReferenceId: "ADJ-1",
                    IdempotencyKey: "adjustment-1",
                    TransactionGroupId: "adjustment-group-1")
            });
        await _context.SaveChangesAsync();

        (await _context.InventoryBalances.SingleAsync()).OnHandQuantity.Should().Be(9m);
        (await _context.InventoryTransactions.CountAsync()).Should().Be(2);
        (await _service.ReconcileAsync()).IsBalanced.Should().BeTrue();
        result.Single().QuantityBefore.Should().Be(7m);
    }

    [Fact]
    public async Task RecordAsyncBlocksNegativeBalanceByDefault()
    {
        var act = () => _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Pick,
                    CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                    -1m,
                    ActorUserId: "user-1",
                    IdempotencyKey: "pick-negative-1",
                    TransactionGroupId: "pick-negative-group")
            });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot become negative*");
        (await _context.InventoryTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesRejectsMutationOfPersistedLedgerTransaction()
    {
        await _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                    1m,
                    ActorUserId: "user-1",
                    IdempotencyKey: "immutable-1",
                    TransactionGroupId: "immutable-group")
            });
        await _context.SaveChangesAsync();
        var transaction = await _context.InventoryTransactions.SingleAsync();
        _context.InventoryTransactions.Update(transaction);

        var act = () => _context.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task StaleBalanceWriterGetsTypedRecoverableConcurrencyConflict()
    {
        await _service.RecordAsync(
            new[]
            {
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    CreateKey(_sourceLocation, InventoryStatusSystemIds.Available),
                    5m,
                    ActorUserId: "user-1",
                    IdempotencyKey: "concurrency-seed",
                    TransactionGroupId: "concurrency-seed-group")
            });
        await _context.SaveChangesAsync();

        await using var competingContext = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options);
        var firstBalance = await _context.InventoryBalances.SingleAsync();
        var competingBalance = await competingContext.InventoryBalances.SingleAsync();
        firstBalance.Apply(1m, 0m, allowNegativeStock: false);
        competingBalance.Apply(1m, 0m, allowNegativeStock: false);

        await _context.SaveChangesAsync();
        var act = () => competingContext.SaveChangesAsync();

        var exception = await act.Should().ThrowAsync<ConcurrencyConflictException>();
        exception.Which.ResourceType.Should().Be(nameof(InventoryBalance));
        exception.Which.ResourceId.Should().Contain("Id=");
        exception.Which.Message.Should().NotContain("Id=");
    }

    [Fact]
    public async Task StockMovementBoundaryAppendsLedgerWithLegacyStockProjection()
    {
        var auditWriter = new Mock<IAuditWriter>();
        auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var stockMovementService = new StockMovementService(
            _unitOfWork,
            NullLogger<StockMovementService>.Instance,
            auditWriter.Object,
            new WmsRequestContext("Test"),
            new WmsOperationContextAccessor(),
            new FixedClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)),
            inventoryLedgerService: _service);

        await stockMovementService.ReceiveAsync(
            _item.Id,
            _sourceLocation.Id,
            new Wms.Domain.ValueObjects.Quantity(10m),
            "user-1");
        await _context.SaveChangesAsync();

        await stockMovementService.PutawayAsync(
            _item.Id,
            _sourceLocation.Id,
            _destinationLocation.Id,
            new Wms.Domain.ValueObjects.Quantity(4m),
            "user-1");
        await _context.SaveChangesAsync();

        (await _context.Stock.SingleAsync(stock => stock.LocationId == _sourceLocation.Id))
            .QuantityAvailable.Value.Should().Be(6m);
        (await _context.Stock.SingleAsync(stock => stock.LocationId == _destinationLocation.Id))
            .QuantityAvailable.Value.Should().Be(4m);
        (await _context.InventoryTransactions.CountAsync()).Should().Be(3);
        (await _service.ReconcileAsync()).IsBalanced.Should().BeTrue();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private InventoryBalanceKey CreateKey(Location location, int statusId) => new(
        _warehouse.Id,
        location.Id,
        _item.Id,
        null,
        null,
        null,
        null,
        statusId,
        _item.UnitOfMeasure);

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
