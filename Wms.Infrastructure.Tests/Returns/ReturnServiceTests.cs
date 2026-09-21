using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Returns;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Returns;

namespace Wms.Infrastructure.Tests.Returns;

public sealed class ReturnServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly ReturnService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _returnLocation;
    private readonly Location _storageLocation;

    public ReturnServiceTests()
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

        _warehouse = new Warehouse("RET-WH", "Returns Warehouse");
        _item = new Item("RET-ITEM", "Return Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();

        _returnLocation = new Location(
            "RET-01",
            "Customer returns",
            _warehouse.Id,
            type: LocationType.Returns,
            isPickable: false,
            isReceivable: true);
        _storageLocation = new Location(
            "RET-STORAGE",
            "Restock storage",
            _warehouse.Id,
            type: LocationType.Storage,
            isPickable: true,
            isReceivable: true);
        _context.AddRange(_returnLocation, _storageLocation);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        _service = new ReturnService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _ledger,
            _clock,
            NullLogger<ReturnService>.Instance);
    }

    [Fact]
    public async Task ReturnLifecycle_IsTraceablePendingUntilDisposition_AndIdempotent()
    {
        var created = await _service.CreateAsync(
            new ReturnAuthorizationInput(
                "RMA-1001",
                _warehouse.Id,
                [new ReturnLineInput(_item.Id, 2m)],
                ReturnLocationId: _returnLocation.Id,
                Unplanned: true,
                Reason: "customer changed mind",
                IdempotencyKey: "create-1001"),
            "receiver-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        var line = created.Value.Lines.Single();

        var authorized = await _service.AuthorizeAsync(
            new ReturnCommandInput(created.Value.Id, "authorize-1001"),
            "receiver-1");
        authorized.IsSuccess.Should().BeTrue(authorized.FirstError?.Message);

        var received = await _service.ReceiveAsync(
            new ReturnReceiptInput(
                created.Value.Id,
                line.Id,
                2m,
                InventoryStatusSystemIds.ReturnPending,
                IdempotencyKey: "receive-1001"),
            "receiver-1");
        received.IsSuccess.Should().BeTrue(received.FirstError?.Message);
        received.Value.Status.Should().Be(ReturnStatus.Received);
        received.Value.ReceivedQuantity.Should().Be(2m);
        (await _context.Stock.SingleAsync(stock =>
                stock.ItemId == _item.Id && stock.LocationId == _returnLocation.Id))
            .InventoryStatusId.Should().Be(InventoryStatusSystemIds.ReturnPending);

        var inspecting = await _service.InspectAsync(
            new ReturnCommandInput(created.Value.Id, "inspect-1001", "inspection started"),
            "inspector-1");
        inspecting.IsSuccess.Should().BeTrue(inspecting.FirstError?.Message);
        inspecting.Value.Status.Should().Be(ReturnStatus.Inspecting);

        var replay = await _service.ReceiveAsync(
            new ReturnReceiptInput(
                created.Value.Id,
                line.Id,
                2m,
                InventoryStatusSystemIds.ReturnPending,
                IdempotencyKey: "receive-1001"),
            "receiver-1");
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        (await _context.ReturnReceipts.CountAsync()).Should().Be(1);

        var restocked = await _service.DisposeAsync(
            new ReturnDispositionInput(
                created.Value.Id,
                received.Value.Receipts.Single().Id,
                ReturnDispositionKind.RestockAvailable,
                1m,
                DestinationLocationId: _storageLocation.Id,
                DestinationInventoryStatusId: InventoryStatusSystemIds.Available,
                Reason: "inspection passed",
                IdempotencyKey: "dispose-1001-a"),
            "inspector-1");
        restocked.IsSuccess.Should().BeTrue(restocked.FirstError?.Message);
        restocked.Value.Status.Should().Be(ReturnStatus.Inspecting);

        var scrapped = await _service.DisposeAsync(
            new ReturnDispositionInput(
                created.Value.Id,
                received.Value.Receipts.Single().Id,
                ReturnDispositionKind.Scrap,
                1m,
                Reason: "damaged on inspection",
                IdempotencyKey: "dispose-1001-b"),
            "inspector-1");
        scrapped.IsSuccess.Should().BeTrue(scrapped.FirstError?.Message);
        scrapped.Value.Status.Should().Be(ReturnStatus.Disposed);

        var closed = await _service.CloseAsync(
            new ReturnCommandInput(created.Value.Id, "close-1001"),
            "receiver-1");
        closed.IsSuccess.Should().BeTrue(closed.FirstError?.Message);
        closed.Value.Status.Should().Be(ReturnStatus.Closed);
        closed.Value.DisposedQuantity.Should().Be(2m);

        (await _context.Stock.SingleAsync(stock =>
                stock.ItemId == _item.Id && stock.LocationId == _storageLocation.Id))
            .QuantityAvailable.Value.Should().Be(1m);
        (await _context.Stock.CountAsync(stock =>
                stock.ItemId == _item.Id && stock.LocationId == _returnLocation.Id))
            .Should().Be(0);
        (await _context.Movements.CountAsync()).Should().Be(3);
        (await _context.ReturnCommands.CountAsync(command => command.Operation == "receive"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ReturnRejectsUnplannedPolicyAndOverReceiptAtomically()
    {
        var plannedWithoutShipment = await _service.CreateAsync(
            new ReturnAuthorizationInput(
                "RMA-1002",
                _warehouse.Id,
                [new ReturnLineInput(_item.Id, 1m)],
                ReturnLocationId: _returnLocation.Id,
                Reason: "missing shipment reference",
                IdempotencyKey: "create-1002"),
            "receiver-1");
        plannedWithoutShipment.IsSuccess.Should().BeFalse();
        plannedWithoutShipment.ErrorCode.Should().Be("returns.create_failed");

        var created = await _service.CreateAsync(
            new ReturnAuthorizationInput(
                "RMA-1003",
                _warehouse.Id,
                [new ReturnLineInput(_item.Id, 1m)],
                ReturnLocationId: _returnLocation.Id,
                Unplanned: true,
                Reason: "over receipt test",
                IdempotencyKey: "create-1003"),
            "receiver-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        (await _service.AuthorizeAsync(
                new ReturnCommandInput(created.Value.Id, "authorize-1003"),
                "receiver-1"))
            .IsSuccess.Should().BeTrue();

        var overReceipt = await _service.ReceiveAsync(
            new ReturnReceiptInput(
                created.Value.Id,
                created.Value.Lines.Single().Id,
                2m,
                InventoryStatusSystemIds.ReturnPending,
                IdempotencyKey: "receive-1003"),
            "receiver-1");
        overReceipt.IsSuccess.Should().BeFalse();
        overReceipt.ErrorCode.Should().Be("returns.receive_failed");
        (await _context.ReturnReceipts.CountAsync()).Should().Be(0);
        (await _context.Stock.CountAsync(stock => stock.LocationId == _returnLocation.Id))
            .Should().Be(0);
        (await _context.ReturnAuthorizations.SingleAsync(value => value.Id == created.Value.Id))
            .Status.Should().Be(ReturnStatus.Authorized);
    }

    [Fact]
    public async Task SerialReturnRequiresExactShippedIdentity_AndQuarantinesIt()
    {
        var serialItem = new Item("RET-SERIAL", "Serialized Return Item", "EA", requiresSerial: true);
        _context.Items.Add(serialItem);
        await _context.SaveChangesAsync();
        var serial = new SerialNumber("SN-RET-001", serialItem.Id);
        serial.SetStatus(SerialStatus.Shipped, "shipment completed", _clock.UtcNow.UtcDateTime);
        _context.SerialNumbers.Add(serial);
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new ReturnAuthorizationInput(
                "RMA-1004",
                _warehouse.Id,
                [new ReturnLineInput(serialItem.Id, 1m, ExpectedSerialNumberId: serial.Id)],
                ReturnLocationId: _returnLocation.Id,
                Unplanned: true,
                Reason: "serialized identity test",
                IdempotencyKey: "create-1004"),
            "receiver-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        (await _service.AuthorizeAsync(
                new ReturnCommandInput(created.Value.Id, "authorize-1004"),
                "receiver-1"))
            .IsSuccess.Should().BeTrue();

        var wrongIdentity = await _service.ReceiveAsync(
            new ReturnReceiptInput(
                created.Value.Id,
                created.Value.Lines.Single().Id,
                1m,
                InventoryStatusSystemIds.ReturnPending,
                SerialNumberId: serial.Id,
                SerialNumber: "WRONG",
                IdempotencyKey: "receive-1004-wrong"),
            "receiver-1");
        wrongIdentity.IsSuccess.Should().BeFalse();

        var received = await _service.ReceiveAsync(
            new ReturnReceiptInput(
                created.Value.Id,
                created.Value.Lines.Single().Id,
                1m,
                InventoryStatusSystemIds.ReturnPending,
                SerialNumberId: serial.Id,
                SerialNumber: serial.Number,
                IdempotencyKey: "receive-1004"),
            "receiver-1");
        received.IsSuccess.Should().BeTrue(received.FirstError?.Message);
        (await _context.SerialNumbers.SingleAsync(value => value.Id == serial.Id))
            .Status.Should().Be(SerialStatus.Quarantine);
        (await _context.Stock.SingleAsync(stock => stock.SerialNumberId == serial.Id))
            .QuantityAvailable.Value.Should().Be(1m);
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
