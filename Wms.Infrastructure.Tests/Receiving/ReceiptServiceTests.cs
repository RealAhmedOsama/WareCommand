using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Purchasing;
using Wms.Application.Receiving;
using Wms.Application.Units;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Receiving;
using Wms.Infrastructure.Units;

namespace Wms.Infrastructure.Tests.Receiving;

public sealed class ReceiptServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IPurchaseOrderService> _purchaseOrders = new();
    private readonly Mock<IAdvanceShippingNoticeService> _asns = new();
    private readonly Mock<IStockMovementService> _stockMovements = new();
    private readonly Mock<IWarehouseWorkService> _warehouseWork = new();
    private readonly WmsDbContext _context;
    private readonly ReceiptService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Supplier _supplier;
    private readonly Location _receivingLocation;

    public ReceiptServiceTests()
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
        _warehouseWork
            .Setup(service => service.EnsurePutawayForReceiptAsync(
                It.IsAny<PutawayWorkGenerationInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<WarehouseWorkDto>>([]));

        _warehouse = new Warehouse("RCPT-WH", "Receipt Warehouse");
        _item = new Item("RCPT-ITEM", "Receipt Widget", "EA");
        _supplier = new Supplier("RCPT-SUP", "Receipt Supplier", defaultCurrencyCode: "USD");
        _context.AddRange(
            _warehouse,
            _item,
            _supplier,
            new UnitOfMeasure("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"));
        _context.SaveChanges();

        _context.WarehouseNumberSequences.Add(new WarehouseNumberSequence(_warehouse.Id));
        _receivingLocation = new Location(
            "RCPT-RECEIVE",
            "Receipt receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _context.Add(_receivingLocation);
        _context.SaveChanges();

        var conversion = new UnitOfMeasureService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<UnitOfMeasureService>.Instance);
        _service = new ReceiptService(
            _context,
            _access.Object,
            conversion,
            _purchaseOrders.Object,
            _asns.Object,
            _stockMovements.Object,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<ReceiptService>.Instance,
            null,
            _warehouseWork.Object);
    }

    [Fact]
    public async Task OpenAndFinalize_PersistsReceiptBeforeMovementAndCompletesLine()
    {
        var opened = await _service.OpenForReceivingAsync(CreateReceivingInput(5m), "receiver-1");

        opened.IsSuccess.Should().BeTrue(opened.Error);
        opened.Value.DocumentNumber.Should().Be("RCPT-RCPT-WH-000001");

        var receipt = await _context.Receipts
            .Include(value => value.Lines)
            .SingleAsync();
        receipt.Status.Should().Be(ReceiptStatus.Open);
        receipt.Lines.Should().ContainSingle(line => line.Id == opened.Value.ReceiptLineId);

        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(5m),
            "receiver-1",
            referenceNumber: opened.Value.DocumentNumber,
            timestampUtc: DateTime.UtcNow);
        movement.LinkReceipt(opened.Value.ReceiptId, opened.Value.ReceiptLineId);
        _context.Movements.Add(movement);
        await _context.SaveChangesAsync();

        var finalized = await _service.FinalizeReceivingAsync(opened.Value, movement, "receiver-1");
        await _context.SaveChangesAsync();

        finalized.IsSuccess.Should().BeTrue(finalized.Error);
        receipt.Status.Should().Be(ReceiptStatus.Completed);
        receipt.Lines.Single().ReceivedBaseQuantity.Should().Be(5m);
        receipt.Lines.Single().AcceptedBaseQuantity.Should().Be(5m);
        (await _context.ReceiptLineMovements.CountAsync()).Should().Be(1);
        (await _context.Movements.SingleAsync()).ReceiptId.Should().Be(receipt.Id);
        _warehouseWork.Verify(service => service.EnsurePutawayForReceiptAsync(
            It.Is<PutawayWorkGenerationInput>(input =>
                input.ReceiptId == receipt.Id &&
                input.ReceiptLineId == receipt.Lines.Single().Id &&
                input.SourceLocationId == _receivingLocation.Id &&
                input.Quantity == 5m &&
                !input.QualityInspectionPending),
            "receiver-1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenAndFinalize_PreservesExplicitOwnerDimension()
    {
        var owner = new InventoryOwner(
            "EXT-RCPT-OWNER",
            InventoryOwnerKind.ExternalOwner,
            "External receipt owner",
            externalOwnerReference: "EXT-RCPT-OWNER");
        _context.InventoryOwners.Add(owner);
        await _context.SaveChangesAsync();

        var opened = await _service.OpenForReceivingAsync(
            CreateReceivingInput(5m) with
            {
                OwnerKind = InventoryOwnerKind.ExternalOwner,
                InventoryOwnerId = owner.Id,
                OwnerCodeSnapshot = owner.OwnerCode
            },
            "receiver-1");
        opened.IsSuccess.Should().BeTrue(opened.Error);

        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(5m),
            "receiver-1",
            referenceNumber: opened.Value.DocumentNumber,
            timestampUtc: DateTime.UtcNow);
        movement.SetOwnership(
            InventoryOwnerKind.ExternalOwner,
            owner.Id,
            owner.OwnerCode);
        movement.LinkReceipt(opened.Value.ReceiptId, opened.Value.ReceiptLineId);
        _context.Movements.Add(movement);
        await _context.SaveChangesAsync();

        var finalized = await _service.FinalizeReceivingAsync(opened.Value, movement, "receiver-1");
        finalized.IsSuccess.Should().BeTrue(finalized.Error);

        var line = await _context.ReceiptLines.SingleAsync();
        line.OwnerKind.Should().Be(InventoryOwnerKind.ExternalOwner);
        line.InventoryOwnerId.Should().Be(owner.Id);
        line.OwnerCodeSnapshot.Should().Be(owner.OwnerCode);
    }

    [Fact]
    public async Task FinalizeRejectsReceiptLineWithOpenInboundException()
    {
        var opened = await _service.OpenForReceivingAsync(CreateReceivingInput(5m), "receiver-1");
        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(5m),
            "receiver-1",
            referenceNumber: opened.Value.DocumentNumber,
            timestampUtc: DateTime.UtcNow);
        movement.LinkReceipt(opened.Value.ReceiptId, opened.Value.ReceiptLineId);
        _context.Movements.Add(movement);
        _context.InboundExceptions.Add(new InboundException(
            _warehouse.Id,
            "INB-EX-RECEIPT-1",
            "receipt-exception-1",
            InboundExceptionCode.DocumentMismatch,
            InboundExceptionSeverity.High,
            "Source document mismatch",
            receiptId: opened.Value.ReceiptId,
            receiptLineId: opened.Value.ReceiptLineId,
            createdByUserId: "receiver-1"));
        await _context.SaveChangesAsync();

        var finalized = await _service.FinalizeReceivingAsync(opened.Value, movement, "receiver-1");

        finalized.ErrorCode.Should().Be("receipt.inbound_exception_open");
        (await _context.ReceiptLineMovements.CountAsync()).Should().Be(0);
        (await _context.Receipts.SingleAsync()).Status.Should().Be(ReceiptStatus.Open);
    }

    [Fact]
    public async Task FinalizeRejectsMovementThatDoesNotMatchThePersistedReceiptLine()
    {
        var opened = await _service.OpenForReceivingAsync(CreateReceivingInput(5m), "receiver-1");
        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(4m),
            "receiver-1");
        movement.LinkReceipt(opened.Value.ReceiptId, opened.Value.ReceiptLineId);

        var result = await _service.FinalizeReceivingAsync(opened.Value, movement, "receiver-1");

        result.ErrorCode.Should().Be("receipt.movement_mismatch");
        (await _context.Receipts.SingleAsync()).Status.Should().Be(ReceiptStatus.Open);
        (await _context.ReceiptLineMovements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReverseCreatesCompensatingReceiptHistoryAndBlocksSecondReverse()
    {
        var opened = await _service.OpenForReceivingAsync(CreateReceivingInput(5m), "receiver-1");
        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(5m),
            "receiver-1",
            timestampUtc: DateTime.UtcNow);
        movement.LinkReceipt(opened.Value.ReceiptId, opened.Value.ReceiptLineId);
        _context.Movements.Add(movement);
        await _context.SaveChangesAsync();
        (await _service.FinalizeReceivingAsync(opened.Value, movement, "receiver-1")).IsSuccess.Should().BeTrue();
        await _context.SaveChangesAsync();

        _stockMovements
            .Setup(service => service.ReverseReceiptAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Movement>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((int receiptId, int receiptLineId, Movement original, string userId, string reason, CancellationToken _) =>
            {
                var reversal = Movement.CreateAdjustment(
                    _item.Id,
                    _receivingLocation.Id,
                    new Quantity(5m),
                    userId,
                    referenceNumber: opened.Value.DocumentNumber,
                    notes: reason,
                    timestampUtc: DateTime.UtcNow,
                    adjustmentDelta: -5m);
                reversal.LinkReceipt(receiptId, receiptLineId, ReceiptMovementKind.Reversal, original.Id);
                _context.Movements.Add(reversal);
                return Task.FromResult(reversal);
            });

        var reversed = await _service.ReverseAsync(
            opened.Value.ReceiptId,
            "receiver-1",
            "damaged after receipt");

        reversed.IsSuccess.Should().BeTrue(reversed.Error);
        reversed.Value.Status.Should().Be(ReceiptStatus.Reversed);
        reversed.Value.Lines.Single().ReceivedBaseQuantity.Should().Be(0m);
        (await _context.ReceiptLineMovements.CountAsync()).Should().Be(2);
        (await _context.Movements.CountAsync()).Should().Be(2);
        _stockMovements.Verify(service => service.ReverseReceiptAsync(
            opened.Value.ReceiptId,
            opened.Value.ReceiptLineId,
            It.IsAny<Movement>(),
            "receiver-1",
            "damaged after receipt",
            It.IsAny<CancellationToken>()), Times.Once);

        var secondReverse = await _service.ReverseAsync(
            opened.Value.ReceiptId,
            "receiver-1",
            "duplicate reversal");
        secondReverse.ErrorCode.Should().Be("receipt.reversal_invalid");
    }

    private ReceiptReceivingInput CreateReceivingInput(decimal quantity) => new(
        _warehouse.Id,
        _receivingLocation.Id,
        _item.Id,
        _item.Sku,
        _item.Name,
        new Quantity(quantity),
        LotNumber: null,
        ExpiryDate: null,
        SerialNumber: null,
        LicensePlateId: null,
        PurchaseOrderId: null,
        PurchaseOrderLineId: null,
        AdvanceShippingNoticeId: null,
        AdvanceShippingNoticeLineId: null,
        PurchaseOrderPlan: null,
        AdvanceShippingNoticePlan: null,
        ReferenceNumber: "RECEIVE-001",
        Notes: "receipt test",
        SupplierId: _supplier.Id);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
