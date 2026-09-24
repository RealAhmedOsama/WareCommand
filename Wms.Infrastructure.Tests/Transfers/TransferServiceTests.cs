using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Transfers;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Transfers;

namespace Wms.Infrastructure.Tests.Transfers;

public sealed class TransferServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly TransferService _service;
    private readonly Warehouse _sourceWarehouse;
    private readonly Warehouse _destinationWarehouse;
    private readonly Item _item;
    private readonly Location _source;
    private readonly Location _transit;
    private readonly Location _destination;
    private readonly Location _internalDestination;

    public TransferServiceTests()
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

        _sourceWarehouse = new Warehouse("TR-SOURCE", "Transfer source");
        _destinationWarehouse = new Warehouse("TR-DEST", "Transfer destination");
        _item = new Item("TR-ITEM", "Transfer item", "EA");
        _context.AddRange(_sourceWarehouse, _destinationWarehouse, _item);
        _context.SaveChanges();

        _source = new Location("TR-SRC-01", "Source storage", _sourceWarehouse.Id, type: LocationType.Storage);
        _transit = new Location("TR-TRANSIT", "Source transit", _sourceWarehouse.Id, type: LocationType.Transit, isPickable: false);
        _destination = new Location("TR-DST-01", "Destination storage", _destinationWarehouse.Id, type: LocationType.Storage);
        _internalDestination = new Location("TR-SRC-02", "Second source storage", _sourceWarehouse.Id, type: LocationType.Storage);
        _context.AddRange(_source, _transit, _destination, _internalDestination);
        _context.SaveChanges();

        _context.Stock.Add(new Stock(_item.Id, _source.Id, new Quantity(5m)));
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(_unitOfWork, _context, _access.Object, _clock);
        _service = new TransferService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _ledger,
            _clock,
            NullLogger<TransferService>.Instance);
    }

    [Fact]
    public async Task CrossWarehouseTransfer_PreservesTransitBalance_AllowsPartialReceipt_AndReplaysCommands()
    {
        var created = await _service.CreateAsync(
            new TransferOrderInput(
                "TR-1001",
                "create-tr-1001",
                _sourceWarehouse.Id,
                _destinationWarehouse.Id,
                _transit.Id,
                [new TransferLineInput(
                    _item.Id,
                    5m,
                    "EA",
                    _source.Id,
                    _destination.Id)]),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        var lineId = created.Value.Lines.Single().Id;

        (await _service.ConfirmAsync(new TransferCommandInput(created.Value.Id, "confirm-tr-1001"), "operator-1"))
            .IsSuccess.Should().BeTrue();
        (await _service.ReleaseAsync(new TransferCommandInput(created.Value.Id, "release-tr-1001"), "operator-1"))
            .IsSuccess.Should().BeTrue();

        var shipped = await _service.ShipAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 5m, "ship-tr-1001"),
            "operator-1");
        shipped.IsSuccess.Should().BeTrue(shipped.FirstError?.Message);
        shipped.Value.Status.Should().Be(TransferOrderStatus.InTransit);
        (await _context.Stock.SingleAsync(value => value.LocationId == _transit.Id))
            .InventoryStatusId.Should().Be(InventoryStatusSystemIds.InTransit);

        var firstReceipt = await _service.ReceiveAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 2m, "receive-tr-1001-a"),
            "operator-1");
        firstReceipt.IsSuccess.Should().BeTrue(firstReceipt.FirstError?.Message);
        firstReceipt.Value.Status.Should().Be(TransferOrderStatus.PartiallyReceived);

        var replay = await _service.ReceiveAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 2m, "receive-tr-1001-a"),
            "operator-1");
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        (await _context.Stock.SingleAsync(value => value.LocationId == _destination.Id))
            .QuantityAvailable.Value.Should().Be(2m);

        var finalReceipt = await _service.ReceiveAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 3m, "receive-tr-1001-b"),
            "operator-1");
        finalReceipt.IsSuccess.Should().BeTrue(finalReceipt.FirstError?.Message);
        finalReceipt.Value.Status.Should().Be(TransferOrderStatus.Received);

        var closed = await _service.CloseAsync(
            new TransferCommandInput(created.Value.Id, "close-tr-1001"),
            "operator-1");
        closed.IsSuccess.Should().BeTrue(closed.FirstError?.Message);
        closed.Value.Status.Should().Be(TransferOrderStatus.Closed);

        (await _context.Stock.CountAsync(value => value.LocationId == _source.Id)).Should().Be(0);
        (await _context.Stock.CountAsync(value => value.LocationId == _transit.Id)).Should().Be(0);
        (await _context.Stock.SingleAsync(value => value.LocationId == _destination.Id))
            .QuantityAvailable.Value.Should().Be(5m);
        (await _ledger.ReconcileAsync()).IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task InternalMovement_IsBalancedAndIdempotent_WithinOneWarehouse()
    {
        var result = await _service.MoveAsync(
            new InternalMovementInput(
                _sourceWarehouse.Id,
                _item.Id,
                2m,
                "EA",
                _source.Id,
                _internalDestination.Id,
                "internal-tr-1002"),
            "operator-1");

        result.IsSuccess.Should().BeTrue(result.FirstError?.Message);
        result.Value.Status.Should().Be(InternalMovementStatus.Completed);

        var replay = await _service.MoveAsync(
            new InternalMovementInput(
                _sourceWarehouse.Id,
                _item.Id,
                2m,
                "EA",
                _source.Id,
                _internalDestination.Id,
                "internal-tr-1002"),
            "operator-1");
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        (await _context.InternalMovements.CountAsync()).Should().Be(1);
        (await _context.Stock.SingleAsync(value => value.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(3m);
        (await _context.Stock.SingleAsync(value => value.LocationId == _internalDestination.Id))
            .QuantityAvailable.Value.Should().Be(2m);
        (await _ledger.ReconcileAsync()).IsBalanced.Should().BeTrue();
    }

    [Fact]
    public async Task InternalMovement_ReportsPersistenceConcurrencyAsRetryableConflict()
    {
        var conflict = new ConcurrencyConflictException(nameof(Stock), $"Id={_item.Id}");
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork
            .Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(conflict);
        unitOfWork
            .Setup(value => value.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new TransferService(
            _context,
            unitOfWork.Object,
            _access.Object,
            _audit.Object,
            _ledger,
            _clock,
            NullLogger<TransferService>.Instance);
        var result = await service.MoveAsync(
            new InternalMovementInput(
                _sourceWarehouse.Id,
                _item.Id,
                1m,
                "EA",
                _source.Id,
                _internalDestination.Id,
                "internal-tr-concurrency"),
            "operator-1");

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Type.Should().Be(ErrorType.Concurrency);
        result.FirstError.Code.Should().Be(conflict.Code);
        result.FirstError.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task TransferRejectsDestinationWarehouseMismatch_BeforeChangingStock()
    {
        var result = await _service.CreateAsync(
            new TransferOrderInput(
                "TR-1003",
                "create-tr-1003",
                _sourceWarehouse.Id,
                _destinationWarehouse.Id,
                _transit.Id,
                [new TransferLineInput(_item.Id, 1m, "EA", _source.Id, _source.Id)]),
            "operator-1");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("transfer.create_failed");
        (await _context.Stock.SingleAsync(value => value.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(5m);
        (await _context.TransferOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TransferShipRejectsLotThatIsNotAllocationEligible()
    {
        var lotItem = new Item("TR-LOT-ITEM", "Lot transfer item", "EA", requiresLot: true);
        _context.Items.Add(lotItem);
        await _context.SaveChangesAsync();
        var lot = new Lot("TR-LOT-HOLD", lotItem.Id, status: LotStatus.Hold);
        _context.Lots.Add(lot);
        await _context.SaveChangesAsync();
        _context.Stock.Add(new Stock(lotItem.Id, _source.Id, new Quantity(3m), lot.Id));
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new TransferOrderInput(
                "TR-LOT-1001",
                "create-tr-lot-1001",
                _sourceWarehouse.Id,
                _destinationWarehouse.Id,
                _transit.Id,
                [new TransferLineInput(
                    lotItem.Id,
                    3m,
                    "EA",
                    _source.Id,
                    _destination.Id,
                    LotId: lot.Id)]),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        var lineId = created.Value.Lines.Single().Id;
        (await _service.ConfirmAsync(
                new TransferCommandInput(created.Value.Id, "confirm-tr-lot-1001"),
                "operator-1"))
            .IsSuccess.Should().BeTrue();
        (await _service.ReleaseAsync(
                new TransferCommandInput(created.Value.Id, "release-tr-lot-1001"),
                "operator-1"))
            .IsSuccess.Should().BeTrue();

        var shipped = await _service.ShipAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 1m, "ship-tr-lot-1001"),
            "operator-1");

        shipped.IsSuccess.Should().BeFalse();
        shipped.ErrorCode.Should().Be("transfer.ship_failed");
        (await _context.Stock.SingleAsync(value =>
                value.ItemId == lotItem.Id && value.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(3m);
    }

    [Fact]
    public async Task SerializedTransferPreservesPersistedIdentityThroughTransitAndReceipt()
    {
        var serialItem = new Item("TR-SERIAL-ITEM", "Serialized transfer item", "EA", requiresSerial: true);
        _context.Items.Add(serialItem);
        await _context.SaveChangesAsync();
        var serial = new SerialNumber("TR-SN-001", serialItem.Id);
        _context.SerialNumbers.Add(serial);
        await _context.SaveChangesAsync();
        serial.RecordReceipt(
            _sourceWarehouse.Id,
            _source.Id,
            lotId: null,
            referenceNumber: "receipt-tr-serial-1001",
            quarantine: false,
            _clock.UtcNow.UtcDateTime);
        _context.Stock.Add(new Stock(
            serialItem.Id,
            _source.Id,
            new Quantity(1m),
            serialNumber: serial.Number,
            serialNumberId: serial.Id));
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new TransferOrderInput(
                "TR-SERIAL-1001",
                "create-tr-serial-1001",
                _sourceWarehouse.Id,
                _destinationWarehouse.Id,
                _transit.Id,
                [new TransferLineInput(
                    serialItem.Id,
                    1m,
                    "EA",
                    _source.Id,
                    _destination.Id,
                    SerialNumberId: serial.Id,
                    SerialNumber: serial.Number)]),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        var lineId = created.Value.Lines.Single().Id;
        (await _service.ConfirmAsync(
                new TransferCommandInput(created.Value.Id, "confirm-tr-serial-1001"),
                "operator-1"))
            .IsSuccess.Should().BeTrue();
        (await _service.ReleaseAsync(
                new TransferCommandInput(created.Value.Id, "release-tr-serial-1001"),
                "operator-1"))
            .IsSuccess.Should().BeTrue();

        var shipped = await _service.ShipAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 1m, "ship-tr-serial-1001"),
            "operator-1");
        shipped.IsSuccess.Should().BeTrue(shipped.FirstError?.Message);
        (await _context.Stock.SingleAsync(value => value.LocationId == _transit.Id))
            .SerialNumberId.Should().Be(serial.Id);

        var received = await _service.ReceiveAsync(
            new TransferQuantityCommandInput(created.Value.Id, lineId, 1m, "receive-tr-serial-1001"),
            "operator-1");
        received.IsSuccess.Should().BeTrue(received.FirstError?.Message);
        (await _context.Stock.SingleAsync(value => value.LocationId == _destination.Id))
            .SerialNumberId.Should().Be(serial.Id);
        (await _context.SerialNumbers.SingleAsync(value => value.Id == serial.Id))
            .CurrentLocationId.Should().Be(_destination.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
