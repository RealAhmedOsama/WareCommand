using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.SupplierReturns;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.SupplierReturns;
using Wms.Infrastructure.WarehouseWork;

namespace Wms.Infrastructure.Tests.SupplierReturns;

public sealed class SupplierReturnServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly WarehouseWorkService _warehouseWork;
    private readonly SupplierReturnService _service;
    private readonly Warehouse _warehouse;
    private readonly Supplier _supplier;
    private readonly Item _item;
    private readonly Location _sourceLocation;
    private readonly Location _stagingLocation;

    public SupplierReturnServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(value => value.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access
            .Setup(value => value.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(value => value.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("RTV-WH", "Return Warehouse");
        _supplier = new Supplier("RTV-SUP", "Return Supplier");
        _item = new Item("RTV-ITEM", "Return Item", "EA");
        _context.AddRange(_warehouse, _supplier, _item);
        _context.SaveChanges();

        _sourceLocation = new Location(
            "RTV-QA",
            "Return quality hold",
            _warehouse.Id,
            type: LocationType.Quarantine,
            isPickable: false,
            isReceivable: false);
        _stagingLocation = new Location(
            "RTV-STAGE",
            "Supplier return staging",
            _warehouse.Id,
            type: LocationType.Staging,
            isPickable: false,
            isReceivable: true);
        _context.AddRange(_sourceLocation, _stagingLocation);
        _context.SaveChanges();

        _context.Stock.Add(new Stock(
            _item.Id,
            _sourceLocation.Id,
            new Wms.Domain.ValueObjects.Quantity(10m),
            inventoryStatusId: InventoryStatusSystemIds.ReturnPending));
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        var workHandler = new SupplierReturnWarehouseWorkCompletionHandler(
            _context,
            _unitOfWork,
            _ledger,
            _clock);
        _warehouseWork = new WarehouseWorkService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _clock,
            [workHandler],
            NullLogger<WarehouseWorkService>.Instance);
        _service = new SupplierReturnService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _ledger,
            _warehouseWork,
            _clock,
            NullLogger<SupplierReturnService>.Instance);
    }

    [Fact]
    public async Task ReturnLifecycle_ReservesMovesShipsExactlyOnceAndCloses()
    {
        var created = await _service.CreateAsync(CreateInput(4m, "AUTH-1"), "creator-1");
        created.IsSuccess.Should().BeTrue(created.Error);

        var approved = await _service.ApproveAsync(
            new SupplierReturnCommandInput(created.Value.Id, "approve-1"),
            "approver-1");
        approved.IsSuccess.Should().BeTrue(approved.Error);
        approved.Value.Status.Should().Be(SupplierReturnStatus.Approved);

        var reserved = await _context.Stock.SingleAsync(value => value.LocationId == _sourceLocation.Id);
        reserved.QuantityReserved.Value.Should().Be(4m);
        var reservation = await _context.InventoryReservations
            .Include(value => value.Allocations)
            .SingleAsync();
        reservation.DemandType.Should().Be(Wms.Application.Auditing.WmsAuditEntityTypes.SupplierReturn);
        reservation.DemandId.Should().Be("RTV-1");
        reservation.Status.Should().Be(InventoryReservationStatus.Reserved);
        reservation.Allocations.Should().ContainSingle(allocation =>
            allocation.LocationId == _sourceLocation.Id &&
            allocation.AllocatedQuantity == 4m &&
            allocation.RemainingQuantity == 4m);
        (await _context.InventoryTransactions
                .Where(value => value.ReferenceId == "RTV-1")
                .SumAsync(value => value.ReservedQuantityDelta))
            .Should().Be(4m);

        var released = await _service.ReleaseAsync(
            new SupplierReturnCommandInput(created.Value.Id, "release-1"),
            "planner-1");
        released.IsSuccess.Should().BeTrue(released.Error);
        released.Value.Status.Should().Be(SupplierReturnStatus.Picking);
        released.Value.WarehouseWorkId.Should().NotBeNull();

        var workId = released.Value.WarehouseWorkId!.Value;
        var assigned = await _warehouseWork.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput("picker-1", null, "assign-1"),
            "planner-1");
        assigned.IsSuccess.Should().BeTrue(assigned.Error);
        var started = await _warehouseWork.StartAsync(
            workId,
            new WarehouseWorkCommandInput("start-1"),
            "picker-1");
        started.IsSuccess.Should().BeTrue(started.Error);

        var workLine = await _context.WarehouseWorkLines.SingleAsync();
        var completed = await _warehouseWork.CompleteAsync(
            workId,
            new WarehouseWorkCompletionInput(
                "complete-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        workLine.Id,
                        _item.Id,
                        _sourceLocation.Id,
                        _stagingLocation.Id,
                        4m)
                ]),
            "picker-1");
        completed.IsSuccess.Should().BeTrue(completed.Error);
        completed.Value.Status.Should().Be(WarehouseWorkStatus.Completed);
        reservation = await _context.InventoryReservations
            .Include(value => value.Allocations)
            .SingleAsync();
        reservation.Status.Should().Be(InventoryReservationStatus.Consumed);
        reservation.Allocations.Single().ConsumedQuantity.Should().Be(4m);
        reservation.Allocations.Single().ReleasedQuantity.Should().Be(0m);

        var packed = await _service.PackAsync(
            new SupplierReturnCommandInput(created.Value.Id, "pack-1"),
            "packer-1");
        packed.IsSuccess.Should().BeTrue(packed.Error);
        packed.Value.Status.Should().Be(SupplierReturnStatus.Packed);

        var shipInput = new SupplierReturnShipInput(
            created.Value.Id,
            "UPS",
            "1Z-RTV-1",
            "BOL-1",
            "ship-1");
        var shipped = await _service.ShipAsync(shipInput, "shipper-1");
        shipped.IsSuccess.Should().BeTrue(shipped.Error);
        shipped.Value.Status.Should().Be(SupplierReturnStatus.Shipped);
        shipped.Value.ShippedQuantity.Should().Be(4m);

        var movementCount = await _context.Movements.CountAsync();
        var duplicate = await _service.ShipAsync(shipInput, "shipper-1");
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.ShippedQuantity.Should().Be(4m);
        (await _context.Movements.CountAsync()).Should().Be(movementCount);

        var acknowledged = await _service.AcknowledgeAsync(
            new SupplierReturnCommandInput(created.Value.Id, "ack-1"),
            "receiver-1");
        acknowledged.IsSuccess.Should().BeTrue(acknowledged.Error);
        var closed = await _service.CloseAsync(
            new SupplierReturnCommandInput(created.Value.Id, "close-1"),
            "receiver-1");
        closed.IsSuccess.Should().BeTrue(closed.Error);
        closed.Value.Status.Should().Be(SupplierReturnStatus.Closed);

        (await _context.Stock.SingleAsync(value => value.LocationId == _sourceLocation.Id))
            .QuantityReserved.Value.Should().Be(0m);
        var staged = await _context.Stock.SingleAsync(value => value.LocationId == _stagingLocation.Id);
        staged.InventoryStatusId.Should().Be(InventoryStatusSystemIds.ReturnPending);
        staged.QuantityAvailable.Value.Should().Be(0m);
    }

    [Fact]
    public async Task ApprovalRejectsOverEligibleQuantityAndDuplicateAuthorization()
    {
        var over = await _service.CreateAsync(CreateInput(11m, "AUTH-OVER", "RTV-OVER"), "creator-1");
        over.IsSuccess.Should().BeTrue(over.Error);
        var rejected = await _service.ApproveAsync(
            new SupplierReturnCommandInput(over.Value.Id, "approve-over"),
            "approver-1");
        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("supplier_return.approve_invalid");

        var first = await _service.CreateAsync(CreateInput(2m, "AUTH-DUP", "RTV-DUP-1"), "creator-1");
        first.IsSuccess.Should().BeTrue(first.Error);
        var second = await _service.CreateAsync(CreateInput(2m, "AUTH-DUP", "RTV-DUP-2"), "creator-1");
        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("supplier_return.authorization_duplicate");
    }

    [Fact]
    public async Task SupervisorApprovedShortPickShipsOnlyTheStagedQuantity()
    {
        var created = await _service.CreateAsync(CreateInput(4m, "AUTH-SHORT", "RTV-SHORT"), "creator-1");
        var approved = await _service.ApproveAsync(
            new SupplierReturnCommandInput(created.Value.Id, "approve-short"),
            "approver-1");
        var released = await _service.ReleaseAsync(
            new SupplierReturnCommandInput(created.Value.Id, "release-short"),
            "planner-1");
        var workId = released.Value.WarehouseWorkId!.Value;
        await _warehouseWork.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput("picker-short", null, "assign-short"),
            "planner-1");
        await _warehouseWork.StartAsync(
            workId,
            new WarehouseWorkCommandInput("start-short"),
            "picker-short");

        var workLine = await _context.WarehouseWorkLines.SingleAsync();
        var completed = await _warehouseWork.CompleteAsync(
            workId,
            new WarehouseWorkCompletionInput(
                "complete-short",
                SupervisorOverride: true,
                OverrideReason: "source shortage accepted by supervisor",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        workLine.Id,
                        _item.Id,
                        _sourceLocation.Id,
                        _stagingLocation.Id,
                        3m)
                ]),
            "picker-short");
        completed.IsSuccess.Should().BeTrue(completed.Error);

        var reservation = await _context.InventoryReservations
            .Include(value => value.Allocations)
            .SingleAsync();
        reservation.Status.Should().Be(InventoryReservationStatus.PartiallyConsumed);
        reservation.Allocations.Single().ConsumedQuantity.Should().Be(3m);
        reservation.Allocations.Single().ReleasedQuantity.Should().Be(1m);
        reservation.Allocations.Single().RemainingQuantity.Should().Be(0m);

        var packed = await _service.PackAsync(
            new SupplierReturnCommandInput(created.Value.Id, "pack-short"),
            "packer-1");
        packed.IsSuccess.Should().BeTrue(packed.Error);
        packed.Value.Lines.Single().ApprovedBaseQuantity.Should().Be(3m);
        packed.Value.Lines.Single().RemainingToStage.Should().Be(0m);

        var shipped = await _service.ShipAsync(
            new SupplierReturnShipInput(
                created.Value.Id,
                "UPS",
                "1Z-SHORT",
                "BOL-SHORT",
                "ship-short"),
            "shipper-1");
        shipped.IsSuccess.Should().BeTrue(shipped.Error);
        shipped.Value.ShippedQuantity.Should().Be(3m);
    }

    [Fact]
    public async Task ExceptionTransitionRequiresReasonAndIsIdempotent()
    {
        var created = await _service.CreateAsync(CreateInput(2m, "AUTH-EXCEPTION", "RTV-EXCEPTION"), "creator-1");
        var missingReason = await _service.ExceptionAsync(
            new SupplierReturnCommandInput(created.Value.Id, "exception-missing"),
            "manager-1");
        missingReason.IsFailure.Should().BeTrue();
        missingReason.ErrorCode.Should().Be("supplier_return.exception_reason_required");

        var input = new SupplierReturnCommandInput(
            created.Value.Id,
            "exception-1",
            "source disposition requires manual review");
        var exception = await _service.ExceptionAsync(input, "manager-1");
        exception.IsSuccess.Should().BeTrue(exception.Error);
        exception.Value.Status.Should().Be(SupplierReturnStatus.Exception);

        var replay = await _service.ExceptionAsync(input, "manager-1");
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Status.Should().Be(SupplierReturnStatus.Exception);
    }

    [Fact]
    public async Task ApprovedReturnCancellationReleasesReservation()
    {
        var created = await _service.CreateAsync(CreateInput(3m, "AUTH-CANCEL"), "creator-1");
        var approved = await _service.ApproveAsync(
            new SupplierReturnCommandInput(created.Value.Id, "approve-cancel"),
            "approver-1");
        approved.IsSuccess.Should().BeTrue(approved.Error);

        var cancelled = await _service.CancelAsync(
            new SupplierReturnCommandInput(
                created.Value.Id,
                "cancel-1",
                "supplier withdrew authorization"),
            "approver-1");
        cancelled.IsSuccess.Should().BeTrue(cancelled.Error);
        cancelled.Value.Status.Should().Be(SupplierReturnStatus.Cancelled);
        var reservation = await _context.InventoryReservations
            .Include(value => value.Allocations)
            .SingleAsync();
        reservation.Status.Should().Be(InventoryReservationStatus.Released);
        reservation.Allocations.Single().ReleasedQuantity.Should().Be(3m);
        reservation.Allocations.Single().RemainingQuantity.Should().Be(0m);
        (await _context.Stock.SingleAsync(value => value.LocationId == _sourceLocation.Id))
            .QuantityReserved.Value.Should().Be(0m);
        (await _context.InventoryTransactions
                .Where(value => value.ReferenceId == "RTV-1")
                .SumAsync(value => value.ReservedQuantityDelta))
            .Should().Be(0m);
    }

    private SupplierReturnCreateInput CreateInput(
        decimal quantity,
        string authorization,
        string returnNumber = "RTV-1") =>
        new(
            returnNumber,
            _warehouse.Id,
            _supplier.Id,
            _stagingLocation.Id,
            [new SupplierReturnLineInput(
                _item.Id,
                quantity,
                "EA",
                _sourceLocation.Id,
                InventoryStatusSystemIds.ReturnPending,
                "quality return")],
            "QC_DISPOSITION",
            "supplier return",
            SupplierAuthorizationReference: authorization);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
