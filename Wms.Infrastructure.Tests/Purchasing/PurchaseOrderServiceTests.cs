using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Purchasing;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Purchasing;
using Wms.Infrastructure.Units;

namespace Wms.Infrastructure.Tests.Purchasing;

public sealed class PurchaseOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WmsDbContext _context;
    private readonly PurchaseOrderService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Supplier _supplier;
    private readonly Location _location;

    public PurchaseOrderServiceTests()
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

        _warehouse = new Warehouse("PO-WH", "Purchase Order Warehouse");
        _item = new Item("PO-ITEM", "Purchase Order Widget", "EA");
        _supplier = new Supplier("PO-SUP", "Purchase Order Supplier", defaultCurrencyCode: "USD");
        _context.AddRange(_warehouse, _item, _supplier, new UnitOfMeasure(
            "EA",
            UnitOfMeasureCategory.Count,
            0,
            "ea",
            "Each"));
        _context.SaveChanges();
        _context.WarehouseNumberSequences.Add(new WarehouseNumberSequence(_warehouse.Id));
        _location = new Location(
            "PO-RECEIVE",
            "PO Receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _context.Add(_location);
        _context.SaveChanges();

        var conversion = new UnitOfMeasureService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<UnitOfMeasureService>.Instance);
        _service = new PurchaseOrderService(
            _context,
            _access.Object,
            conversion,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<PurchaseOrderService>.Instance);
    }

    [Fact]
    public async Task CreateAndConfirmAsync_SnapshotsSupplierItemAndConversion()
    {
        var created = await _service.CreateAsync(CreateInput(), "buyer-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.DocumentNumber.Should().Be("PO-PO-WH-000001");
        created.Value.Status.Should().Be(PurchaseOrderStatus.Draft);
        created.Value.SupplierCode.Should().Be("PO-SUP");
        created.Value.Lines.Should().ContainSingle(line =>
            line.ItemSku == "PO-ITEM" &&
            line.OrderedBaseQuantity == 10m &&
            line.BaseUnitOfMeasure == "EA");

        var confirmed = await _service.ConfirmAsync(created.Value.Id, "buyer-1");

        confirmed.IsSuccess.Should().BeTrue(confirmed.Error);
        confirmed.Value.Status.Should().Be(PurchaseOrderStatus.Confirmed);
        confirmed.Value.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmAsync_RejectsSupplierThatBecameInactive()
    {
        var created = await _service.CreateAsync(CreateInput(), "buyer-1");
        _supplier.Deactivate();
        await _context.SaveChangesAsync();

        var confirmed = await _service.ConfirmAsync(created.Value.Id, "buyer-1");

        confirmed.ErrorCode.Should().Be("supplier.inactive");
    }

    [Fact]
    public async Task UpdateAsync_ReplacesDraftLinesAndPreservesDocumentIdentity()
    {
        var created = await _service.CreateAsync(CreateInput(), "buyer-1");

        var updated = await _service.UpdateAsync(
            created.Value.Id,
            CreateInput(orderedQuantity: 12m, notes: "Updated draft"),
            "buyer-1");

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value.DocumentNumber.Should().Be(created.Value.DocumentNumber);
        updated.Value.Notes.Should().Be("Updated draft");
        updated.Value.Lines.Should().ContainSingle(line => line.OrderedQuantity == 12m);
        (await _context.PurchaseOrderLines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReceiptAllocationAsync_TracksPartialAndFullReceiptOnCanonicalMovement()
    {
        var created = await _service.CreateAsync(CreateInput(), "buyer-1");
        await _service.ConfirmAsync(created.Value.Id, "buyer-1");
        var lineId = created.Value.Lines.Single().Id;

        var first = await RecordReceiptAsync(created.Value.Id, lineId, 4m, "receiver-1");
        first.IsSuccess.Should().BeTrue(first.Error);
        var afterFirst = await _service.GetAsync(created.Value.Id);
        afterFirst.Value.Status.Should().Be(PurchaseOrderStatus.PartiallyReceived);
        afterFirst.Value.Lines.Single().ReceivedBaseQuantity.Should().Be(4m);

        var second = await RecordReceiptAsync(created.Value.Id, lineId, 6m, "receiver-1");
        second.IsSuccess.Should().BeTrue(second.Error);
        var afterSecond = await _service.GetAsync(created.Value.Id);
        afterSecond.Value.Status.Should().Be(PurchaseOrderStatus.Received);
        afterSecond.Value.Lines.Single().ReceivedBaseQuantity.Should().Be(10m);
        (await _context.PurchaseOrderReceiptAllocations.CountAsync()).Should().Be(2);
        (await _context.Movements.CountAsync(movement =>
                movement.PurchaseOrderId == created.Value.Id &&
                movement.PurchaseOrderLineId == lineId))
            .Should().Be(2);
    }

    [Fact]
    public async Task ValidateReceiptAsync_RejectsBeyondToleranceAndCancelAfterHistory()
    {
        var created = await _service.CreateAsync(CreateInput(overTolerance: 10m), "buyer-1");
        await _service.ConfirmAsync(created.Value.Id, "buyer-1");
        var lineId = created.Value.Lines.Single().Id;

        var allowed = await _service.ValidateReceiptAsync(
            created.Value.Id,
            lineId,
            _item.Id,
            _warehouse.Id,
            11m);
        var rejected = await _service.ValidateReceiptAsync(
            created.Value.Id,
            lineId,
            _item.Id,
            _warehouse.Id,
            11.1m);

        allowed.IsSuccess.Should().BeTrue(allowed.Error);
        rejected.ErrorCode.Should().Be("purchase_order.over_receipt");

        await RecordReceiptAsync(created.Value.Id, lineId, 1m, "receiver-1");
        var cancelled = await _service.CancelAsync(created.Value.Id, "buyer-1");
        cancelled.ErrorCode.Should().Be("purchase_order.lifecycle_invalid");
    }

    [Fact]
    public async Task CloseAndReopenAsync_PreserveReceiptHistoryAndStatus()
    {
        var created = await _service.CreateAsync(CreateInput(), "buyer-1");
        await _service.ConfirmAsync(created.Value.Id, "buyer-1");
        var lineId = created.Value.Lines.Single().Id;
        await RecordReceiptAsync(created.Value.Id, lineId, 10m, "receiver-1");

        var closed = await _service.CloseAsync(created.Value.Id, "buyer-1");
        var reopened = await _service.ReopenAsync(created.Value.Id, "buyer-1");

        closed.Value.Status.Should().Be(PurchaseOrderStatus.Closed);
        reopened.Value.Status.Should().Be(PurchaseOrderStatus.Received);
        reopened.Value.Lines.Single().IsClosed.Should().BeFalse();
        (await _context.PurchaseOrderReceiptAllocations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_StoresExternalReferenceAndSourceMetadata()
    {
        var header = "WAREHOUSE_ID,SUPPLIER_ID,ORDER_DATE,EXPECTED_RECEIPT_DATE,EXTERNAL_REFERENCE,SOURCE_TYPE,SOURCE_REFERENCE,CURRENCY_CODE,NOTES,ITEM_SKU,ORDERED_QUANTITY,UNIT_OF_MEASURE,OVER_DELIVERY_TOLERANCE_PERCENT,UNDER_DELIVERY_TOLERANCE_PERCENT,SUPPLIER_ITEM_REFERENCE,LINE_NOTES";
        var row = string.Join(",", [
            _warehouse.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _supplier.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "2026-09-20", "2026-09-22", "ERP-PO-1",
            "ERP", "batch-7", "USD", "Imported", "PO-ITEM", "10", "EA", "2", "1", "", "Imported line"]);

        var imported = await _service.ImportAsync($"{header}\n{row}", "integration-1");

        imported.IsSuccess.Should().BeTrue(imported.Error);
        imported.Value.ImportedCount.Should().Be(1);
        var order = await _context.PurchaseOrders.SingleAsync();
        order.ExternalReference.Should().Be("ERP-PO-1");
        order.SourceType.Should().Be("ERP");
        order.SourceReference.Should().Be("batch-7");
    }

    private PurchaseOrderInput CreateInput(
        decimal overTolerance = 0m,
        decimal orderedQuantity = 10m,
        string? notes = null) => new(
        _warehouse.Id,
        _supplier.Id,
        new DateOnly(2026, 9, 20),
        new DateOnly(2026, 9, 22),
        Lines: [new PurchaseOrderLineInput(
            _item.Sku,
            orderedQuantity,
            "EA",
            overTolerance,
            0m)],
        Notes: notes);

    private async Task<Result> RecordReceiptAsync(
        int purchaseOrderId,
        int lineId,
        decimal quantity,
        string userId)
    {
        var plan = await _service.ValidateReceiptAsync(
            purchaseOrderId,
            lineId,
            _item.Id,
            _warehouse.Id,
            quantity);
        if (plan.IsFailure)
        {
            return plan;
        }

        var movement = Movement.CreateReceipt(
            _item.Id,
            _location.Id,
            new Quantity(quantity),
            userId,
            referenceNumber: $"RCV-{quantity}",
            timestampUtc: new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));
        _context.Movements.Add(movement);
        var recorded = await _service.RecordReceiptAsync(plan.Value, movement, userId);
        if (recorded.IsSuccess)
        {
            await _context.SaveChangesAsync();
        }

        return recorded;
    }

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
