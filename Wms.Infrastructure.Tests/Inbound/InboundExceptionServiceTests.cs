using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inbound;

namespace Wms.Infrastructure.Tests.Inbound;

public sealed class InboundExceptionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly InboundExceptionService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _available;
    private readonly Location _receiving;
    private readonly Location _staging;
    private readonly Receipt _receipt;
    private readonly ReceiptLine _line;

    public InboundExceptionServiceTests()
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

        _service = new InboundExceptionService(
            _context,
            _access.Object,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<InboundExceptionService>.Instance);

        _warehouse = new Warehouse("EX-WH", "Exception Warehouse");
        _item = new Item("EX-ITEM", "Exception Item", "EA");
        _available = new InventoryStatus(
            InventoryStatusCodes.Available,
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            isSystem: true);
        _context.AddRange(_warehouse, _item, _available);
        _context.SaveChanges();

        _receiving = new Location(
            "EX-RECEIVE",
            "Exception receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _staging = new Location(
            "EX-STAGE",
            "Exception staging",
            _warehouse.Id,
            type: LocationType.Staging,
            isPickable: false,
            isReceivable: false);
        _context.AddRange(_receiving, _staging);
        _context.SaveChanges();

        _receipt = new Receipt(
            "EX-RECEIPT",
            _warehouse.Id,
            _warehouse.Code,
            supplierId: null,
            supplierCodeSnapshot: null,
            supplierNameSnapshot: null,
            purchaseOrderId: null,
            advanceShippingNoticeId: null,
            dockLocationId: null,
            receivingLocationId: _receiving.Id,
            createdByUserId: "receiver-1");
        _line = new ReceiptLine(
            1,
            _warehouse.Id,
            _item.Id,
            _item.Sku,
            _item.Name,
            "EA",
            10m,
            "EA",
            10m,
            1m,
            0,
            QuantityRoundingMode.Reject,
            0m,
            "BASE",
            string.Empty,
            receivingLocationId: _receiving.Id,
            inventoryStatusId: _available.Id,
            inventoryStatusCodeSnapshot: _available.Code,
            inventoryStatusNameSnapshot: _available.Name);
        _receipt.AddLine(_line);
        _receipt.Open("receiver-1", DateTime.UtcNow);
        _context.Add(_receipt);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CreateIsIdempotentAndMarksReceiptExceptionAtomically()
    {
        var input = CreateInput("create-once");

        var first = await _service.CreateAsync(input, "receiver-1");
        var duplicate = await _service.CreateAsync(input, "receiver-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.Id.Should().Be(first.Value.Id);
        (await _context.InboundExceptions.CountAsync()).Should().Be(1);
        (await _context.Receipts.SingleAsync()).Status.Should().Be(ReceiptStatus.Exception);
    }

    [Fact]
    public async Task ExistingAvailableReceiptStockMustBeHeldBeforeExceptionCapture()
    {
        var movement = Movement.CreateReceipt(
            _item.Id,
            _receiving.Id,
            new Quantity(2m),
            "receiver-1",
            timestampUtc: DateTime.UtcNow,
            inventoryStatusId: InventoryStatusSystemIds.Available);
        movement.LinkReceipt(_receipt.Id, _line.Id);
        _context.Add(movement);
        _context.Add(new Stock(
            _item.Id,
            _receiving.Id,
            new Quantity(2m),
            inventoryStatusId: InventoryStatusSystemIds.Available));
        await _context.SaveChangesAsync();

        var result = await _service.CreateAsync(CreateInput("available-stock"), "receiver-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inbound_exception.stock_must_be_held");
        (await _context.InboundExceptions.CountAsync()).Should().Be(0);
        (await _context.Receipts.SingleAsync()).Status.Should().Be(ReceiptStatus.Open);
    }

    [Fact]
    public async Task CorrectSourceDataResolutionResumesReceiptWithinSameTransaction()
    {
        var created = await _service.CreateAsync(CreateInput("resolve-source"), "receiver-1");

        var resolved = await _service.ResolveAsync(
            created.Value.Id,
            new InboundExceptionResolutionInput(
                InboundExceptionResolution.CorrectSourceData,
                "Purchase order reference corrected"),
            "supervisor-1");

        resolved.IsSuccess.Should().BeTrue(resolved.Error);
        resolved.Value.Status.Should().Be(InboundExceptionStatus.Resolved);
        (await _context.Receipts.SingleAsync()).Status.Should().Be(ReceiptStatus.Receiving);
    }

    [Fact]
    public async Task SupervisorResolutionRequiresOverrideReason()
    {
        var created = await _service.CreateAsync(CreateInput("supervisor-required"), "receiver-1");

        var rejected = await _service.ResolveAsync(
            created.Value.Id,
            new InboundExceptionResolutionInput(
                InboundExceptionResolution.BlindReceiptApproval,
                "Approve without source document"),
            "supervisor-1");

        rejected.ErrorCode.Should().Be("inbound_exception.supervisor_reason_required");
        (await _context.InboundExceptions.SingleAsync()).Status.Should().Be(InboundExceptionStatus.Open);
    }

    private InboundExceptionInput CreateInput(string key) => new(
        _warehouse.Id,
        InboundExceptionCode.DocumentMismatch,
        InboundExceptionSeverity.High,
        "Inbound document does not match the physical load.",
        key,
        QueueCode: "RECEIVING",
        DueAtUtc: new DateTime(2026, 9, 21, 13, 0, 0, DateTimeKind.Utc),
        ReceiptId: _receipt.Id,
        ReceiptLineId: _line.Id,
        ItemId: _item.Id,
        StagingLocationId: _staging.Id,
        ExpectedBaseQuantity: 10m,
        ActualBaseQuantity: 10m,
        VarianceBaseQuantity: 0m,
        ItemSkuSnapshot: _item.Sku);

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
