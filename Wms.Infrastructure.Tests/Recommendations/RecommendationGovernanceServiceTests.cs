using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.Recommendations;
using Wms.Application.WarehouseWork;
using Wms.Application.Workforce;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Recommendations;

namespace Wms.Infrastructure.Tests.Recommendations;

public sealed class RecommendationGovernanceServiceTests : IDisposable
{
    private const int WarehouseId = 3;
    private const int PolicyId = 7;
    private const string SourceFingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IReplenishmentExecutionService> _replenishment = new();
    private readonly Mock<ISlottingService> _slotting = new();
    private readonly Mock<IWorkforceService> _workforce = new();
    private readonly Mock<IInboundExceptionService> _inboundExceptions = new();
    private readonly Mock<IOutboundExceptionService> _outboundExceptions = new();
    private readonly Mock<IForecastingService> _forecasting = new();
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly DeterministicReplenishmentDraftProvider _provider = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero));
    private string _currentFingerprint = SourceFingerprint;
    private bool _failNormalExecution;
    private RecommendationGovernanceService _service = null!;

    public RecommendationGovernanceServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options,
            _clock);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _warehouseAccess
            .Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(user => user.UserId).Returns("reviewer-1");
        _replenishment
            .Setup(service => service.GenerateAsync(
                It.IsAny<ReplenishmentGenerationQuery>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplenishmentGenerationQuery query, string _, CancellationToken _) =>
                !query.DryRun && _failNormalExecution
                    ? Result.Failure<ReplenishmentGenerationResultDto>(WmsErrors.Dependency(
                        "work.command_unavailable",
                        "The normal replenishment command is temporarily unavailable.",
                        isRetryable: true))
                    : Result.Success(new ReplenishmentGenerationResultDto(
                        1,
                        1,
                        query.DryRun ? 0 : 1,
                        0,
                        0,
                        [CreatePlan(query.DryRun)])));
        _service = CreateService(enabled: true);
    }

    [Fact]
    public async Task Persists_replay_safe_shadow_proposal_review_and_normal_work_reference()
    {
        var first = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        var replay = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        first.Value.Should().ContainSingle();
        replay.Value.Single().RecommendationId.Should().Be(first.Value.Single().RecommendationId);
        first.Value.Single().Status.Should().Be(RecommendationStatus.Proposed);
        first.Value.Single().ShadowMode.Should().BeTrue();
        first.Value.Single().CanMutateInventory.Should().BeFalse();
        (await _context.GovernedRecommendations.CountAsync()).Should().Be(1);

        var recommendation = first.Value.Single();
        var reviewed = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "review-1", "Reviewed; operator a@example.com token=secret-value."));
        reviewed.IsSuccess.Should().BeTrue(reviewed.Error);
        reviewed.Value.Status.Should().Be(RecommendationStatus.Reviewed);
        reviewed.Value.Revision.Should().Be(2);

        var staleRevision = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "review-stale", "Stale reviewer."));
        staleRevision.ErrorCode.Should().Be("recommendation.revision_conflict");

        var approved = await _service.ApproveAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(2, "approve-1"));
        approved.IsSuccess.Should().BeTrue(approved.Error);
        approved.Value.Status.Should().Be(RecommendationStatus.Approved);
        approved.Value.Revision.Should().Be(3);

        var executed = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(3, "execute-1"));
        executed.IsSuccess.Should().BeTrue(executed.Error);
        executed.Value.Status.Should().Be(RecommendationStatus.Executed);
        executed.Value.ExecutionReference.Should().Be("warehouse-work:899");
        executed.Value.ExecutionAttempts.Should().Be(1);

        var responseLossReplay = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(3, "execute-1"));
        responseLossReplay.IsSuccess.Should().BeTrue(responseLossReplay.Error);
        responseLossReplay.Value.ExecutionReference.Should().Be("warehouse-work:899");
        _replenishment.Verify(service => service.GenerateAsync(
            It.Is<ReplenishmentGenerationQuery>(query => query.PolicyId == PolicyId && !query.DryRun),
            "reviewer-1",
            It.IsAny<CancellationToken>()), Times.Once);

        var history = await _service.GetHistoryAsync(recommendation.RecommendationId);
        history.IsSuccess.Should().BeTrue(history.Error);
        history.Value.Select(entry => entry.EventType).Should()
            .Equal("created", "reviewed", "approved", "executed");
        history.Value[1].Comment.Should().Contain("[redacted-email]").And.Contain("token=[redacted]");
    }

    [Fact]
    public async Task Stale_approval_returns_conflict_without_issuing_normal_work()
    {
        var created = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        var recommendation = created.Value.Single();
        var reviewed = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "review-2", "Reviewed."));
        _currentFingerprint = new string('b', 64);

        var approval = await _service.ApproveAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(reviewed.Value.Revision, "approve-stale"));

        approval.ErrorCode.Should().Be("recommendation.stale");
        var current = await _service.GetAsync(recommendation.RecommendationId);
        current.Value.Status.Should().Be(RecommendationStatus.Reviewed);
        _replenishment.Verify(service => service.GenerateAsync(
            It.Is<ReplenishmentGenerationQuery>(query => !query.DryRun),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Disabled_or_unavailable_provider_leaves_deterministic_execution_unrequested()
    {
        _service = CreateService(enabled: false);
        var disabled = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        disabled.ErrorCode.Should().Be("recommendation.disabled");

        _service = CreateService(enabled: true, killSwitchEnabled: true);
        var killSwitch = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        killSwitch.ErrorCode.Should().Be("recommendation.kill_switch_active");

        _service = CreateService(enabled: true, providerAvailable: false);
        var unavailable = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        unavailable.ErrorCode.Should().Be("recommendation.provider_unavailable");

        var overBudget = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 51));
        overBudget.ErrorCode.Should().Be("recommendation.generation_limit_invalid");

        _replenishment.Verify(service => service.GenerateAsync(
            It.IsAny<ReplenishmentGenerationQuery>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        (await _context.GovernedRecommendations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Provider_timeout_is_reported_as_retryable_without_persisting_proposals()
    {
        _replenishment
            .Setup(service => service.GenerateAsync(
                It.Is<ReplenishmentGenerationQuery>(query => query.DryRun),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (
                ReplenishmentGenerationQuery _,
                string _,
                CancellationToken cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result.Success(new ReplenishmentGenerationResultDto(0, 0, 0, 0, 0, []));
            });
        _service = CreateService(enabled: true, providerTimeoutSeconds: 1);

        var timedOut = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));

        timedOut.ErrorCode.Should().Be("recommendation.provider_timeout");
        timedOut.FirstError?.IsRetryable.Should().BeTrue();
        (await _context.GovernedRecommendations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Execution_failure_is_persisted_and_retried_with_a_new_key()
    {
        var created = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        var recommendation = created.Value.Single();
        var reviewed = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "review-retry", "Reviewed."));
        var approved = await _service.ApproveAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(reviewed.Value.Revision, "approve-retry"));
        _failNormalExecution = true;

        var failed = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(approved.Value.Revision, "execute-attempt-1"));

        failed.ErrorCode.Should().Be("work.command_unavailable");
        var retryable = await _service.GetAsync(recommendation.RecommendationId);
        retryable.Value.Status.Should().Be(RecommendationStatus.ExecutionFailed);
        retryable.Value.ExecutionAttempts.Should().Be(1);
        retryable.Value.LastExecutionErrorCode.Should().Be("work.command_unavailable");

        _failNormalExecution = false;
        var retried = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(retryable.Value.Revision, "execute-attempt-2"));

        retried.IsSuccess.Should().BeTrue(retried.Error);
        retried.Value.Status.Should().Be(RecommendationStatus.Executed);
        retried.Value.ExecutionAttempts.Should().Be(2);
        var history = await _service.GetHistoryAsync(recommendation.RecommendationId);
        history.Value.Select(entry => entry.EventType).Should()
            .Equal("created", "reviewed", "approved", "execution-failed", "executed");
    }

    [Fact]
    public async Task Execution_rechecks_source_state_after_approval()
    {
        var created = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        var recommendation = created.Value.Single();
        var reviewed = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "review-before-change", "Reviewed."));
        var approved = await _service.ApproveAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(reviewed.Value.Revision, "approve-before-change"));
        _currentFingerprint = new string('c', 64);

        var execution = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(approved.Value.Revision, "execute-stale"));

        execution.ErrorCode.Should().Be("recommendation.stale");
        var current = await _service.GetAsync(recommendation.RecommendationId);
        current.Value.Status.Should().Be(RecommendationStatus.Approved);
        _replenishment.Verify(service => service.GenerateAsync(
            It.Is<ReplenishmentGenerationQuery>(query => !query.DryRun),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Workload_recommendation_uses_normal_idempotent_claim_and_quality_reports_outcome()
    {
        var work = CreateWork();
        var suggestion = new WorkSuggestionDto(work, "eligible by deterministic queue score", 9, "DEFAULT", null,
            Score: 12.5m, ScoringBreakdown: "priority=10;due=2.5");
        _workforce
            .Setup(service => service.SuggestAsync(
                It.IsAny<WorkforceSuggestionsQuery>(),
                "reviewer-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WorkforceSuggestionsDto(
                [suggestion],
                _clock.UtcNow.UtcDateTime)));
        _workforce
            .Setup(service => service.ClaimAsync(
                work.Id,
                It.IsAny<WarehouseWorkClaimInput>(),
                "reviewer-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int workId, WarehouseWorkClaimInput _, string _, CancellationToken _) =>
                Result.Success(CreateWork(workId, WarehouseWorkStatus.Assigned, "reviewer-1", revision: 5)));

        var generated = await _service.GenerateAsync(
            RecommendationType.WorkloadPriority,
            new RecommendationGenerationRequest(WarehouseId, 10));

        generated.IsSuccess.Should().BeTrue(generated.Error);
        generated.Value.Should().ContainSingle();
        var recommendation = generated.Value.Single();
        recommendation.Type.Should().Be(RecommendationType.WorkloadPriority);
        recommendation.Action.ActionType.Should().Be("work-claim");

        var reviewed = await _service.ReviewAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(1, "workload-review", "Reviewed."));
        var approved = await _service.ApproveAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(reviewed.Value.Revision, "workload-approve"));
        var executed = await _service.ExecuteAsync(
            recommendation.RecommendationId,
            new RecommendationLifecycleCommand(approved.Value.Revision, "workload-execute"));

        executed.IsSuccess.Should().BeTrue(executed.Error);
        executed.Value.Status.Should().Be(RecommendationStatus.Executed);
        executed.Value.ExecutionReference.Should().Be("warehouse-work:899");
        _workforce.Verify(service => service.ClaimAsync(
            work.Id,
            It.Is<WarehouseWorkClaimInput>(input => input.IdempotencyKey == $"recommendation:{recommendation.RecommendationId}"),
            "reviewer-1",
            It.IsAny<CancellationToken>()), Times.Once);

        var quality = await _service.GetQualityAsync(WarehouseId);
        quality.IsSuccess.Should().BeTrue(quality.Error);
        var workload = quality.Value.Types.Single(value => value.Type == RecommendationType.WorkloadPriority);
        workload.Generated.Should().Be(1);
        workload.BaselineMatched.Should().Be(1);
        workload.Reviewed.Should().Be(1);
        workload.Approved.Should().Be(1);
        workload.Executed.Should().Be(1);
        workload.ExecutionOutcomes.Should().ContainKey("work-claimed").WhoseValue.Should().Be(1);
    }

    private RecommendationGovernanceService CreateService(
        bool enabled,
        bool providerAvailable = true,
        bool killSwitchEnabled = false,
        int providerTimeoutSeconds = 10)
    {
        var adapter = new ReplenishmentRecommendationCommandAdapter(_replenishment.Object);
        var adapters = new IRecommendationCommandAdapter[]
        {
            adapter,
            new SlottingRecommendationCommandAdapter(_slotting.Object, _clock),
            new WorkloadPriorityRecommendationCommandAdapter(_workforce.Object),
            new ExceptionResolutionRecommendationCommandAdapter(
                _inboundExceptions.Object,
                _outboundExceptions.Object),
            new RiskSummaryRecommendationCommandAdapter(_forecasting.Object)
        };
        var sourceFactory = new RecommendationCandidateSourceFactory(
            _replenishment.Object,
            _slotting.Object,
            _workforce.Object,
            _inboundExceptions.Object,
            _outboundExceptions.Object,
            _forecasting.Object,
            _warehouseAccess.Object,
            _clock);
        return new RecommendationGovernanceService(
            _context,
            _provider,
            adapters,
            _replenishment.Object,
            _warehouseAccess.Object,
            _currentUser.Object,
            _clock,
            Options.Create(new RecommendationGovernanceOptions
            {
                Enabled = enabled,
                KillSwitchEnabled = killSwitchEnabled,
                ProviderAvailable = providerAvailable,
                ShadowMode = true,
                ProviderName = DeterministicReplenishmentDraftProvider.ProviderName,
                MaximumProposalsPerRequest = 50,
                ProviderTimeoutSeconds = providerTimeoutSeconds
            }),
            NullLogger<RecommendationGovernanceService>.Instance,
            sourceFactory);
    }

    private static WarehouseWorkDto CreateWork(
        int id = 899,
        WarehouseWorkStatus status = WarehouseWorkStatus.Available,
        string? assignedUserId = null,
        long revision = 4) => new(
            Id: id,
            WorkNumber: $"WW-{id}",
            CreationKey: $"work-source:{id}",
            Type: WarehouseWorkType.Pick,
            WarehouseId: WarehouseId,
            SourceEntityType: "source",
            SourceEntityId: "source-id",
            SourceLineReference: null,
            QueueCode: "DEFAULT",
            Priority: 40,
            DueAtUtc: null,
            TeamCode: null,
            Notes: null,
            Status: status,
            AssignedUserId: assignedUserId,
            AssignedTeamCode: null,
            AssignedByUserId: null,
            AssignedAtUtc: null,
            StartedAtUtc: null,
            PausedAtUtc: null,
            CompletedAtUtc: null,
            CancelledAtUtc: null,
            CompletedByUserId: null,
            CancelledByUserId: null,
            CancellationReason: null,
            ExceptionType: null,
            ExceptionReason: null,
            ExceptionAtUtc: null,
            SupervisorOverride: false,
            OverrideReason: null,
            Revision: revision,
            Lines: [],
            IsTerminal: status is WarehouseWorkStatus.Completed or WarehouseWorkStatus.Cancelled,
            HasExceptions: false);

    private ReplenishmentWorkPlanDto CreatePlan(bool dryRun) =>
        new(
            PolicyId,
            20,
            "ITEM-20",
            WarehouseId,
            30,
            8m,
            0m,
            8m,
            dryRun ? "dry-run-eligible" : "work-created",
            dryRun ? null : 899,
            dryRun ? null : "WORK-899",
            [])
        {
            SourceStateFingerprint = _currentFingerprint,
            CapacityAvailableQuantity = 8m
        };

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
