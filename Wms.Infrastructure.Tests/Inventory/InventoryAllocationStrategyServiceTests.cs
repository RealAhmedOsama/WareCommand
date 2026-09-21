using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryAllocationStrategyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InventoryAllocationStrategyService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _firstLocation;
    private readonly Location _secondLocation;

    public InventoryAllocationStrategyServiceTests()
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
            .ReturnsAsync(Wms.Application.Common.Result.Success());
        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("STRAT-WH", "Strategy warehouse");
        _item = new Item("STRAT-ITEM", "Strategy item", "EA");
        _availableStatus = new InventoryStatus(
            "STRAT-AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _firstLocation = new Location(
            "STRAT-A",
            "First location",
            _warehouse.Id,
            priority: 20);
        _secondLocation = new Location(
            "STRAT-B",
            "Second location",
            _warehouse.Id,
            priority: 1);
        _context.AddRange(_firstLocation, _secondLocation);
        _context.SaveChanges();

        _service = new InventoryAllocationStrategyService(
            _context,
            _access.Object,
            _audit.Object,
            _clock,
            NullLogger<InventoryAllocationStrategyService>.Instance);
    }

    [Fact]
    public async Task PolicySaveSearchAndEffectiveOverlapGuardAreWarehouseScoped()
    {
        var start = _clock.UtcNow.UtcDateTime.AddDays(-1);
        var first = await _service.SaveAsync(
            null,
            new InventoryAllocationStrategyPolicyInput(
                _warehouse.Id,
                "STRAT-CRUD",
                InventoryAllocationStrategyKind.LocationPriority,
                start,
                ItemId: _item.Id,
                EffectiveToUtc: start.AddDays(5)),
            "planner");

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.PolicyKey.Should().Be("STRAT-CRUD");

        var overlap = await _service.SaveAsync(
            null,
            new InventoryAllocationStrategyPolicyInput(
                _warehouse.Id,
                "STRAT-CRUD-2",
                InventoryAllocationStrategyKind.Fifo,
                start.AddDays(1),
                ItemId: _item.Id,
                EffectiveToUtc: start.AddDays(6)),
            "planner");

        overlap.IsFailure.Should().BeTrue();
        var listed = await _service.SearchAsync(
            new InventoryAllocationStrategyPolicyQuery(_warehouse.Id));
        listed.IsSuccess.Should().BeTrue(listed.Error);
        listed.Value.Should().ContainSingle(policy => policy.Id == first.Value.Id);
    }

    [Fact]
    public async Task FefoRanksExpiryBeforeLocationAndReturnsExplanation()
    {
        var laterLot = new Lot(
            "STRAT-LATER",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(90));
        var earlierLot = new Lot(
            "STRAT-EARLIER",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(30));
        _context.AddRange(laterLot, earlierLot);
        _context.SaveChanges();

        var laterBalance = AddBalance(_firstLocation, laterLot.Id, 8m);
        var earlierBalance = AddBalance(_secondLocation, earlierLot.Id, 8m);
        _context.SaveChanges();
        AddReceipt(laterBalance, _clock.UtcNow.UtcDateTime.AddDays(-2));
        AddReceipt(earlierBalance, _clock.UtcNow.UtcDateTime.AddDays(-1));
        await _context.SaveChangesAsync();
        await AddPolicyAsync(InventoryAllocationStrategyKind.Fefo);

        var result = await _service.ResolveAsync(
            Request(10m),
            _item,
            _warehouse,
            await LoadBalancesAsync(),
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

        result.Snapshot.Strategy.Should().Be(InventoryAllocationStrategyKind.Fefo);
        result.OrderedCandidates.Select(balance => balance.LotId)
            .Should().ContainInOrder(earlierLot.Id, laterLot.Id);
        result.SelectionReasons[earlierBalance.Id]
            .Should().Contain("FEFO expiry date");
    }

    [Fact]
    public async Task FixedLocationAndMinimumShelfLifeRejectCandidatesWithReasons()
    {
        var validLot = new Lot(
            "STRAT-VALID",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(45));
        var shortLot = new Lot(
            "STRAT-SHORT",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(5));
        _context.AddRange(validLot, shortLot);
        _context.SaveChanges();

        var wrongLocation = AddBalance(_firstLocation, validLot.Id, 5m);
        var shortShelf = AddBalance(_secondLocation, shortLot.Id, 5m);
        var valid = AddBalance(_secondLocation, validLot.Id, 5m);
        _context.SaveChanges();
        await AddPolicyAsync(
            InventoryAllocationStrategyKind.FixedLocation,
            fixedLocationId: _secondLocation.Id,
            minimumShelfLifeDays: 30);

        var result = await _service.ResolveAsync(
            Request(4m),
            _item,
            _warehouse,
            await LoadBalancesAsync(),
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

        result.OrderedCandidates.Should().ContainSingle(balance => balance.Id == valid.Id);
        result.RejectedReasons[wrongLocation.Id].Should().Contain("fixed-location");
        result.RejectedReasons[shortShelf.Id].Should().Contain("minimum shelf life");
    }

    [Fact]
    public async Task WholeLicensePlatePreferenceCanMoveACompleteLpnAheadOfAnOlderLooseCandidate()
    {
        var completeLpn = new LicensePlate(
            "STRAT-LPN-1",
            LicensePlateType.Pallet,
            _warehouse.Id,
            _firstLocation.Id);
        var olderLpn = new LicensePlate(
            "STRAT-LPN-2",
            LicensePlateType.Pallet,
            _warehouse.Id,
            _secondLocation.Id);
        _context.AddRange(completeLpn, olderLpn);
        _context.SaveChanges();

        var whole = AddBalance(_firstLocation, null, 6m, completeLpn.Id);
        var olderPartial = AddBalance(_secondLocation, null, 2m, olderLpn.Id);
        _context.SaveChanges();
        AddReceipt(olderPartial, _clock.UtcNow.UtcDateTime.AddDays(-5));
        AddReceipt(whole, _clock.UtcNow.UtcDateTime.AddDays(-1));
        await _context.SaveChangesAsync();
        await AddPolicyAsync(
            InventoryAllocationStrategyKind.Fifo,
            preferWholeLicensePlate: true);

        var result = await _service.ResolveAsync(
            Request(5m),
            _item,
            _warehouse,
            await LoadBalancesAsync(),
            DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime));

        result.OrderedCandidates[0].Id.Should().Be(whole.Id);
        result.SelectionReasons[whole.Id].Should().Contain("whole-LPN preference");
    }

    private async Task AddPolicyAsync(
        InventoryAllocationStrategyKind strategy,
        int? fixedLocationId = null,
        int minimumShelfLifeDays = 0,
        bool preferWholeLicensePlate = false)
    {
        _context.InventoryAllocationStrategyPolicies.Add(
            new InventoryAllocationStrategyPolicy(
                _warehouse.Id,
                $"STRAT-{Guid.NewGuid():N}",
                _item.Id,
                null,
                null,
                strategy,
                fixedLocationId,
                preferWholeLicensePlate,
                minimumShelfLifeDays,
                InventoryAllocationMissingExpiryFallback.Last,
                _clock.UtcNow.UtcDateTime.AddDays(-1)));
        await _context.SaveChangesAsync();
    }

    private InventoryBalance AddBalance(
        Location location,
        int? lotId,
        decimal quantity,
        int? licensePlateId = null)
    {
        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            location.Id,
            _item.Id,
            lotId,
            null,
            null,
            licensePlateId,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        balance.Apply(quantity, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
        return balance;
    }

    private void AddReceipt(InventoryBalance balance, DateTime occurredAtUtc)
    {
        _context.InventoryTransactions.Add(new InventoryTransaction(
            InventoryTransactionType.Receipt,
            balance.GetKey(),
            balance.OnHandQuantity,
            0m,
            balance.OnHandQuantity,
            0m,
            0m,
            0m,
            "test",
            occurredAtUtc,
            $"corr-{Guid.NewGuid():N}",
            $"idem-{Guid.NewGuid():N}",
            $"group-{Guid.NewGuid():N}",
            1));
    }

    private async Task<IReadOnlyList<InventoryBalance>> LoadBalancesAsync() =>
        await _context.InventoryBalances
            .Include(balance => balance.Location)
            .Include(balance => balance.Lot)
            .Include(balance => balance.LicensePlate)
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Item)
            .Include(balance => balance.InventoryStatus)
            .OrderBy(balance => balance.Id)
            .ToListAsync();

    private InventoryReservationRequest Request(decimal quantity) =>
        new(
            "SalesOrderLine",
            "STRAT-ORDER",
            1,
            _warehouse.Id,
            _item.Id,
            quantity,
            ActorUserId: "planner");

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
