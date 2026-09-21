using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.Application.Workforce;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.WarehouseWork;
using Wms.Infrastructure.Workforce;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Workforce;

public sealed class WorkforceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _zone;
    private readonly Location _source;
    private readonly DateTimeOffset _now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly WarehouseWorkAssignmentEligibilityService _eligibility;
    private readonly WarehouseWorkService _warehouseWorkService;
    private readonly WorkforceService _service;

    public WorkforceServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options,
            new FixedClock(_now));
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

        _warehouse = new Warehouse("WF-WH", "Workforce warehouse");
        _item = new Item("WF-ITEM", "Workforce item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();
        _zone = new Location(
            "WF-ZONE",
            "Workforce zone",
            _warehouse.Id,
            type: LocationType.Zone,
            isPickable: false,
            isReceivable: false);
        _context.Add(_zone);
        _context.SaveChanges();
        _source = new Location(
            "WF-SOURCE",
            "Workforce source",
            _warehouse.Id,
            parentLocationId: _zone.Id,
            type: LocationType.PickFace);
        _context.Add(_source);

        _context.Users.Add(new WmsUser
        {
            Id = "worker-1",
            UserName = "worker-1",
            NormalizedUserName = "WORKER-1",
            Email = "worker-1@example.test",
            NormalizedEmail = "WORKER-1@EXAMPLE.TEST",
            DisplayName = "Worker One",
            EmployeeCode = "WF-001",
            IsActive = true
        });
        _context.SaveChanges();
        _context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = "worker-1",
            WarehouseId = _warehouse.Id,
            IsDefault = true,
            AssignedAtUtc = _now
        });
        _context.SaveChanges();

        var clock = new FixedClock(_now);
        _eligibility = new WarehouseWorkAssignmentEligibilityService(_context, clock);
        _warehouseWorkService = new WarehouseWorkService(
            _context,
            new UnitOfWork(_context, _access.Object),
            _access.Object,
            _audit.Object,
            clock,
            [],
            NullLogger<WarehouseWorkService>.Instance,
            null,
            _eligibility);
        _service = new WorkforceService(
            _context,
            _access.Object,
            _audit.Object,
            clock,
            _warehouseWorkService,
            _eligibility);
    }

    [Fact]
    public async Task ProfileAndQueueEligibilityControlsSelfClaim()
    {
        var queue = await _service.SaveQueueAsync(
            null,
            new WorkQueueInput(
                _warehouse.Id,
                "PICK-WF",
                "Workforce picking",
                WarehouseWorkType.Pick,
                _zone.Id,
                AssignmentStrategy: WarehouseWorkAssignmentStrategy.SelfClaim,
                RequiredSkillCodes: ["FORKLIFT"]),
            "manager");
        queue.IsSuccess.Should().BeTrue(queue.Error);

        var profile = await _service.SaveWorkerProfileAsync(
            "worker-1",
            new WorkerProfileInput(
                _warehouse.Id,
                ShiftStartAtUtc: _now.UtcDateTime.AddHours(-1),
                ShiftEndAtUtc: _now.UtcDateTime.AddHours(8),
                PreferredZoneLocationIds: [_zone.Id]),
            "manager");
        profile.IsSuccess.Should().BeTrue(profile.Error);

        var work = AddAvailableWork("wf-eligibility");
        var denied = await _service.ClaimAsync(
            work.Id,
            new WarehouseWorkClaimInput("claim-denied"),
            "worker-1");
        denied.IsFailure.Should().BeTrue();
        denied.ErrorCode.Should().Be("work.skill_ineligible");

        var updated = await _service.SaveWorkerProfileAsync(
            "worker-1",
            new WorkerProfileInput(
                _warehouse.Id,
                ShiftStartAtUtc: _now.UtcDateTime.AddHours(-1),
                ShiftEndAtUtc: _now.UtcDateTime.AddHours(8),
                SkillCodes: ["FORKLIFT"],
                PreferredZoneLocationIds: [_zone.Id]),
            "manager");
        updated.IsSuccess.Should().BeTrue(updated.Error);

        var claimed = await _service.ClaimAsync(
            work.Id,
            new WarehouseWorkClaimInput("claim-allowed"),
            "worker-1");
        claimed.IsSuccess.Should().BeTrue(claimed.Error);
        claimed.Value.AssignedUserId.Should().Be("worker-1");
    }

    [Fact]
    public async Task QueueCapacityAndWorkRevisionPreventSecondClaim()
    {
        await ConfigureWorkerAsync(capacity: 1);
        var first = AddAvailableWork("wf-capacity-1");
        var second = AddAvailableWork("wf-capacity-2");

        var firstClaim = await _service.ClaimAsync(
            first.Id,
            new WarehouseWorkClaimInput("claim-capacity-1"),
            "worker-1");
        firstClaim.IsSuccess.Should().BeTrue(firstClaim.Error);

        var secondClaim = await _service.ClaimAsync(
            second.Id,
            new WarehouseWorkClaimInput("claim-capacity-2"),
            "worker-1");
        secondClaim.IsFailure.Should().BeTrue();
        secondClaim.ErrorCode.Should().Be("work.queue_capacity_reached");
    }

    [Fact]
    public async Task ClaimReplayUsesTheSameIdempotentResult()
    {
        await ConfigureWorkerAsync();
        var work = AddAvailableWork("wf-replay");
        var input = new WarehouseWorkClaimInput("claim-replay");

        var first = await _service.ClaimAsync(work.Id, input, "worker-1");
        var replay = await _service.ClaimAsync(work.Id, input, "worker-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Id.Should().Be(first.Value.Id);
        replay.Value.Revision.Should().Be(first.Value.Revision);
        (await _context.WarehouseWorkCommands.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ShiftAndZoneFactsAreAppliedToRouting()
    {
        await ConfigureWorkerAsync(
            shiftStartUtc: _now.UtcDateTime.AddHours(2),
            shiftEndUtc: _now.UtcDateTime.AddHours(8));
        var work = AddAvailableWork("wf-shift");

        var result = await _service.ClaimAsync(
            work.Id,
            new WarehouseWorkClaimInput("claim-shift"),
            "worker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.shift_inactive");
    }

    [Fact]
    public async Task SuggestionsUseDeterministicRouteCostsAndExplainTheScore()
    {
        await ConfigureWorkerAsync();
        var near = new Location(
            "WF-NEAR",
            "Workforce near stop",
            _warehouse.Id,
            parentLocationId: _zone.Id,
            type: LocationType.PickFace);
        var far = new Location(
            "WF-FAR",
            "Workforce far stop",
            _warehouse.Id,
            parentLocationId: _zone.Id,
            type: LocationType.PickFace);
        _context.AddRange(near, far);
        await _context.SaveChangesAsync();

        var policy = await _service.SaveInterleavingPolicyAsync(
            null,
            new WorkInterleavingPolicyInput(
                _warehouse.Id,
                "DEFAULT",
                "Default deterministic interleaving",
                PriorityWeight: 10m,
                DeadlineWeight: 100m,
                TravelWeight: 1m,
                ZoneAffinityWeight: 10m),
            "manager");
        policy.IsSuccess.Should().BeTrue(policy.Error);

        foreach (var route in new[]
                 {
                     new WorkRouteInput(_warehouse.Id, _source.Id, near.Id, TravelMinutes: 2m),
                     new WorkRouteInput(_warehouse.Id, _source.Id, far.Id, TravelMinutes: 10m),
                     new WorkRouteInput(_warehouse.Id, near.Id, far.Id, TravelMinutes: 3m)
                 })
        {
            var saved = await _service.SaveRouteAsync(null, route, "manager");
            saved.IsSuccess.Should().BeTrue(saved.Error);
        }

        var nearWork = AddAvailableWorkAt("wf-route-near", near.Id);
        var farWork = AddAvailableWorkAt("wf-route-far", far.Id);
        var result = await _service.SuggestAsync(
            new WorkforceSuggestionsQuery(
                _warehouse.Id,
                WorkType: WarehouseWorkType.Pick,
                Limit: 2,
                CurrentLocationId: _source.Id,
                PolicyCode: "DEFAULT"),
            "worker-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Suggestions.Select(value => value.Work.Id)
            .Should().ContainInOrder(nearWork.Id, farWork.Id);
        result.Value.Suggestions[0].TravelMinutes.Should().Be(2m);
        result.Value.Suggestions[1].TravelMinutes.Should().Be(3m);
        result.Value.Suggestions.All(value => !value.IsFallback).Should().BeTrue();
        result.Value.Suggestions[0].ScoringBreakdown.Should().Contain("travel=2");
        result.Value.Suggestions[0].ScoringBreakdown.Should().Contain("priority=50*10");
    }

    [Fact]
    public async Task MissingRouteUsesPriorityFallbackAndManualOverrideRemainsExplicit()
    {
        await ConfigureWorkerAsync();
        var first = AddAvailableWorkAt("wf-fallback-first", _source.Id, priority: 90);
        var second = AddAvailableWorkAt("wf-fallback-second", _source.Id, priority: 10);

        var fallback = await _service.SuggestAsync(
            new WorkforceSuggestionsQuery(_warehouse.Id, WarehouseWorkType.Pick, Limit: 2),
            "worker-1");
        fallback.IsSuccess.Should().BeTrue(fallback.Error);
        fallback.Value.Suggestions.Select(value => value.Work.Id)
            .Should().ContainInOrder(first.Id, second.Id);
        fallback.Value.Suggestions.All(value => value.IsFallback).Should().BeTrue();

        var manual = await _service.SuggestAsync(
            new WorkforceSuggestionsQuery(
                _warehouse.Id,
                WarehouseWorkType.Pick,
                Limit: 1,
                ManualOverrideWorkId: second.Id),
            "worker-1");
        manual.IsSuccess.Should().BeTrue(manual.Error);
        manual.Value.Suggestions.Should().ContainSingle();
        manual.Value.Suggestions[0].Work.Id.Should().Be(second.Id);
        manual.Value.Suggestions[0].IsManualOverride.Should().BeTrue();

        var claimed = await _service.ClaimAsync(
            second.Id,
            new WarehouseWorkClaimInput("claim-manual-override"),
            "worker-1");
        claimed.IsSuccess.Should().BeTrue(claimed.Error);
        var afterClaim = await _service.SuggestAsync(
            new WorkforceSuggestionsQuery(_warehouse.Id, WarehouseWorkType.Pick, Limit: 2),
            "worker-1");
        afterClaim.IsSuccess.Should().BeTrue(afterClaim.Error);
        afterClaim.Value.Suggestions.Should().NotContain(value => value.Work.Id == second.Id);
    }

    [Fact]
    public async Task ActivityAndMetricsReportOperationalFactsWithoutRanking()
    {
        await ConfigureWorkerAsync();
        var work = AddAvailableWork("wf-metrics");
        var claimed = await _service.ClaimAsync(
            work.Id,
            new WarehouseWorkClaimInput("claim-metrics"),
            "worker-1");
        claimed.IsSuccess.Should().BeTrue(claimed.Error);

        work.Start("worker-1", _now.UtcDateTime.AddMinutes(-60));
        work.Lines.Single().RecordActualQuantity(10m);
        work.Complete("worker-1", _now.UtcDateTime.AddMinutes(-10));
        _context.SaveChanges();

        var activity = await _service.RecordActivityAsync(
            new WorkforceActivityInput(
                work.Id,
                WarehouseWorkActivityCategory.Travel,
                _now.UtcDateTime.AddMinutes(-40),
                _now.UtcDateTime.AddMinutes(-30),
                "travel to source"),
            "worker-1");
        activity.IsSuccess.Should().BeTrue(activity.Error);

        var metrics = await _service.GetMetricsAsync(
            new WorkforceMetricsQuery(
                _warehouse.Id,
                _now.UtcDateTime.AddHours(-2),
                _now.UtcDateTime.AddMinutes(1)));
        metrics.IsSuccess.Should().BeTrue(metrics.Error);
        metrics.Value.CompletedCount.Should().Be(1);
        metrics.Value.CompletedLines.Should().Be(1);
        metrics.Value.CompletedUnits.Should().Be(10m);
        metrics.Value.Buckets.Should().ContainSingle();
        metrics.Value.Buckets.Single().TravelMinutes.Should().Be(10);
        metrics.Value.InterpretationNote.Should().Contain("not punitive");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task ConfigureWorkerAsync(
        int? capacity = null,
        DateTime? shiftStartUtc = null,
        DateTime? shiftEndUtc = null)
    {
        var queue = await _service.SaveQueueAsync(
            null,
            new WorkQueueInput(
                _warehouse.Id,
                "PICK-WF",
                "Workforce picking",
                WarehouseWorkType.Pick,
                _zone.Id,
                Capacity: capacity,
                AssignmentStrategy: WarehouseWorkAssignmentStrategy.SelfClaim,
                RequiredSkillCodes: ["FORKLIFT"]),
            "manager");
        queue.IsSuccess.Should().BeTrue(queue.Error);
        var profile = await _service.SaveWorkerProfileAsync(
            "worker-1",
            new WorkerProfileInput(
                _warehouse.Id,
                ShiftStartAtUtc: shiftStartUtc ?? _now.UtcDateTime.AddHours(-1),
                ShiftEndAtUtc: shiftEndUtc ?? _now.UtcDateTime.AddHours(8),
                SkillCodes: ["FORKLIFT"],
                PreferredZoneLocationIds: [_zone.Id]),
            "manager");
        profile.IsSuccess.Should().BeTrue(profile.Error);
    }

    private WarehouseWorkEntity AddAvailableWork(
        string creationKey,
        int priority = 50,
        DateTime? dueAtUtc = null) =>
        AddAvailableWorkAt(creationKey, _source.Id, priority, dueAtUtc);

    private WarehouseWorkEntity AddAvailableWorkAt(
        string creationKey,
        int sourceLocationId,
        int priority = 50,
        DateTime? dueAtUtc = null)
    {
        var work = new Wms.Domain.Entities.WarehouseWork(
            $"WF-{creationKey}",
            creationKey,
            WarehouseWorkType.Pick,
            _warehouse.Id,
            "TEST",
            creationKey,
            priority: priority,
            queueCode: "PICK-WF",
            dueAtUtc: dueAtUtc);
        work.AddLine(new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            10m,
            "EA",
            sourceLocationId: sourceLocationId));
        work.MakeAvailable(_now.UtcDateTime.AddMinutes(-5));
        _context.WarehouseWorks.Add(work);
        _context.SaveChanges();
        return work;
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
