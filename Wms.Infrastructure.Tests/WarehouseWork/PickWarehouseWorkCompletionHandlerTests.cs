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
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Logging;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Services;
using Wms.Infrastructure.WarehouseWork;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.WarehouseWork;

public sealed class PickWarehouseWorkCompletionHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly InventoryReservationService _reservations;
    private readonly PickWarehouseWorkCompletionHandler _handler;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _source;
    private readonly Location _destination;

    public PickWarehouseWorkCompletionHandlerTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("PICK-WH", "Pick Warehouse");
        _item = new Item("PICK-ITEM", "Pick Item", "EA");
        _availableStatus = new InventoryStatus(
            "PICK_AVAILABLE",
            "Pick available",
            "متاح للالتقاط",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _source = new Location(
            "PICK-SOURCE",
            "Pick source",
            _warehouse.Id,
            type: LocationType.Storage,
            isPickable: true);
        _destination = new Location(
            "PICK-STAGE",
            "Pick staging",
            _warehouse.Id,
            type: LocationType.Staging,
            isPickable: false);
        _context.AddRange(_source, _destination);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        _reservations = new InventoryReservationService(
            _unitOfWork,
            _context,
            _ledger,
            _access.Object,
            _clock,
            NullLogger<InventoryReservationService>.Instance);
        var stockMovements = new StockMovementService(
            _unitOfWork,
            NullLogger<StockMovementService>.Instance,
            _audit.Object,
            new WmsRequestContext("PickTest"),
            new WmsOperationContextAccessor(),
            _clock,
            inventoryLedgerService: _ledger);
        _handler = new PickWarehouseWorkCompletionHandler(
            _context,
            _unitOfWork,
            stockMovements,
            _reservations,
            _ledger,
            _clock);
    }

    [Fact]
    public async Task CompletionMovesExactDimensionAndConsumesExactAllocationInOneTransaction()
    {
        var scenario = await CreateScenarioAsync(5m);

        scenario.Work.Assign("picker-1", "PICK", "picker-1", _clock.UtcNow.UtcDateTime);
        scenario.Work.Start("picker-1", _clock.UtcNow.UtcDateTime);

        await _unitOfWork.BeginTransactionAsync();
        var result = await _handler.ExecuteAsync(
            scenario.Work,
            new WarehouseWorkCompletionInput(
                "pick-complete-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        scenario.Line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        5m)
                ]),
            "picker-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        scenario.Work.Lines.Single().RecordActualQuantity(5m);
        scenario.Work.Complete("picker-1", _clock.UtcNow.UtcDateTime);
        await _context.SaveChangesAsync();
        await _unitOfWork.CommitTransactionAsync();

        var sourceStock = await _context.Stock.SingleAsync(stock => stock.LocationId == _source.Id);
        var destinationStock = await _context.Stock.SingleAsync(stock => stock.LocationId == _destination.Id);
        sourceStock.QuantityAvailable.Value.Should().Be(0m);
        destinationStock.QuantityAvailable.Value.Should().Be(5m);

        var allocation = await _context.InventoryReservationAllocations.SingleAsync();
        allocation.ConsumedQuantity.Should().Be(5m);
        allocation.RemainingQuantity.Should().Be(0m);
        var reservation = await _context.InventoryReservations.SingleAsync();
        reservation.Status.Should().Be(InventoryReservationStatus.Consumed);

        var sourceBalance = await _context.InventoryBalances.SingleAsync(
            balance => balance.LocationId == _source.Id);
        var destinationBalance = await _context.InventoryBalances.SingleAsync(
            balance => balance.LocationId == _destination.Id);
        sourceBalance.OnHandQuantity.Should().Be(0m);
        sourceBalance.ReservedQuantity.Should().Be(0m);
        destinationBalance.OnHandQuantity.Should().Be(5m);
        (await _context.Movements.SingleAsync()).ToLocationId.Should().Be(_destination.Id);
        (await _context.InventoryTransactions.CountAsync()).Should().Be(4);
        scenario.Work.Status.Should().Be(WarehouseWorkStatus.Completed);
    }

    [Fact]
    public async Task DuplicateLineScansAreRejectedBeforeAnyStockMutation()
    {
        var scenario = await CreateScenarioAsync(5m);

        var result = await _handler.ExecuteAsync(
            scenario.Work,
            new WarehouseWorkCompletionInput(
                "pick-duplicate-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        scenario.Line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        2m),
                    new WarehouseWorkScanInput(
                        scenario.Line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        3m)
                ]),
            "picker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.pick_scan_lines_invalid");
        (await _context.Movements.CountAsync()).Should().Be(0);
        (await _context.InventoryReservationAllocations.SingleAsync())
            .ConsumedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task PartialLicensePlatePickIsRejectedBeforeMovement()
    {
        var licensePlate = new LicensePlate(
            "PICK-LPN-1",
            LicensePlateType.Pallet,
            _warehouse.Id,
            _source.Id);
        _context.Add(licensePlate);
        await _context.SaveChangesAsync();
        var scenario = await CreateScenarioAsync(5m, licensePlate.Id);

        var result = await _handler.ExecuteAsync(
            scenario.Work,
            new WarehouseWorkCompletionInput(
                "pick-partial-lpn-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        scenario.Line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        2m,
                        LicensePlateId: licensePlate.Id)
                ]),
            "picker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.partial_lpn_not_supported");
        (await _context.Movements.CountAsync()).Should().Be(0);
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(WarehouseWorkEntity Work, WarehouseWorkLine Line)> CreateScenarioAsync(
        decimal quantity,
        int? licensePlateId = null)
    {
        var receiptKey = new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            _item.Id,
            null,
            null,
            null,
            licensePlateId,
            _availableStatus.Id,
            _item.UnitOfMeasure);
        await _ledger.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    receiptKey,
                    quantity,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: $"pick-receipt-{Guid.NewGuid():N}",
                    TransactionGroupId: "pick-receipt-group")
            ]);
        _context.Stock.Add(new Stock(
            _item.Id,
            _source.Id,
            new Quantity(quantity),
            inventoryStatusId: _availableStatus.Id,
            licensePlateId: licensePlateId));
        await _context.SaveChangesAsync();

        var reservation = await _reservations.ReserveAsync(
            new InventoryReservationRequest(
                "SalesOrder",
                $"SO-PICK-{Guid.NewGuid():N}",
                1,
                _warehouse.Id,
                _item.Id,
                quantity,
                Selector: new InventoryReservationSelector(
                    LocationId: _source.Id,
                    LicensePlateId: licensePlateId,
                    InventoryStatusId: _availableStatus.Id,
                    BaseUnitOfMeasure: _item.UnitOfMeasure),
                ActorUserId: "allocator-1",
                CorrelationId: "pick-reservation"));
        var allocation = reservation.Allocations.Single();
        var work = new WarehouseWorkEntity(
            "WORK-PICK-1",
            $"sales-order:pick:{Guid.NewGuid():N}",
            WarehouseWorkType.Pick,
            _warehouse.Id,
            "SalesOrder",
            "SO-PICK",
            notes: "scanner pick test");
        var line = new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            quantity,
            _item.UnitOfMeasure,
            _source.Id,
            _destination.Id,
            inventoryStatusId: _availableStatus.Id,
            licensePlateId: licensePlateId,
            reservationId: reservation.ReservationId,
            reservationAllocationId: allocation.AllocationId);
        work.AddLine(line);
        work.MakeAvailable(_clock.UtcNow.UtcDateTime);
        _context.Add(work);
        await _context.SaveChangesAsync();
        return (work, line);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
