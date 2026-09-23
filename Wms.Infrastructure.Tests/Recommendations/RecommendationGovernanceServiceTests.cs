using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Recommendations;
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

        _service = CreateService(enabled: true, providerAvailable: false);
        var unavailable = await _service.GenerateReplenishmentAsync(
            new RecommendationGenerationRequest(WarehouseId, 10));
        unavailable.ErrorCode.Should().Be("recommendation.provider_unavailable");
        _replenishment.Verify(service => service.GenerateAsync(
            It.IsAny<ReplenishmentGenerationQuery>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

    private RecommendationGovernanceService CreateService(
        bool enabled,
        bool providerAvailable = true)
    {
        var adapter = new ReplenishmentRecommendationCommandAdapter(_replenishment.Object);
        return new RecommendationGovernanceService(
            _context,
            _provider,
            [adapter],
            _replenishment.Object,
            _warehouseAccess.Object,
            _currentUser.Object,
            _clock,
            Options.Create(new RecommendationGovernanceOptions
            {
                Enabled = enabled,
                KillSwitchEnabled = false,
                ProviderAvailable = providerAvailable,
                ShadowMode = true,
                ProviderName = DeterministicReplenishmentDraftProvider.ProviderName,
                MaximumProposalsPerRequest = 50,
                ProviderTimeoutSeconds = 10
            }),
            NullLogger<RecommendationGovernanceService>.Instance);
    }

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
