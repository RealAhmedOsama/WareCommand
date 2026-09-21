using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inbound;

namespace Wms.Infrastructure.Tests.Inbound;

public sealed class CrossDockServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Supplier _supplier;
    private readonly Customer _customer;
    private readonly Location _receiving;
    private readonly Location _staging;
    private readonly int _availableStatusId;
    private readonly DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly CrossDockService _service;

    public CrossDockServiceTests()
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
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("XDOCK-WH", "Cross-dock warehouse");
        _item = new Item("XDOCK-ITEM", "Cross-dock item", "EA");
        _supplier = new Supplier("XDOCK-SUP", "Cross-dock supplier", defaultCurrencyCode: "USD");
        _customer = new Customer("XDOCK-CUST", "Cross-dock customer");
        _context.AddRange(_warehouse, _item, _supplier, _customer);
        _context.SaveChanges();

        _receiving = new Location(
            "XDOCK-RECEIVE",
            "Cross-dock receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false);
        _staging = new Location(
            "XDOCK-STAGE",
            "Cross-dock staging",
            _warehouse.Id,
            type: LocationType.Staging,
            isPickable: false);
        _context.AddRange(_receiving, _staging);
        _context.SaveChanges();
        _context.WarehouseOperationalLocations.Add(new WarehouseOperationalLocation(
            _warehouse.Id,
            _staging.Id,
            WarehouseOperationalLocationRole.Staging));
        _context.SaveChanges();

        var available = new InventoryStatus(
            InventoryStatusCodes.Available,
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.InventoryStatuses.Add(available);
        _context.SaveChanges();
        _availableStatusId = available.Id;

        _service = new CrossDockService(
            _context,
            _access.Object,
            _audit.Object,
            new FixedClock(_now));
    }

    [Fact]
    public async Task SimulationMatchesDemandDeterministicallyAndLeavesExcessAsFallback()
    {
        AddConfirmedOrder("SO-XDOCK-0001", 6m, priority: 90);
        var receipt = AddReceipt(10m, _availableStatusId);
        var policy = await CreatePolicyAsync();

        var result = await _service.SimulateAsync(
            new CrossDockSimulationInput(
                receipt.Line.Id,
                CrossDockMode.Opportunistic,
                policy.Value.Id,
                _now.UtcDateTime));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.MatchedBaseQuantity.Should().Be(6m);
        result.Value.FallbackBaseQuantity.Should().Be(4m);
        result.Value.Matches.Should().ContainSingle();
        result.Value.Matches[0].SalesOrderDocumentNumber.Should().Be("SO-XDOCK-0001");
        result.Value.Explanation.Should().Contain("fallback");
        (await _context.CrossDockPlans.CountAsync()).Should().Be(0);
        (await _context.InventoryReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task QualityPendingStockCannotBypassInventoryStatusGate()
    {
        AddConfirmedOrder("SO-XDOCK-0002", 5m, priority: 90);
        var pending = new InventoryStatus(
            "QC_PENDING",
            "QC pending",
            "قيد الفحص",
            isAvailable: false,
            isAllocatable: false,
            isPickable: false,
            isShippable: false,
            isCountable: true);
        _context.InventoryStatuses.Add(pending);
        _context.SaveChanges();
        var receipt = AddReceipt(5m, pending.Id);
        var policy = await CreatePolicyAsync();

        var result = await _service.SimulateAsync(
            new CrossDockSimulationInput(
                receipt.Line.Id,
                CrossDockMode.Planned,
                policy.Value.Id,
                _now.UtcDateTime));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.MatchedBaseQuantity.Should().Be(0m);
        result.Value.FallbackBaseQuantity.Should().Be(5m);
        result.Value.Explanation.Should().Contain("not allocatable");
    }

    [Fact]
    public async Task PlanPersistsReceiptDemandAndTraceabilitySnapshots()
    {
        AddConfirmedOrder("SO-XDOCK-0003", 8m, priority: 100);
        var receipt = AddReceipt(
            8m,
            _availableStatusId,
            lotNumber: "LOT-XDOCK-1",
            serialNumber: "SER-XDOCK-1");
        var policy = await CreatePolicyAsync();

        var first = await _service.CreatePlanAsync(
            new CrossDockPlanCreateInput(
                receipt.Line.Id,
                CrossDockMode.Planned,
                policy.Value.Id,
                AsOfUtc: _now.UtcDateTime),
            "planner");
        var replay = await _service.CreatePlanAsync(
            new CrossDockPlanCreateInput(
                receipt.Line.Id,
                CrossDockMode.Planned,
                policy.Value.Id,
                AsOfUtc: _now.UtcDateTime),
            "planner");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Id.Should().Be(first.Value.Id);
        first.Value.Status.Should().Be(CrossDockPlanStatus.Matched);
        first.Value.MatchedBaseQuantity.Should().Be(8m);
        first.Value.SourceLocationId.Should().Be(_receiving.Id);
        first.Value.DestinationLocationId.Should().Be(_staging.Id);
        first.Value.LotNumber.Should().Be("LOT-XDOCK-1");
        first.Value.SerialNumber.Should().Be("SER-XDOCK-1");
        first.Value.Lines.Should().ContainSingle(line =>
            line.SalesOrderDocumentNumber == "SO-XDOCK-0003" &&
            line.MatchedBaseQuantity == 8m);
        (await _context.CrossDockPlans.CountAsync()).Should().Be(1);
        (await _context.InventoryReservations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task PolicyFiltersSupplierSourceAndCustomerWithoutCreatingAPlan()
    {
        AddConfirmedOrder("SO-XDOCK-0004", 5m, priority: 50);
        var receipt = AddReceipt(5m, _availableStatusId, sourceType: "ASN");
        var policy = await CreatePolicyAsync(
            sourceType: "PURCHASE_ORDER",
            supplierId: _supplier.Id,
            customerId: _customer.Id);

        var result = await _service.SimulateAsync(
            new CrossDockSimulationInput(
                receipt.Line.Id,
                CrossDockMode.Opportunistic,
                policy.Value.Id,
                _now.UtcDateTime));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.MatchedBaseQuantity.Should().Be(0m);
        result.Value.FallbackBaseQuantity.Should().Be(5m);
        result.Value.Explanation.Should().Contain("inbound source");
    }

    [Fact]
    public async Task MatchedPlanCanBeCancelledBeforeAnyExecutionBoundary()
    {
        AddConfirmedOrder("SO-XDOCK-0005", 3m, priority: 50);
        var receipt = AddReceipt(3m, _availableStatusId);
        var policy = await CreatePolicyAsync();
        var plan = await _service.CreatePlanAsync(
            new CrossDockPlanCreateInput(
                receipt.Line.Id,
                CrossDockMode.Opportunistic,
                policy.Value.Id,
                AsOfUtc: _now.UtcDateTime),
            "planner");

        var cancelled = await _service.CancelAsync(
            plan.Value.Id,
            "Demand was cancelled before reservation/work execution.",
            "planner");

        cancelled.IsSuccess.Should().BeTrue(cancelled.Error);
        cancelled.Value.Status.Should().Be(CrossDockPlanStatus.Cancelled);
        (await _service.CancelAsync(
                plan.Value.Id,
                "duplicate cancellation",
                "planner"))
            .ErrorCode.Should().Be("crossdock.cancel_invalid");
    }

    private async Task<Result<CrossDockPolicyDto>> CreatePolicyAsync(
        string? sourceType = null,
        int? supplierId = null,
        int? customerId = null) =>
        await _service.SavePolicyAsync(
            null,
            new CrossDockPolicyInput(
                _warehouse.Id,
                "default-cross-dock",
                "Default cross-dock",
                Priority: 100,
                ItemId: _item.Id,
                SupplierId: supplierId,
                InboundSourceType: sourceType,
                CustomerId: customerId,
                DestinationLocationId: _staging.Id,
                EffectiveFromUtc: _now.UtcDateTime),
            "planner");

    private SalesOrder AddConfirmedOrder(string documentNumber, decimal quantity, int priority)
    {
        var order = new SalesOrder(
            documentNumber,
            _warehouse.Id,
            _warehouse.Code,
            _customer.Id,
            _customer.Code,
            _customer.LegalName,
            _customer.LocalizedName,
            _customer.ContactName,
            _customer.ContactEmail,
            _customer.ContactPhone,
            null,
            null,
            _customer.LegalName,
            null,
            "EG",
            "Cairo",
            "Cairo",
            "11511",
            "Cross-dock street",
            null,
            null,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 22),
            null,
            "TEST",
            null,
            priority,
            null,
            null,
            null,
            null,
            true,
            null,
            "test");
        var line = new SalesOrderLine(
            1,
            _item.Id,
            _item.Sku,
            _item.Name,
            _item.LocalizedName,
            null,
            "EA",
            quantity,
            "EA",
            quantity,
            1m,
            0,
            "EA -> EA",
            string.Empty);
        order.ReplaceDraftLines([line]);
        order.Confirm("test", _now.UtcDateTime);
        _context.SalesOrders.Add(order);
        _context.SaveChanges();
        return order;
    }

    private (Receipt Receipt, ReceiptLine Line) AddReceipt(
        decimal quantity,
        int inventoryStatusId,
        string sourceType = "PO",
        string? lotNumber = null,
        string? serialNumber = null)
    {
        var status = _context.InventoryStatuses.Single(value => value.Id == inventoryStatusId);
        var receipt = new Receipt(
            $"RCPT-{_context.Receipts.Count() + 1:0000}",
            _warehouse.Id,
            _warehouse.Code,
            _supplier.Id,
            _supplier.Code,
            _supplier.LegalName,
            null,
            null,
            null,
            _receiving.Id,
            sourceType,
            sourceReference: "INBOUND-REF");
        var line = new ReceiptLine(
            1,
            _warehouse.Id,
            _item.Id,
            _item.Sku,
            _item.Name,
            "EA",
            quantity,
            "EA",
            quantity,
            1m,
            0,
            QuantityRoundingMode.Reject,
            0m,
            "EA -> EA",
            string.Empty,
            receivingLocationId: _receiving.Id,
            lotNumberSnapshot: lotNumber,
            serialNumberSnapshot: serialNumber,
            inventoryStatusId: status.Id,
            inventoryStatusCodeSnapshot: status.Code,
            inventoryStatusNameSnapshot: status.Name);
        receipt.AddLine(line);
        receipt.Open("receiver", _now.UtcDateTime);
        receipt.StartReceiving("receiver", _now.UtcDateTime);
        line.RecordPhysicalReceipt(quantity, quantity, 0m, 0m, 0m);
        _context.Receipts.Add(receipt);
        _context.SaveChanges();
        return (receipt, line);
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
