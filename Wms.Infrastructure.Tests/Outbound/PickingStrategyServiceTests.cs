using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Outbound;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Outbound;

public sealed class PickingStrategyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Warehouse _warehouse;
    private readonly Customer _customer;
    private readonly Item _item;
    private readonly Location _zoneA;
    private readonly Location _zoneB;
    private readonly Location _sourceA;
    private readonly Location _sourceB;
    private readonly PickingStrategyService _service;

    public PickingStrategyServiceTests()
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

        _warehouse = new Warehouse("PICK-WH", "Picking warehouse");
        _customer = new Customer("PICK-CUST", "Picking customer");
        _item = new Item("PICK-ITEM", "Picking item", "EA");
        _context.AddRange(_warehouse, _customer, _item);
        _context.SaveChanges();

        _zoneA = new Location(
            "PICK-ZONE-A",
            "Zone A",
            _warehouse.Id,
            type: LocationType.Zone,
            isPickable: false,
            isReceivable: false);
        _zoneB = new Location(
            "PICK-ZONE-B",
            "Zone B",
            _warehouse.Id,
            type: LocationType.Zone,
            isPickable: false,
            isReceivable: false);
        _context.AddRange(_zoneA, _zoneB);
        _context.SaveChanges();
        _sourceA = new Location(
            "PICK-A-01",
            "Zone A pick face",
            _warehouse.Id,
            parentLocationId: _zoneA.Id,
            type: LocationType.PickFace);
        _sourceB = new Location(
            "PICK-B-01",
            "Zone B pick face",
            _warehouse.Id,
            parentLocationId: _zoneB.Id,
            type: LocationType.PickFace);
        _context.AddRange(_sourceA, _sourceB);
        _context.SaveChanges();

        _service = new PickingStrategyService(
            _context,
            _access.Object,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<PickingStrategyService>.Instance);
    }

    [Fact]
    public async Task BatchPlanKeepsOrderAttributionAndCombinesCommonSourceDemand()
    {
        var first = AddOrderAndWork("SO-PICK-0001", 10m, _sourceA);
        var second = AddOrderAndWork("SO-PICK-0002", 5m, _sourceA);
        _context.SaveChanges();

        var result = await _service.CreatePlanAsync(
            new PickingPlanCreateInput(
                _warehouse.Id,
                "batch-plan-1",
                Strategy: PickingStrategyKind.Batch,
                MaxOrders: 2,
                MaxContainers: 2),
            "planner");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Strategy.Should().Be(PickingStrategyKind.Batch);
        result.Value.OrderCount.Should().Be(2);
        result.Value.Lines.Should().HaveCount(2);
        result.Value.Lines.Select(line => line.SalesOrderId).Should().Contain([first.Order.Id, second.Order.Id]);
        result.Value.Lines.Sum(line => line.PlannedQuantity).Should().Be(15m);
        result.Value.Containers.Should().ContainSingle();
        result.Value.Lines.Select(line => line.BatchKey).Distinct().Should().ContainSingle();
        (await _context.PickingPlanLines.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ClusterPlanRejectsWrongTargetAndAcceptsTheExpectedContainerScan()
    {
        AddOrderAndWork("SO-PICK-0003", 4m, _sourceA);
        _context.SaveChanges();

        var created = await _service.CreatePlanAsync(
            new PickingPlanCreateInput(
                _warehouse.Id,
                "cluster-plan-1",
                Strategy: PickingStrategyKind.Cluster),
            "planner");
        created.IsSuccess.Should().BeTrue(created.Error);
        var container = created.Value.Containers.Single();
        container.TargetScanRequired.Should().BeTrue();

        var wrong = await _service.ScanContainerAsync(
            created.Value.Id,
            container.Id,
            new PickingContainerScanInput("WRONG-TOTE", "scan-1"),
            "worker");
        var accepted = await _service.ScanContainerAsync(
            created.Value.Id,
            container.Id,
            new PickingContainerScanInput(container.ExpectedScanCode, "scan-2"),
            "worker");

        wrong.IsFailure.Should().BeTrue();
        wrong.ErrorCode.Should().Be("picking.wrong_target_container");
        accepted.IsSuccess.Should().BeTrue(accepted.Error);
        accepted.Value.Containers.Single().Status.Should().Be(PickingContainerStatus.Scanned);
    }

    [Fact]
    public async Task PickAndPassRequiresOrderedZoneHandoffs()
    {
        var order = AddOrder("SO-PICK-0004");
        AddWork(order, order.Lines.Single(), 3m, _sourceA, "pass-work-a");
        AddWork(order, order.Lines.Single(), 2m, _sourceB, "pass-work-b");
        _context.SaveChanges();

        var created = await _service.CreatePlanAsync(
            new PickingPlanCreateInput(
                _warehouse.Id,
                "pass-plan-1",
                Strategy: PickingStrategyKind.PickAndPass,
                MaxOrders: 1,
                MaxContainers: 4),
            "planner");
        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.Handoffs.Should().HaveCount(2);

        var first = created.Value.Handoffs.OrderBy(handoff => handoff.Sequence).First();
        var second = created.Value.Handoffs.OrderBy(handoff => handoff.Sequence).Last();
        var outOfOrder = await _service.CompleteHandoffAsync(
            created.Value.Id,
            second.Id,
            new PickingHandoffInput(second.ExpectedContainerScanCode, "handoff-2"),
            "worker");
        var firstCompleted = await _service.CompleteHandoffAsync(
            created.Value.Id,
            first.Id,
            new PickingHandoffInput(first.ExpectedContainerScanCode, "handoff-1"),
            "worker");
        var secondCompleted = await _service.CompleteHandoffAsync(
            created.Value.Id,
            second.Id,
            new PickingHandoffInput(second.ExpectedContainerScanCode, "handoff-3"),
            "worker");

        outOfOrder.IsFailure.Should().BeTrue();
        outOfOrder.ErrorCode.Should().Be("picking.handoff_out_of_order");
        firstCompleted.IsSuccess.Should().BeTrue(firstCompleted.Error);
        secondCompleted.IsSuccess.Should().BeTrue(secondCompleted.Error);
        secondCompleted.Value.Handoffs.All(handoff => handoff.Status == PickingHandoffStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public async Task ActivePolicySelectsTheConfiguredStrategyAndCapacity()
    {
        AddOrderAndWork("SO-PICK-0005", 4m, _sourceA);
        AddOrderAndWork("SO-PICK-0006", 3m, _sourceA);
        _context.SaveChanges();

        var policy = await _service.SavePolicyAsync(
            null,
            new PickingStrategyPolicyInput(
                _warehouse.Id,
                "cluster-fast",
                "Cluster fast orders",
                PickingStrategyKind.Cluster,
                Priority: 90,
                MaxOrders: 1,
                MaxContainers: 1),
            "planner");
        var plan = await _service.CreatePlanAsync(
            new PickingPlanCreateInput(_warehouse.Id, "policy-plan-1"),
            "planner");

        policy.IsSuccess.Should().BeTrue(policy.Error);
        plan.IsSuccess.Should().BeTrue(plan.Error);
        plan.Value.Strategy.Should().Be(PickingStrategyKind.Cluster);
        plan.Value.PolicyId.Should().Be(policy.Value.Id);
        plan.Value.OrderCount.Should().Be(1);
        plan.Value.Containers.Should().ContainSingle(container => container.TargetScanRequired);
    }

    [Fact]
    public async Task SingleOrderStrategyKeepsOrdersInSeparateExecutionContainers()
    {
        AddOrderAndWork("SO-PICK-0007", 4m, _sourceA);
        AddOrderAndWork("SO-PICK-0008", 3m, _sourceA);
        _context.SaveChanges();

        var plan = await _service.CreatePlanAsync(
            new PickingPlanCreateInput(
                _warehouse.Id,
                "single-order-plan-1",
                Strategy: PickingStrategyKind.SingleOrder,
                MaxOrders: 2,
                MaxContainers: 2),
            "planner");

        plan.IsSuccess.Should().BeTrue(plan.Error);
        plan.Value.Containers.Should().HaveCount(2);
        plan.Value.Containers.Select(container => container.SalesOrderId).Distinct().Should().HaveCount(2);
        plan.Value.Lines.GroupBy(line => line.ContainerId).Should().HaveCount(2);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private (SalesOrder Order, SalesOrderLine Line) AddOrderAndWork(
        string documentNumber,
        decimal quantity,
        Location sourceLocation)
    {
        var order = AddOrder(documentNumber);
        var line = order.Lines.Single();
        AddWork(order, line, quantity, sourceLocation, $"work-{documentNumber}");
        return (order, line);
    }

    private SalesOrder AddOrder(string documentNumber)
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
            "Picking customer",
            null,
            "EG",
            "Cairo",
            "Cairo",
            "11511",
            "Picking street",
            null,
            null,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 22),
            null,
            "TEST",
            null,
            100,
            "FAST",
            "NEXT",
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
            10m,
            "EA",
            10m,
            1m,
            0,
            "EA -> EA",
            string.Empty);
        order.ReplaceDraftLines([line]);
        order.Confirm("test", new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
        _context.Add(order);
        _context.SaveChanges();
        return order;
    }

    private void AddWork(
        SalesOrder order,
        SalesOrderLine line,
        decimal quantity,
        Location sourceLocation,
        string creationKey)
    {
        var work = new WarehouseWorkEntity(
            $"WORK-{creationKey}",
            creationKey,
            WarehouseWorkType.Pick,
            _warehouse.Id,
            "SalesOrderLine",
            line.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            priority: order.Priority,
            sourceLineReference: $"{order.DocumentNumber}:1",
            queueCode: "PICK");
        work.AddLine(new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            quantity,
            "EA",
            sourceLocation.Id,
            null,
            null,
            null,
            null,
            null,
            null,
            "test-pick",
            null,
            null,
            null));
        work.MakeAvailable(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));
        _context.Add(work);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
