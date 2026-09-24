using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Logging;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Services;
using Wms.Infrastructure.WarehouseWork;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.WarehouseWork;

public sealed class ReplenishmentWarehouseWorkCompletionHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly ReplenishmentWarehouseWorkCompletionHandler _handler;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _source;
    private readonly Location _destination;
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));

    public ReplenishmentWarehouseWorkCompletionHandlerTests()
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

        _warehouse = new Warehouse("REP-WORK-WH", "Replenishment work warehouse");
        _item = new Item("REP-WORK-ITEM", "Replenishment work item", "EA");
        _availableStatus = new InventoryStatus(
            "REP_WORK_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _source = new Location(
            "REP-WORK-SOURCE",
            "Replenishment source",
            _warehouse.Id,
            type: LocationType.Bulk,
            isReceivable: false);
        _destination = new Location(
            "REP-WORK-FACE",
            "Replenishment face",
            _warehouse.Id,
            type: LocationType.PickFace);
        _context.AddRange(_source, _destination);
        _context.SaveChanges();

        _context.Stock.Add(new Stock(
            _item.Id,
            _source.Id,
            new Quantity(5m),
            inventoryStatusId: _availableStatus.Id));
        var sourceBalance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        sourceBalance.Apply(5m, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(sourceBalance);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        var ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        var stockMovement = new StockMovementService(
            _unitOfWork,
            NullLogger<StockMovementService>.Instance,
            _audit.Object,
            new WmsRequestContext("ReplenishmentTest"),
            new WmsOperationContextAccessor(),
            _clock,
            inventoryLedgerService: ledger);
        _handler = new ReplenishmentWarehouseWorkCompletionHandler(
            stockMovement,
            _unitOfWork);
    }

    [Fact]
    public async Task CompletionMovesSourceStockIntoPickFaceAndBalancesLedger()
    {
        var (work, line) = await CreateWorkAsync();
        work.Assign("worker-1", "REPLENISHMENT", "manager-1", _clock.UtcNow.UtcDateTime);
        work.Start("worker-1", _clock.UtcNow.UtcDateTime);

        await _unitOfWork.BeginTransactionAsync();
        var result = await _handler.ExecuteAsync(
            work,
            new WarehouseWorkCompletionInput(
                "replenishment-complete-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        5m,
                        LotId: null,
                        SerialNumberId: null)
                ]),
            "worker-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        line.RecordActualQuantity(5m);
        work.Complete("worker-1", _clock.UtcNow.UtcDateTime);
        await _context.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        (await _context.Stock.SingleAsync(stock => stock.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(0m);
        (await _context.Stock.SingleAsync(stock => stock.LocationId == _destination.Id))
            .QuantityAvailable.Value.Should().Be(5m);
        (await _context.InventoryBalances.SingleAsync(
                balance => balance.LocationId == _source.Id))
            .OnHandQuantity.Should().Be(0m);
        (await _context.InventoryBalances.SingleAsync(
                balance => balance.LocationId == _destination.Id))
            .OnHandQuantity.Should().Be(5m);
        (await _context.Movements.SingleAsync()).Type.Should().Be(MovementType.Putaway);
        work.Status.Should().Be(WarehouseWorkStatus.Completed);
    }

    [Fact]
    public async Task CompletionPreservesExternalOwnerFromPlannedWorkLine()
    {
        var owner = new InventoryOwner(
            "REP-EXTERNAL-OWNER",
            InventoryOwnerKind.ExternalOwner,
            "Replenishment external owner",
            externalOwnerReference: "REP-OWNER-001");
        _context.InventoryOwners.Add(owner);
        await _context.SaveChangesAsync();
        _context.Stock.Remove(await _context.Stock.SingleAsync(stock => stock.LocationId == _source.Id));
        _context.InventoryBalances.Remove(await _context.InventoryBalances.SingleAsync(
            balance => balance.LocationId == _source.Id));
        await _context.SaveChangesAsync();

        _context.Stock.Add(new Stock(
            _item.Id,
            _source.Id,
            new Quantity(5m),
            inventoryStatusId: _availableStatus.Id,
            ownerKind: owner.Kind,
            inventoryOwnerId: owner.Id,
            ownerCodeSnapshot: owner.OwnerCode));
        var sourceBalance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure,
            owner.Kind,
            owner.Id,
            owner.OwnerCode));
        sourceBalance.Apply(5m, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(sourceBalance);
        await _context.SaveChangesAsync();

        var (work, line) = await CreateWorkAsync(owner.Kind, owner.Id, owner.OwnerCode);
        work.Assign("worker-1", "REPLENISHMENT", "manager-1", _clock.UtcNow.UtcDateTime);
        work.Start("worker-1", _clock.UtcNow.UtcDateTime);

        await _unitOfWork.BeginTransactionAsync();
        var result = await _handler.ExecuteAsync(
            work,
            new WarehouseWorkCompletionInput(
                "replenishment-external-owner-complete-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        5m)
                ]),
            "worker-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        line.RecordActualQuantity(5m);
        work.Complete("worker-1", _clock.UtcNow.UtcDateTime);
        await _context.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var destinationStock = await _context.Stock.SingleAsync(stock => stock.LocationId == _destination.Id);
        destinationStock.QuantityAvailable.Value.Should().Be(5m);
        destinationStock.OwnerKind.Should().Be(owner.Kind);
        destinationStock.InventoryOwnerId.Should().Be(owner.Id);
        destinationStock.OwnerCodeSnapshot.Should().Be(owner.OwnerCode);
        var movement = await _context.Movements.SingleAsync();
        movement.OwnerKind.Should().Be(owner.Kind);
        movement.InventoryOwnerId.Should().Be(owner.Id);
        movement.OwnerCodeSnapshot.Should().Be(owner.OwnerCode);
        var ledgerEntries = await _context.InventoryTransactions
            .Where(value => value.ReferenceId == work.WorkNumber)
            .ToArrayAsync();
        ledgerEntries.Should().HaveCount(2);
        ledgerEntries.Should().OnlyContain(value =>
            value.OwnerKind == owner.Kind &&
            value.InventoryOwnerId == owner.Id &&
            value.OwnerCodeSnapshot == owner.OwnerCode &&
            value.ActorUserId == "worker-1");
    }

    [Fact]
    public async Task DuplicateLineScansAreRejectedBeforeAnyMovement()
    {
        var (work, line) = await CreateWorkAsync();

        var result = await _handler.ExecuteAsync(
            work,
            new WarehouseWorkCompletionInput(
                "replenishment-invalid-1",
                Scans:
                [
                    new WarehouseWorkScanInput(line.Id, _item.Id, _source.Id, _destination.Id, 5m),
                    new WarehouseWorkScanInput(line.Id, _item.Id, _source.Id, _destination.Id, 5m)
                ]),
            "worker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.replenishment_scan_lines_invalid");
        (await _context.Movements.CountAsync()).Should().Be(0);
        (await _context.Stock.SingleAsync(stock => stock.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(5m);
    }

    private async Task<(WarehouseWorkEntity Work, WarehouseWorkLine Line)> CreateWorkAsync(
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        var work = new WarehouseWorkEntity(
            "WORK-REP-EXEC",
            $"replenishment-test-{Guid.NewGuid():N}",
            WarehouseWorkType.Replenishment,
            _warehouse.Id,
            "InventoryReplenishmentPolicy",
            "1");
        var line = new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            5m,
            _item.UnitOfMeasure,
            _source.Id,
            _destination.Id,
            inventoryStatusId: _availableStatus.Id,
            ownerKind: ownerKind,
            inventoryOwnerId: inventoryOwnerId,
            ownerCodeSnapshot: ownerCodeSnapshot);
        work.AddLine(line);
        work.MakeAvailable(_clock.UtcNow.UtcDateTime);
        _context.WarehouseWorks.Add(work);
        await _context.SaveChangesAsync();
        return (work, line);
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
