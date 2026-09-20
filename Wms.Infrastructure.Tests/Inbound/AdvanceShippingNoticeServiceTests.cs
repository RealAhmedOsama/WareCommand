using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Purchasing;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inbound;
using Wms.Infrastructure.Purchasing;
using Wms.Infrastructure.Units;

namespace Wms.Infrastructure.Tests.Inbound;

public sealed class AdvanceShippingNoticeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WmsDbContext _context;
    private readonly PurchaseOrderService _purchaseOrderService;
    private readonly AdvanceShippingNoticeService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Supplier _supplier;
    private readonly Location _receivingLocation;
    private readonly Location _dock;

    public AdvanceShippingNoticeServiceTests()
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

        _warehouse = new Warehouse("ASN-WH", "ASN Warehouse");
        _item = new Item("ASN-ITEM", "ASN Widget", "EA");
        _supplier = new Supplier("ASN-SUP", "ASN Supplier", defaultCurrencyCode: "USD");
        _context.AddRange(
            _warehouse,
            _item,
            _supplier,
            new UnitOfMeasure("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"));
        _context.SaveChanges();

        _context.WarehouseNumberSequences.Add(new WarehouseNumberSequence(_warehouse.Id));
        _receivingLocation = new Location(
            "ASN-RECEIVE",
            "ASN Receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _dock = new Location(
            "ASN-DOCK",
            "ASN Dock",
            _warehouse.Id,
            type: LocationType.Dock,
            isPickable: false,
            isReceivable: false);
        _context.AddRange(_receivingLocation, _dock);
        _context.SaveChanges();

        var conversion = new UnitOfMeasureService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<UnitOfMeasureService>.Instance);
        _purchaseOrderService = new PurchaseOrderService(
            _context,
            _access.Object,
            conversion,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<PurchaseOrderService>.Instance);
        _service = new AdvanceShippingNoticeService(
            _context,
            _access.Object,
            conversion,
            _purchaseOrderService,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<AdvanceShippingNoticeService>.Instance);
    }

    [Fact]
    public async Task ArrivalDoesNotCreateStockAndCompletionWaitsForReceipt()
    {
        var created = await _service.CreateAsync(CreateInput(), "planner-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.DocumentNumber.Should().Be("ASN-ASN-WH-000001");
        created.Value.Status.Should().Be(AdvanceShippingNoticeStatus.Draft);
        created.Value.Lines.Should().ContainSingle(line => line.ExpectedBaseQuantity == 10m);

        var submitted = await _service.SubmitAsync(created.Value.Id, "planner-1");
        var expected = await _service.MarkExpectedAsync(created.Value.Id, "planner-1");
        var arrived = await _service.ArriveAsync(created.Value.Id, _dock.Id, "receiver-1");
        var completed = await _service.CompleteAsync(created.Value.Id, "receiver-1");

        submitted.Value.Status.Should().Be(AdvanceShippingNoticeStatus.Submitted);
        expected.Value.Status.Should().Be(AdvanceShippingNoticeStatus.Expected);
        arrived.Value.Status.Should().Be(AdvanceShippingNoticeStatus.Arrived);
        (await _context.Movements.CountAsync()).Should().Be(0);
        completed.ErrorCode.Should().Be("asn.lifecycle_invalid");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateExternalReferenceAndReservesOpenPurchaseOrderQuantity()
    {
        var purchaseOrder = await _purchaseOrderService.CreateAsync(
            new PurchaseOrderInput(
                _warehouse.Id,
                _supplier.Id,
                new DateOnly(2026, 9, 21),
                Lines: [new PurchaseOrderLineInput(_item.Sku, 10m, "EA")]),
            "buyer-1");
        await _purchaseOrderService.ConfirmAsync(purchaseOrder.Value.Id, "buyer-1");
        var poLineId = purchaseOrder.Value.Lines.Single().Id;

        var first = await _service.CreateAsync(
            CreateInput(
                externalReference: "ERP-ASN-1",
                purchaseOrderId: purchaseOrder.Value.Id,
                purchaseOrderLineId: poLineId,
                quantity: 6m),
            "planner-1");
        var exceedsOpenQuantity = await _service.CreateAsync(
            CreateInput(
                externalReference: "ERP-ASN-2",
                purchaseOrderId: purchaseOrder.Value.Id,
                purchaseOrderLineId: poLineId,
                quantity: 5m),
            "planner-1");
        var duplicate = await _service.CreateAsync(
            CreateInput(
                externalReference: "erp-asn-1"),
            "planner-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        exceedsOpenQuantity.ErrorCode.Should().Be("asn.purchase_order_quantity_exceeded");
        duplicate.ErrorCode.Should().Be("asn.external_reference_conflict");
    }

    [Fact]
    public async Task ValidateReceiptAsync_RequiresExactPreAdvisedLotSerialAndLicensePlate()
    {
        var lotSerialItem = new Item("ASN-TRACKED", "Tracked ASN Widget", "EA", requiresLot: true, requiresSerial: true);
        _context.Add(lotSerialItem);
        var expectedPlate = new LicensePlate("ASN-LPN-EXPECTED", LicensePlateType.Pallet, _warehouse.Id);
        var otherPlate = new LicensePlate("ASN-LPN-OTHER", LicensePlateType.Pallet, _warehouse.Id);
        _context.AddRange(expectedPlate, otherPlate);
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new AdvanceShippingNoticeInput(
                _warehouse.Id,
                _supplier.Id,
                DockLocationId: _dock.Id,
                Lines: [new AdvanceShippingNoticeLineInput(
                    lotSerialItem.Sku,
                    10m,
                    "EA",
                    PreAdvisedLotNumber: "LOT-EXPECTED",
                    PreAdvisedExpiryDate: new DateTime(2027, 9, 21),
                    PreAdvisedSerialNumber: "SERIAL-EXPECTED",
                    ExpectedLicensePlateNumber: expectedPlate.Number)]),
            "planner-1");
        await _service.SubmitAsync(created.Value.Id, "planner-1");
        await _service.MarkExpectedAsync(created.Value.Id, "planner-1");
        await _service.ArriveAsync(created.Value.Id, _dock.Id, "receiver-1");
        var lineId = created.Value.Lines.Single().Id;

        var wrongPlate = await _service.ValidateReceiptAsync(
            created.Value.Id,
            lineId,
            lotSerialItem.Id,
            _warehouse.Id,
            10m,
            "LOT-EXPECTED",
            new DateTime(2027, 9, 21),
            "SERIAL-EXPECTED",
            otherPlate.Id);
        var wrongLot = await _service.ValidateReceiptAsync(
            created.Value.Id,
            lineId,
            lotSerialItem.Id,
            _warehouse.Id,
            10m,
            "LOT-WRONG",
            new DateTime(2027, 9, 21),
            "SERIAL-EXPECTED",
            expectedPlate.Id);
        var valid = await _service.ValidateReceiptAsync(
            created.Value.Id,
            lineId,
            lotSerialItem.Id,
            _warehouse.Id,
            10m,
            "LOT-EXPECTED",
            new DateTime(2027, 9, 21),
            "SERIAL-EXPECTED",
            expectedPlate.Id);

        wrongPlate.ErrorCode.Should().Be("asn.license_plate_mismatch");
        wrongLot.ErrorCode.Should().Be("asn.lot_mismatch");
        valid.IsSuccess.Should().BeTrue(valid.Error);
    }

    [Fact]
    public async Task RecordReceiptAsync_AllocatesCanonicalMovementToAsnAndPurchaseOrderWithoutDuplicatingStock()
    {
        var purchaseOrder = await _purchaseOrderService.CreateAsync(
            new PurchaseOrderInput(
                _warehouse.Id,
                _supplier.Id,
                new DateOnly(2026, 9, 21),
                Lines: [new PurchaseOrderLineInput(_item.Sku, 10m, "EA")]),
            "buyer-1");
        await _purchaseOrderService.ConfirmAsync(purchaseOrder.Value.Id, "buyer-1");
        var poLineId = purchaseOrder.Value.Lines.Single().Id;
        var asn = await _service.CreateAsync(
            CreateInput(purchaseOrderId: purchaseOrder.Value.Id, purchaseOrderLineId: poLineId),
            "planner-1");
        await _service.SubmitAsync(asn.Value.Id, "planner-1");
        await _service.MarkExpectedAsync(asn.Value.Id, "planner-1");
        await _service.ArriveAsync(asn.Value.Id, _dock.Id, "receiver-1");

        var lineId = asn.Value.Lines.Single().Id;
        var plan = await _service.ValidateReceiptAsync(
            asn.Value.Id,
            lineId,
            _item.Id,
            _warehouse.Id,
            4m,
            null,
            null,
            null,
            null);
        var movement = Movement.CreateReceipt(
            _item.Id,
            _receivingLocation.Id,
            new Quantity(4m),
            "receiver-1",
            referenceNumber: "ASN-RECEIPT-1",
            timestampUtc: new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
        _context.Movements.Add(movement);

        var recorded = await _service.RecordReceiptAsync(plan.Value, movement, "receiver-1");
        await _context.SaveChangesAsync();

        recorded.IsSuccess.Should().BeTrue(recorded.Error);
        (await _context.Movements.CountAsync()).Should().Be(1);
        var storedMovement = await _context.Movements.SingleAsync();
        storedMovement.AdvanceShippingNoticeId.Should().Be(asn.Value.Id);
        storedMovement.AdvanceShippingNoticeLineId.Should().Be(lineId);
        storedMovement.PurchaseOrderId.Should().Be(purchaseOrder.Value.Id);
        storedMovement.PurchaseOrderLineId.Should().Be(poLineId);
        (await _context.AdvanceShippingNoticeReceiptAllocations.CountAsync()).Should().Be(1);
        (await _context.PurchaseOrderReceiptAllocations.CountAsync()).Should().Be(1);
        (await _service.GetAsync(asn.Value.Id)).Value.Status.Should().Be(AdvanceShippingNoticeStatus.Receiving);

        var cancelled = await _service.CancelAsync(asn.Value.Id, "planner-1");
        cancelled.ErrorCode.Should().Be("asn.lifecycle_invalid");
    }

    [Fact]
    public async Task AddDiscrepancyAsync_AppendsHistoryAndMarksExceptionPathVisible()
    {
        var created = await _service.CreateAsync(CreateInput(), "planner-1");
        await _service.SubmitAsync(created.Value.Id, "planner-1");
        await _service.MarkExpectedAsync(created.Value.Id, "planner-1");
        await _service.ArriveAsync(created.Value.Id, _dock.Id, "receiver-1");

        var result = await _service.AddDiscrepancyAsync(
            created.Value.Id,
            new AdvanceShippingNoticeDiscrepancyInput(
                created.Value.Lines.Single().Id,
                AdvanceShippingNoticeDiscrepancyKind.QuantityShort,
                10m,
                8m,
                -2m,
                "Two base units were short at receiving."),
            "receiver-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Discrepancies.Should().ContainSingle(discrepancy =>
            discrepancy.Kind == AdvanceShippingNoticeDiscrepancyKind.QuantityShort &&
            discrepancy.VarianceBaseQuantity == -2m);
        (await _context.AdvanceShippingNoticeDiscrepancies.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_SanitizesSecretPayloadAndRejectsDuplicateExternalReference()
    {
        var header = "WAREHOUSE_ID,SUPPLIER_ID,CARRIER_NAME,EXPECTED_FROM_UTC,EXPECTED_TO_UTC,VEHICLE_NUMBER,TRAILER_NUMBER,CONTAINER_NUMBER,TRACKING_REFERENCE,EXTERNAL_REFERENCE,SOURCE_TYPE,SOURCE_REFERENCE,SOURCE_PAYLOAD,DOCK_LOCATION_ID,NOTES,ITEM_SKU,EXPECTED_QUANTITY,UNIT_OF_MEASURE,PACKAGING_CODE,PURCHASE_ORDER_ID,PURCHASE_ORDER_LINE_ID,OVER_DELIVERY_TOLERANCE_PERCENT,UNDER_DELIVERY_TOLERANCE_PERCENT,PRE_ADVISED_LOT_NUMBER,PRE_ADVISED_EXPIRY_DATE,PRE_ADVISED_SERIAL_NUMBER,EXPECTED_LICENSE_PLATE_NUMBER,EXPECTED_LICENSE_PLATE_IS_SSCC,LINE_NOTES";
        var row = string.Join(',', [
            _warehouse.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _supplier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Carrier", "2026-09-21T12:00:00Z", "2026-09-21T13:00:00Z", "TRUCK-1", "", "", "TRACK-1", "ERP-ASN-IMPORT", "ERP", "batch-1", "{\"token\":\"secret-value\"}", _dock.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "Imported", _item.Sku, "10", "EA", "", "", "", "", "", "", "", "", "", "false", "Imported line"]);

        var imported = await _service.ImportAsync($"{header}\n{row}", "integration-1");

        imported.IsSuccess.Should().BeTrue(imported.Error);
        imported.Value.ImportedCount.Should().Be(1);
        var stored = await _context.AdvanceShippingNotices.SingleAsync();
        stored.SourcePayload.Should().Contain("[REDACTED]");
        stored.SourcePayload.Should().NotContain("secret-value");
    }

    private AdvanceShippingNoticeInput CreateInput(
        string? externalReference = null,
        int? purchaseOrderId = null,
        int? purchaseOrderLineId = null,
        decimal quantity = 10m) => new(
        _warehouse.Id,
        _supplier.Id,
        CarrierName: "ASN Carrier",
        ExpectedArrivalFromUtc: new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc),
        ExpectedArrivalToUtc: new DateTime(2026, 9, 21, 14, 0, 0, DateTimeKind.Utc),
        ExternalReference: externalReference,
        DockLocationId: _dock.Id,
        Lines: [new AdvanceShippingNoticeLineInput(
            _item.Sku,
            quantity,
            "EA",
            PurchaseOrderId: purchaseOrderId,
            PurchaseOrderLineId: purchaseOrderLineId)]);

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
