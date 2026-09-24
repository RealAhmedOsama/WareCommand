using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.WarehouseWork;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class CycleCountServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IStockMovementService> _stockMovement = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _location;
    private readonly CycleCountService _service;

    public CycleCountServiceTests()
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

        _warehouse = new Warehouse("COUNT-WH", "Cycle-count warehouse");
        _item = new Item("COUNT-ITEM", "Cycle-count item", "EA");
        _availableStatus = new InventoryStatus(
            "COUNT_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _location = new Location(
            "COUNT-BIN",
            "Count bin",
            _warehouse.Id,
            type: LocationType.Bin,
            isReceivable: false);
        _context.Add(_location);
        _context.SaveChanges();

        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        balance.Apply(7m, 0m, allowNegativeStock: false);
        _context.Add(balance);
        _context.SaveChanges();

        var workService = new WarehouseWorkService(
            _context,
            new UnitOfWork(_context, _access.Object),
            _access.Object,
            _audit.Object,
            _clock,
            Array.Empty<IWarehouseWorkCompletionHandler>(),
            NullLogger<WarehouseWorkService>.Instance);
        _service = new CycleCountService(
            _context,
            new Lazy<IWarehouseWorkService>(() => workService),
            _stockMovement.Object,
            _access.Object,
            _audit.Object,
            _clock,
            NullLogger<CycleCountService>.Instance);
    }

    [Fact]
    public async Task BlindDuePlanCreatesSnapshotAndCountWorkWithoutExpectedQuantityInSummary()
    {
        var plan = await _service.SavePlanAsync(
            null,
            new CycleCountPlanInput(
                "COUNT-DAILY",
                _warehouse.Id,
                _location.Id,
                _item.Id,
                null,
                1,
                0m,
                Blind: true,
                CycleCountFreezePolicy.SnapshotAndReconcile,
                _clock.UtcNow.UtcDateTime.AddHours(-1)),
            "planner-1");
        plan.IsSuccess.Should().BeTrue(plan.Error);

        var result = await _service.GenerateAsync(
            new CycleCountGenerationQuery(_warehouse.Id, plan.Value.Id),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.PlansExamined.Should().Be(1);
        result.Value.TasksCreated.Should().Be(1);
        result.Value.LinesCreated.Should().Be(1);
        result.Value.Tasks.Should().ContainSingle();
        result.Value.Tasks.Single().Blind.Should().BeTrue();
        result.Value.Tasks.Single().ExpectedQuantity.Should().BeNull();
        result.Value.Tasks.Single().WarehouseWorkId.Should().NotBeNull();

        var task = await _context.CycleCountTasks
            .Include(value => value.Lines)
            .SingleAsync();
        task.Lines.Single().ExpectedQuantity.Should().Be(7m);
        task.WarehouseWorkId.Should().NotBeNull();
        var work = await _context.WarehouseWorks
            .Include(value => value.Lines)
            .SingleAsync();
        work.Type.Should().Be(WarehouseWorkType.Count);
        work.Lines.Single().PlannedQuantity.Should().Be(1m);
        work.Lines.Single().SourceReference.Should().StartWith("cycle-count-line:");
    }

    [Fact]
    public async Task DuplicatePlanKeysAreRejectedWithinWarehouse()
    {
        var input = new CycleCountPlanInput(
            "COUNT-DUPLICATE",
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            7,
            0m,
            Blind: false,
            CycleCountFreezePolicy.BlockSelectedDimensions,
            _clock.UtcNow.UtcDateTime.AddDays(1));

        var first = await _service.SavePlanAsync(null, input, "planner-1");
        var second = await _service.SavePlanAsync(null, input, "planner-2");

        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("cycle_count.plan_key_conflict");
        (await _context.CycleCountPlans.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task VarianceRequiresApprovalAndAppliesOneStableCountMovement()
    {
        var task = await GenerateTaskAsync("COUNT-APPROVAL", blind: true);

        var started = await _service.StartTaskAsync(
            task.Id,
            new CycleCountTaskStartInput(task.Revision),
            "counter-1");

        started.IsSuccess.Should().BeTrue(started.Error);
        started.Value.Status.Should().Be(CycleCountTaskStatus.InProgress);
        started.Value.Lines.Single().ExpectedQuantity.Should().BeNull();

        var submitted = await _service.RecordCountsAsync(
            task.Id,
            [new CycleCountLineCountInput(task.Lines.Single().Id, 5m)],
            "counter-1");

        submitted.IsSuccess.Should().BeTrue(submitted.Error);
        submitted.Value.Status.Should().Be(CycleCountTaskStatus.AwaitingApproval);
        submitted.Value.Lines.Single().ExpectedQuantity.Should().BeNull();
        submitted.Value.Lines.Single().VarianceQuantity.Should().BeNull();
        _stockMovement
            .Verify(service => service.CountVarianceAsync(
                It.IsAny<CycleCountTask>(),
                It.IsAny<CycleCountLine>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);

        _stockMovement
            .Setup(service => service.CountVarianceAsync(
                It.IsAny<CycleCountTask>(),
                It.IsAny<CycleCountLine>(),
                "supervisor-1",
                "Verified bin recount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Movement(
                MovementType.CycleCount,
                _item.Id,
                new Quantity(5m),
                "supervisor-1"));

        var approval = await _service.ApproveTaskAsync(
            task.Id,
            new CycleCountTaskApprovalInput(submitted.Value.Revision, "Verified bin recount"),
            "supervisor-1");

        approval.IsSuccess.Should().BeTrue(approval.Error);
        approval.Value.Status.Should().Be(CycleCountTaskStatus.Completed);
        approval.Value.Lines.Single().ExpectedQuantity.Should().Be(7m);
        approval.Value.Lines.Single().CountedQuantity.Should().Be(5m);
        approval.Value.Lines.Single().VarianceQuantity.Should().Be(-2m);

        var replay = await _service.ApproveTaskAsync(
            task.Id,
            new CycleCountTaskApprovalInput(1, "duplicate request"),
            "supervisor-1");

        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Status.Should().Be(CycleCountTaskStatus.Completed);
        _stockMovement
            .Verify(service => service.CountVarianceAsync(
                It.IsAny<CycleCountTask>(),
                It.IsAny<CycleCountLine>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.CountSubmitted),
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.CountApproved),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VarianceApprovalRejectsAStaleInventorySnapshot()
    {
        var task = await GenerateTaskAsync("COUNT-STALE", blind: false);
        var started = await _service.StartTaskAsync(
            task.Id,
            new CycleCountTaskStartInput(task.Revision),
            "counter-1");
        started.IsSuccess.Should().BeTrue(started.Error);

        var submitted = await _service.RecordCountsAsync(
            task.Id,
            [new CycleCountLineCountInput(task.Lines.Single().Id, 5m)],
            "counter-1");
        submitted.IsSuccess.Should().BeTrue(submitted.Error);

        var balance = await _context.InventoryBalances.SingleAsync();
        balance.Apply(1m, 0m, allowNegativeStock: false);
        await _context.SaveChangesAsync();

        var approval = await _service.ApproveTaskAsync(
            task.Id,
            new CycleCountTaskApprovalInput(submitted.Value.Revision, "Recount required"),
            "supervisor-1");

        approval.IsFailure.Should().BeTrue();
        approval.ErrorCode.Should().Be("cycle_count.snapshot_stale");
        _stockMovement.Verify(service => service.CountVarianceAsync(
            It.IsAny<CycleCountTask>(),
            It.IsAny<CycleCountLine>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        (await _context.CycleCountTasks.SingleAsync()).Status
            .Should().Be(CycleCountTaskStatus.AwaitingApproval);
    }

    private async Task<CycleCountTask> GenerateTaskAsync(string planKey, bool blind)
    {
        var plan = await _service.SavePlanAsync(
            null,
            new CycleCountPlanInput(
                planKey,
                _warehouse.Id,
                _location.Id,
                _item.Id,
                null,
                1,
                0m,
                blind,
                CycleCountFreezePolicy.SnapshotAndReconcile,
                _clock.UtcNow.UtcDateTime.AddHours(-1)),
            "planner-1");
        plan.IsSuccess.Should().BeTrue(plan.Error);

        var generated = await _service.GenerateAsync(
            new CycleCountGenerationQuery(_warehouse.Id, plan.Value.Id),
            "planner-1");
        generated.IsSuccess.Should().BeTrue(generated.Error);

        return await _context.CycleCountTasks
            .Include(value => value.Lines)
            .SingleAsync(value => value.PlanId == plan.Value.Id);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
