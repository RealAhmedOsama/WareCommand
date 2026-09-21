using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
using Wms.Infrastructure.InventoryStatuses;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryDispositionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InventoryDispositionService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _location;

    public InventoryDispositionServiceTests()
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

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        var ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        var inventoryStatusService = new InventoryStatusService(
            _unitOfWork,
            _audit.Object,
            _clock,
            NullLogger<InventoryStatusService>.Instance,
            ledger);
        _service = new InventoryDispositionService(
            _context,
            _access.Object,
            _audit.Object,
            _clock,
            inventoryStatusService,
            NullLogger<InventoryDispositionService>.Instance);

        _warehouse = new Warehouse(
            "DSP-WH",
            "Disposition warehouse",
            timeZone: "UTC",
            expiryWarningDays: 3);
        _item = new Item("DSP-ITEM", "Disposition item", "EA");
        _item.SetShelfLife(90);
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();
        _location = new Location(
            "DSP-BIN",
            "Disposition bin",
            _warehouse.Id,
            type: LocationType.Bin,
            isReceivable: false);
        _context.Locations.Add(_location);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ExpiryCandidatesUseBusinessDateWarningAndMinimumShelfLife()
    {
        var shortShelfLot = new Lot(
            "DSP-SHORT",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(4));
        var boundaryLot = new Lot(
            "DSP-BOUNDARY",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime);
        _context.AddRange(shortShelfLot, boundaryLot);
        await _context.SaveChangesAsync();
        _context.InventoryBalances.AddRange(
            new InventoryBalance(new InventoryBalanceKey(
                _warehouse.Id,
                _location.Id,
                _item.Id,
                shortShelfLot.Id,
                null,
                null,
                null,
                InventoryStatusSystemIds.Available,
                _item.UnitOfMeasure)),
            new InventoryBalance(new InventoryBalanceKey(
                _warehouse.Id,
                _location.Id,
                _item.Id,
                boundaryLot.Id,
                null,
                null,
                null,
                InventoryStatusSystemIds.Available,
                _item.UnitOfMeasure)));
        await _context.SaveChangesAsync();
        var balances = await _context.InventoryBalances.ToListAsync();
        balances[0].Apply(5m, 0m, allowNegativeStock: false);
        balances[1].Apply(5m, 0m, allowNegativeStock: false);
        await _context.SaveChangesAsync();

        var policy = await _service.SavePolicyAsync(
            null,
            new InventoryDispositionPolicyInput(
                _warehouse.Id,
                "DSP-EXPIRY",
                "Disposition expiry",
                10,
                _item.Id,
                null,
                WarningDays: 1,
                MinimumShelfLifeDays: 5,
                RequireApprovalForScrap: true,
                RequireWitnessForDestruction: true,
                EffectiveFromUtc: _clock.UtcNow.UtcDateTime.AddDays(-1)),
            "planner");
        policy.IsSuccess.Should().BeTrue(policy.Error);

        var result = await _service.GetExpiryCandidatesAsync(
            new InventoryExpiryCandidateQuery(_warehouse.Id, _clock.UtcNow.UtcDateTime));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().HaveCount(2);
        result.Value.Single(value => value.LotNumber == "DSP-SHORT")
            .Should().Match<InventoryExpiryCandidateDto>(value =>
                value.BusinessDate == new DateOnly(2026, 9, 21) &&
                value.DaysUntilExpiry == 4 &&
                value.BelowMinimumShelfLife &&
                !value.IsExpired);
        result.Value.Single(value => value.LotNumber == "DSP-BOUNDARY")
            .IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task PartialDamageDispositionPreservesDimensionAndWritesLedgerLegs()
    {
        var lot = new Lot(
            "DSP-PARTIAL",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(30));
        _context.Lots.Add(lot);
        await _context.SaveChangesAsync();
        var stock = AddStock(lot.Id, 10m);

        var created = await _service.CreateAsync(
            new InventoryDispositionCreateInput(
                "damage-command-1",
                stock.Id,
                InventoryDispositionKind.Damage,
                3m,
                "Receipt carton damaged"),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.Status.Should().Be(InventoryDispositionStatus.Approved);

        var executed = await _service.ExecuteAsync(created.Value.Id, "operator-1");

        executed.IsSuccess.Should().BeTrue(executed.Error);
        executed.Value.Status.Should().Be(InventoryDispositionStatus.Completed);
        var stocks = await _context.Stock
            .Where(value => value.ItemId == _item.Id && value.LocationId == _location.Id)
            .ToListAsync();
        stocks.Should().HaveCount(2);
        stocks.Single(value => value.InventoryStatusId == InventoryStatusSystemIds.Available)
            .QuantityAvailable.Value.Should().Be(7m);
        stocks.Single(value => value.InventoryStatusId == InventoryStatusSystemIds.Damaged)
            .Should().Match<Stock>(value =>
                value.QuantityAvailable.Value == 3m &&
                value.LotId == lot.Id &&
                value.LocationId == _location.Id);
        (await _context.InventoryTransactions
            .CountAsync(value => value.ReferenceType == "Movement" &&
                                 value.ReferenceId == created.Value.DispositionNumber))
            .Should().Be(2);
    }

    [Fact]
    public async Task ScrapRequiresApprovalAndRepeatedCommandIsIdempotent()
    {
        var stock = AddStock(null, 5m);
        var input = new InventoryDispositionCreateInput(
            "scrap-command-1",
            stock.Id,
            InventoryDispositionKind.Scrap,
            2m,
            "Unrepairable damage");

        var first = await _service.CreateAsync(input, "operator-1");
        var replay = await _service.CreateAsync(input, "operator-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        first.Value.Id.Should().Be(replay.Value.Id);
        first.Value.Status.Should().Be(InventoryDispositionStatus.PendingApproval);
        (await _context.InventoryDispositions.CountAsync()).Should().Be(1);

        var approved = await _service.ApproveAsync(first.Value.Id, "supervisor-1");
        approved.IsSuccess.Should().BeTrue(approved.Error);
        var completed = await _service.ExecuteAsync(first.Value.Id, "supervisor-1");

        completed.IsSuccess.Should().BeTrue(completed.Error);
        completed.Value.Status.Should().Be(InventoryDispositionStatus.Completed);
        (await _context.Stock.SingleAsync(value => value.Id == stock.Id))
            .QuantityAvailable.Value.Should().Be(3m);
        (await _context.Stock.CountAsync(value => value.InventoryStatusId == InventoryStatusSystemIds.ScrapPending))
            .Should().Be(1);
    }

    [Fact]
    public async Task DestructionRequiresWitnessBeforeApprovalWorkflowStarts()
    {
        var stock = AddStock(null, 1m);

        var result = await _service.CreateAsync(
            new InventoryDispositionCreateInput(
                "destruction-command-1",
                stock.Id,
                InventoryDispositionKind.Destruction,
                1m,
                "Regulatory destruction",
                WitnessUserId: null),
            "operator-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inventory.disposition.witness_required");
        (await _context.InventoryDispositions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecallMarksLotAndReturnsCurrentBalanceTrace()
    {
        var lot = new Lot(
            "DSP-RECALL",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(30));
        _context.Lots.Add(lot);
        await _context.SaveChangesAsync();
        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            lot.Id,
            null,
            null,
            null,
            InventoryStatusSystemIds.Available,
            _item.UnitOfMeasure));
        balance.Apply(4m, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
        await _context.SaveChangesAsync();

        var created = await _service.CreateRecallAsync(
            new InventoryRecallCreateInput(
                "recall-command-1",
                _warehouse.Id,
                _item.Id,
                lot.Id,
                null,
                null,
                "Supplier recall"),
            "quality-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        (await _context.Lots.SingleAsync(value => value.Id == lot.Id))
            .Status.Should().Be(LotStatus.Recalled);

        var trace = await _service.GetRecallTraceAsync(created.Value.Id);

        trace.IsSuccess.Should().BeTrue(trace.Error);
        trace.Value.Trace.Should().Contain(value =>
            value.Source == "current-balance" &&
            value.StockBalanceId == balance.Id &&
            value.Quantity == 4m &&
            value.InventoryStatusCode == InventoryStatusCodes.Available);
    }

    private Stock AddStock(int? lotId, decimal quantity)
    {
        var stock = new Stock(
            _item.Id,
            _location.Id,
            new Wms.Domain.ValueObjects.Quantity(quantity),
            lotId,
            inventoryStatusId: InventoryStatusSystemIds.Available);
        _context.Stock.Add(stock);
        _context.SaveChanges();
        return stock;
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
