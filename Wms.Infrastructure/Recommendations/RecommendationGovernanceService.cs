using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Recommendations;
using Wms.Domain.Entities;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Recommendations;

public sealed class RecommendationGovernanceService(
    WmsDbContext context,
    IRecommendationDraftProvider draftProvider,
    IEnumerable<IRecommendationCommandAdapter> commandAdapters,
    IReplenishmentExecutionService replenishmentExecutionService,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<RecommendationGovernanceOptions> options,
    ILogger<RecommendationGovernanceService> logger) : IRecommendationGovernanceService
{
    private const int MaximumPageSize = 100;
    private const int ExpirySweepBatchSize = 500;
    private readonly Dictionary<RecommendationType, IRecommendationCommandAdapter> _commandAdapters =
        commandAdapters.ToDictionary(adapter => adapter.Type);
    private readonly RecommendationGovernanceOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<IReadOnlyList<RecommendationRecord>>> GenerateReplenishmentAsync(
        RecommendationGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = GetActor();
        if (actor.IsFailure)
        {
            return actor.ToFailure<IReadOnlyList<RecommendationRecord>>();
        }

        if (request.WarehouseId <= 0 ||
            request.Limit is < 1 ||
            request.Limit > _options.MaximumProposalsPerRequest)
        {
            return Result.Failure<IReadOnlyList<RecommendationRecord>>(WmsErrors.Validation(
                "recommendation.generation_limit_invalid",
                $"Warehouse ID must be positive and the limit must be between 1 and {_options.MaximumProposalsPerRequest}."));
        }

        var gate = await CheckGenerationGateAsync(request.WarehouseId, cancellationToken);
        if (gate.IsFailure)
        {
            return gate.ToFailure<IReadOnlyList<RecommendationRecord>>();
        }

        var now = clock.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds));
        try
        {
            var plansResult = await replenishmentExecutionService.GenerateAsync(
                new ReplenishmentGenerationQuery(
                    request.WarehouseId,
                    Limit: request.Limit,
                    DryRun: true),
                actor.Value,
                timeout.Token);
            if (plansResult.IsFailure)
            {
                return plansResult.ToFailure<IReadOnlyList<RecommendationRecord>>();
            }

            var output = new List<RecommendationRecord>();
            foreach (var plan in plansResult.Value.Plans
                         .Where(plan => plan.Decision == "dry-run-eligible" &&
                                        plan.PlannedQuantity > 0m &&
                                        !string.IsNullOrWhiteSpace(plan.SourceStateFingerprint)))
            {
                timeout.Token.ThrowIfCancellationRequested();
                var draftResult = await draftProvider.CreateReplenishmentDraftAsync(
                    new ReplenishmentRecommendationSource(plan, now, now.AddDays(7)),
                    timeout.Token);
                if (draftResult.IsFailure)
                {
                    return draftResult.ToFailure<IReadOnlyList<RecommendationRecord>>();
                }

                var permissionCodes = new HashSet<string>(StringComparer.Ordinal)
                {
                    WmsPermissions.ReportsRead,
                    WmsPermissions.InventoryRead
                };
                var governanceRequest = new RecommendationRequest(
                    draftResult.Value,
                    new HashSet<int> { request.WarehouseId },
                    permissionCodes,
                    KillSwitchEnabled: _options.KillSwitchEnabled,
                    ProviderAvailable: _options.ProviderAvailable &&
                                       draftProvider.IsAvailable &&
                                       string.Equals(_options.ProviderName, draftProvider.Name, StringComparison.Ordinal),
                    ShadowMode: _options.ShadowMode);
                var created = RecommendationPolicy.Create(governanceRequest, now);
                if (created.IsFailure)
                {
                    return created.ToFailure<IReadOnlyList<RecommendationRecord>>();
                }

                var saved = await SaveProposalAsync(created.Value, actor.Value, cancellationToken);
                if (saved.IsFailure)
                {
                    return saved.ToFailure<IReadOnlyList<RecommendationRecord>>();
                }

                output.Add(saved.Value);
            }

            return Result.Success<IReadOnlyList<RecommendationRecord>>(output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<IReadOnlyList<RecommendationRecord>>(WmsErrors.Dependency(
                "recommendation.provider_timeout",
                "Recommendation generation exceeded its configured time budget; deterministic operations remain available.",
                isRetryable: true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Governed recommendation generation failed for warehouse {WarehouseId}", request.WarehouseId);
            return Result.Failure<IReadOnlyList<RecommendationRecord>>(WmsErrors.FromException(
                exception,
                "recommendation.generation_failed",
                "Governed recommendations could not be generated."));
        }
    }

    public async Task<Result<RecommendationPage>> SearchAsync(
        RecommendationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Page is < 1 or > 1_000_000 || query.PageSize is < 1 or > MaximumPageSize)
        {
            return Result.Failure<RecommendationPage>(WmsErrors.Validation(
                "recommendation.page_invalid",
                $"Page must be positive and page size must be between 1 and {MaximumPageSize}."));
        }

        var reportsAuthorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReportsRead,
            query.WarehouseId,
            cancellationToken);
        if (reportsAuthorization.IsFailure)
        {
            return reportsAuthorization.ToFailure<RecommendationPage>();
        }

        var readableTypes = new List<int>();
        foreach (var type in Enum.GetValues<RecommendationType>())
        {
            if (query.Type.HasValue && query.Type.Value != type)
            {
                continue;
            }

            if (await warehouseAccessService.HasPermissionAsync(
                    RecommendationPolicy.RequiredPermission(type),
                    cancellationToken))
            {
                readableTypes.Add((int)type);
            }
        }

        if (readableTypes.Count == 0)
        {
            return Result.Failure<RecommendationPage>(WmsErrors.Forbidden(
                "recommendation.permission_denied",
                "The authenticated user cannot view recommendations for this type."));
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        if (query.WarehouseId.HasValue && !scope.HasGlobalAccess &&
            !scope.WarehouseIds.Contains(query.WarehouseId.Value))
        {
            return Result.Failure<RecommendationPage>(WmsErrors.Forbidden(
                "recommendation.warehouse_forbidden",
                "The requested warehouse is outside the authenticated scope."));
        }

        await ExpireDueAsync(scope, query.WarehouseId, cancellationToken);

        var rows = context.GovernedRecommendations.AsNoTracking()
            .Where(row => readableTypes.Contains(row.Type));
        if (query.WarehouseId.HasValue)
        {
            rows = rows.Where(row => row.WarehouseId == query.WarehouseId.Value);
        }
        else if (!scope.HasGlobalAccess)
        {
            var warehouseIds = scope.WarehouseIds.ToArray();
            rows = rows.Where(row => warehouseIds.Contains(row.WarehouseId));
        }

        if (query.Type.HasValue)
        {
            rows = rows.Where(row => row.Type == (int)query.Type.Value);
        }

        if (query.Status.HasValue)
        {
            rows = rows.Where(row => row.Status == (int)query.Status.Value);
        }

        var totalCount = await rows.CountAsync(cancellationToken);
        var entities = await rows
            .OrderByDescending(row => row.Id)
            .ThenBy(row => row.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new RecommendationPage(
            entities.Select(Map).ToArray(),
            query.Page,
            query.PageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)query.PageSize)));
    }

    public async Task<Result<RecommendationRecord>> GetAsync(
        string recommendationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(recommendationId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<RecommendationRecord>(NotFound());
        }

        var authorization = await AuthorizeReadAsync(entity, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<RecommendationRecord>();
        }

        await ExpireIfDueAsync(entity, "system:recommendation-expiry", cancellationToken);
        return Result.Success(Map(entity));
    }

    public async Task<Result<IReadOnlyList<RecommendationHistoryEntry>>> GetHistoryAsync(
        string recommendationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(recommendationId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<IReadOnlyList<RecommendationHistoryEntry>>(NotFound());
        }

        var authorization = await AuthorizeReadAsync(entity, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<RecommendationHistoryEntry>>();
        }

        await ExpireIfDueAsync(entity, "system:recommendation-expiry", cancellationToken);
        var history = await context.GovernedRecommendationEvents.AsNoTracking()
            .Where(entry => entry.RecommendationId == entity.Id)
            .OrderBy(entry => entry.Id)
            .Select(entry => new RecommendationHistoryEntry(
                entry.EventType,
                entry.ActorUserId,
                entry.OccurredAtUtc,
                entry.ResultingRevision,
                entry.Comment,
                entry.StateFingerprint,
                entry.CommandReference,
                entry.OutcomeCode))
            .ToArrayAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RecommendationHistoryEntry>>(history);
    }

    public Task<Result<RecommendationRecord>> ReviewAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(recommendationId, command, "reviewed", cancellationToken);

    public async Task<Result<RecommendationRecord>> ApproveAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        var ready = await LoadLifecycleEntityAsync(recommendationId, command, cancellationToken);
        if (ready.IsFailure)
        {
            return ready.ToFailure<RecommendationRecord>();
        }

        var (entity, actor) = ready.Value;
        var replay = await FindReplayAsync(entity, "approved", command.IdempotencyKey, cancellationToken);
        if (replay)
        {
            return Result.Success(Map(entity));
        }

        var revision = CheckRevision(entity, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return revision.ToFailure<RecommendationRecord>();
        }

        var now = clock.UtcNow;
        if (entity.ExpiresAtUtc <= now &&
            ((RecommendationStatus)entity.Status is RecommendationStatus.Proposed or RecommendationStatus.Reviewed))
        {
            entity.Transition((int)RecommendationStatus.Expired, "Expired before approval.", now);
            context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
                entity.Id,
                EventKey("expired", command.IdempotencyKey),
                "expired",
                actor,
                now,
                entity.Revision,
                entity.LastDispositionComment,
                entity.SourceStateFingerprint));
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.expired",
                "The recommendation expired before approval."));
        }

        if (!_commandAdapters.TryGetValue((RecommendationType)entity.Type, out var adapter))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.command_unavailable",
                "No authoritative WMS command adapter is registered for this recommendation type."));
        }

        var current = Map(entity);
        var revalidation = await adapter.RevalidateAsync(current, actor, cancellationToken);
        if (revalidation.IsFailure)
        {
            return revalidation.ToFailure<RecommendationRecord>();
        }

        var permissionCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            WmsPermissions.ReportsRead,
            RecommendationPolicy.RequiredPermission(current.Type)
        };
        var transition = RecommendationLifecycle.Approve(
            current,
            new RecommendationApprovalRequest(
                revalidation.Value.CurrentStateFingerprint,
                revalidation.Value.DeterministicEligibility,
                new HashSet<int> { entity.WarehouseId },
                permissionCodes),
            now);
        if (transition.IsFailure)
        {
            return transition.ToFailure<RecommendationRecord>();
        }

        var comment = Redact(command.Comment) ?? transition.Value.LastDispositionComment;
        entity.Transition((int)transition.Value.Status, comment, now);
        var saved = await SaveTransitionAsync(
            entity,
            "approved",
            EventKey("approved", command.IdempotencyKey),
            actor,
            now,
            comment,
            cancellationToken,
            revalidation.Value.CurrentStateFingerprint);
        return saved.IsFailure ? saved.ToFailure<RecommendationRecord>() : Result.Success(Map(entity));
    }

    public async Task<Result<RecommendationRecord>> RejectAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        var ready = await LoadLifecycleEntityAsync(recommendationId, command, cancellationToken);
        if (ready.IsFailure)
        {
            return ready.ToFailure<RecommendationRecord>();
        }

        var (entity, actor) = ready.Value;
        if (await FindReplayAsync(entity, "rejected", command.IdempotencyKey, cancellationToken))
        {
            return Result.Success(Map(entity));
        }

        var revision = CheckRevision(entity, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return revision.ToFailure<RecommendationRecord>();
        }

        if (entity.ExpiresAtUtc <= clock.UtcNow &&
            ((RecommendationStatus)entity.Status is RecommendationStatus.Proposed or RecommendationStatus.Reviewed))
        {
            entity.Transition((int)RecommendationStatus.Expired, "Expired before rejection.", clock.UtcNow);
            context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
                entity.Id,
                EventKey("expired", command.IdempotencyKey),
                "expired",
                actor,
                clock.UtcNow,
                entity.Revision,
                entity.LastDispositionComment,
                entity.SourceStateFingerprint));
            await context.SaveChangesAsync(cancellationToken);
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.expired",
                "The recommendation expired before it could be rejected."));
        }

        var lifecycle = RecommendationLifecycle.MarkRejected(Map(entity), Redact(command.Comment) ?? string.Empty);
        if (lifecycle.IsFailure)
        {
            return lifecycle.ToFailure<RecommendationRecord>();
        }

        var comment = lifecycle.Value.LastDispositionComment;
        entity.Transition((int)RecommendationStatus.Rejected, comment, clock.UtcNow);
        var saved = await SaveTransitionAsync(
            entity,
            "rejected",
            EventKey("rejected", command.IdempotencyKey),
            actor,
            clock.UtcNow,
            comment,
            cancellationToken);
        return saved.IsFailure ? saved.ToFailure<RecommendationRecord>() : Result.Success(Map(entity));
    }

    public async Task<Result<RecommendationRecord>> ExpireAsync(
        string recommendationId,
        long expectedRevision,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var command = new RecommendationLifecycleCommand(expectedRevision, idempotencyKey);
        var ready = await LoadLifecycleEntityAsync(recommendationId, command, cancellationToken);
        if (ready.IsFailure)
        {
            return ready.ToFailure<RecommendationRecord>();
        }

        var (entity, actor) = ready.Value;
        if (await FindReplayAsync(entity, "expired", idempotencyKey, cancellationToken))
        {
            return Result.Success(Map(entity));
        }

        var revision = CheckRevision(entity, expectedRevision);
        if (revision.IsFailure)
        {
            return revision.ToFailure<RecommendationRecord>();
        }

        if (entity.ExpiresAtUtc > clock.UtcNow ||
            (RecommendationStatus)entity.Status is not (RecommendationStatus.Proposed or RecommendationStatus.Reviewed))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.expiry_invalid",
                "Only an expired proposed or reviewed recommendation can be marked expired."));
        }

        entity.Transition((int)RecommendationStatus.Expired, "Expired by the recommendation lifecycle.", clock.UtcNow);
        var saved = await SaveTransitionAsync(
            entity,
            "expired",
            EventKey("expired", idempotencyKey),
            actor,
            clock.UtcNow,
            entity.LastDispositionComment,
            cancellationToken);
        return saved.IsFailure ? saved.ToFailure<RecommendationRecord>() : Result.Success(Map(entity));
    }

    public async Task<Result<RecommendationRecord>> ExecuteAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default)
    {
        var ready = await LoadLifecycleEntityAsync(recommendationId, command, cancellationToken);
        if (ready.IsFailure)
        {
            return ready.ToFailure<RecommendationRecord>();
        }

        var (loaded, actor) = ready.Value;
        if (await FindReplayAsync(loaded, "executed", command.IdempotencyKey, cancellationToken))
        {
            return Result.Success(Map(loaded));
        }

        if ((RecommendationStatus)loaded.Status == RecommendationStatus.Executed)
        {
            return Result.Success(Map(loaded));
        }

        var revision = CheckRevision(loaded, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return revision.ToFailure<RecommendationRecord>();
        }

        if (!_commandAdapters.TryGetValue((RecommendationType)loaded.Type, out var adapter))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.command_unavailable",
                "No authoritative WMS command adapter is registered for this recommendation type."));
        }

        if (loaded.ExpiresAtUtc <= clock.UtcNow)
        {
            loaded.Transition((int)RecommendationStatus.Expired, "Expired before execution.", clock.UtcNow);
            var expired = await SaveTransitionAsync(
                loaded,
                "expired",
                EventKey("expired", command.IdempotencyKey),
                actor,
                clock.UtcNow,
                loaded.LastDispositionComment,
                cancellationToken);
            return expired.IsFailure
                ? expired.ToFailure<RecommendationRecord>()
                : Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                    "recommendation.expired",
                    "The recommendation expired before work creation."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var entity = loaded;
        await context.Entry(entity).ReloadAsync(cancellationToken);

        if (await FindReplayAsync(entity, "executed", command.IdempotencyKey, cancellationToken) ||
            (RecommendationStatus)entity.Status == RecommendationStatus.Executed)
        {
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(Map(entity));
        }

        if (entity.Revision != command.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RecommendationRecord>(RevisionConflict());
        }

        if ((RecommendationStatus)entity.Status is not (RecommendationStatus.Approved or RecommendationStatus.ExecutionFailed))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.execution_state_invalid",
                "Only an approved or retryable failed recommendation can create normal WMS work."));
        }

        var current = Map(entity);
        var revalidation = await adapter.RevalidateAsync(current, actor, cancellationToken);
        if (revalidation.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return revalidation.ToFailure<RecommendationRecord>();
        }

        if (!revalidation.Value.DeterministicEligibility ||
            !string.Equals(
                revalidation.Value.CurrentStateFingerprint,
                entity.SourceStateFingerprint,
                StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.stale",
                "Current inventory, capacity, status, lot, serial, license-plate, or ownership state no longer matches the approved proposal."));
        }

        var execution = await adapter.ExecuteAsync(current, actor, cancellationToken);
        if (execution.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            if (execution.FirstError?.Code == "recommendation.stale")
            {
                return execution.ToFailure<RecommendationRecord>();
            }

            var failure = await RecordExecutionFailureAsync(
                recommendationId,
                command.ExpectedRevision,
                command.IdempotencyKey,
                actor,
                execution.FirstError?.Code ?? "recommendation.execution_failed",
                cancellationToken);
            if (failure.IsFailure)
            {
                return failure;
            }

            if (failure.Value.Status == RecommendationStatus.Executed)
            {
                return failure;
            }

            return execution.ToFailure<RecommendationRecord>();
        }

        var executed = RecommendationLifecycle.MarkExecuted(
            current,
            $"warehouse-work:{execution.Value.CommandReference}",
            clock.UtcNow);
        if (executed.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return executed.ToFailure<RecommendationRecord>();
        }

        entity.Transition(
            (int)RecommendationStatus.Executed,
            Redact(command.Comment) ?? executed.Value.LastDispositionComment,
            clock.UtcNow,
            $"warehouse-work:{execution.Value.CommandReference}");
        context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
            entity.Id,
            EventKey("executed", command.IdempotencyKey),
            "executed",
            actor,
            clock.UtcNow,
            entity.Revision,
            entity.LastDispositionComment,
            revalidation.Value.CurrentStateFingerprint,
            entity.ExecutionReference,
            execution.Value.OutcomeCode));

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(Map(entity));
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or ConcurrencyConflictException)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var latest = await FindAsync(recommendationId, cancellationToken);
            if (latest?.Status == (int)RecommendationStatus.Executed)
            {
                return Result.Success(Map(latest));
            }

            return Result.Failure<RecommendationRecord>(RevisionConflict());
        }
    }

    private async Task<Result<RecommendationRecord>> TransitionAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        string eventType,
        CancellationToken cancellationToken)
    {
        var ready = await LoadLifecycleEntityAsync(recommendationId, command, cancellationToken);
        if (ready.IsFailure)
        {
            return ready.ToFailure<RecommendationRecord>();
        }

        var (entity, actor) = ready.Value;
        if (await FindReplayAsync(entity, eventType, command.IdempotencyKey, cancellationToken))
        {
            return Result.Success(Map(entity));
        }

        var revision = CheckRevision(entity, command.ExpectedRevision);
        if (revision.IsFailure)
        {
            return revision.ToFailure<RecommendationRecord>();
        }

        var comment = Redact(command.Comment) ?? string.Empty;
        var lifecycle = RecommendationLifecycle.MarkReviewed(Map(entity), comment, clock.UtcNow);
        if (lifecycle.IsFailure)
        {
            return lifecycle.ToFailure<RecommendationRecord>();
        }

        var targetStatus = lifecycle.Value.Status;
        entity.Transition((int)targetStatus, lifecycle.Value.LastDispositionComment, clock.UtcNow);
        var actualEventType = targetStatus == RecommendationStatus.Expired ? "expired" : eventType;
        var saved = await SaveTransitionAsync(
            entity,
            actualEventType,
            EventKey(actualEventType, command.IdempotencyKey),
            actor,
            clock.UtcNow,
            lifecycle.Value.LastDispositionComment,
            cancellationToken);
        return saved.IsFailure ? saved.ToFailure<RecommendationRecord>() : Result.Success(Map(entity));
    }

    private async Task<Result<(GovernedRecommendation Entity, string Actor)>> LoadLifecycleEntityAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ExpectedRevision < 1 ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey) ||
            command.IdempotencyKey.Trim().Length > 250)
        {
            return Result.Failure<(GovernedRecommendation, string)>(WmsErrors.Validation(
                "recommendation.command_invalid",
                "An expected revision and bounded idempotency key are required."));
        }

        var actor = GetActor();
        if (actor.IsFailure)
        {
            return actor.ToFailure<(GovernedRecommendation, string)>();
        }

        var entity = await FindAsync(recommendationId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<(GovernedRecommendation, string)>(NotFound());
        }

        var authorization = await AuthorizeManageAsync(entity, cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<(GovernedRecommendation, string)>()
            : Result.Success((entity, actor.Value));
    }

    private Result<string> GetActor() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? Result.Success(currentUser.UserId!)
            : Result.Failure<string>(WmsErrors.Unauthorized(
                "authorization.authentication_required",
                "An active authenticated user is required."));

    private async Task<Result> CheckGenerationGateAsync(
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.disabled",
                "Recommendation generation is disabled; deterministic warehouse operations remain available.",
                isRetryable: false));
        }

        if (_options.KillSwitchEnabled)
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.kill_switch_active",
                "Recommendation generation is disabled by the operational kill switch.",
                isRetryable: false));
        }

        if (!_options.ShadowMode)
        {
            return Result.Failure(WmsErrors.Forbidden(
                "recommendation.shadow_required",
                "Recommendation generation must remain in shadow mode."));
        }

        if (!_options.ProviderAvailable || !draftProvider.IsAvailable ||
            !string.Equals(_options.ProviderName, draftProvider.Name, StringComparison.Ordinal))
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.provider_unavailable",
                "The configured recommendation provider is unavailable; deterministic warehouse operations remain available.",
                isRetryable: true));
        }

        if (_options.ProviderTimeoutSeconds is < 1 or > 60 ||
            _options.MaximumProposalsPerRequest is < 1 or > 200)
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.configuration_invalid",
                "Recommendation provider timeout or request budget is outside the supported range.",
                isRetryable: false));
        }

        var reports = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReportsRead,
            warehouseId,
            cancellationToken);
        if (reports.IsFailure)
        {
            return reports;
        }

        var inventory = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            warehouseId,
            cancellationToken);
        if (inventory.IsFailure)
        {
            return inventory;
        }

        return await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            warehouseId,
            cancellationToken);
    }

    private async Task<Result<RecommendationRecord>> SaveProposalAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await context.GovernedRecommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.RecommendationId == recommendation.RecommendationId, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        var entity = new GovernedRecommendation(
            recommendation.RecommendationId,
            (int)recommendation.Type,
            recommendation.WarehouseId,
            recommendation.SourceSnapshotId,
            recommendation.SourceFromUtc,
            recommendation.SourceToUtc,
            recommendation.SourceStateFingerprint,
            recommendation.ProviderName,
            recommendation.ModelVersion,
            recommendation.DeterministicBaselineVersion,
            recommendation.Confidence,
            recommendation.Explanation,
            JsonSerializer.Serialize(recommendation.Action, JsonOptions),
            JsonSerializer.Serialize(recommendation.NumericFeatures, JsonOptions),
            recommendation.ExpectedImpactLow,
            recommendation.ExpectedImpactHigh,
            recommendation.GeneratedAtUtc,
            recommendation.ExpiresAtUtc,
            recommendation.ShadowMode,
            "deterministic-baseline-matched",
            actorUserId);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.GovernedRecommendations.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
            context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
                entity.Id,
                EventKey("created", recommendation.RecommendationId),
                "created",
                actorUserId,
                clock.UtcNow,
                entity.Revision,
                stateFingerprint: entity.SourceStateFingerprint,
                outcomeCode: entity.ShadowComparison));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(Map(entity));
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            var replay = await context.GovernedRecommendations.AsNoTracking()
                .SingleOrDefaultAsync(row => row.RecommendationId == recommendation.RecommendationId, cancellationToken);
            if (replay is not null)
            {
                return Result.Success(Map(replay));
            }

            logger.LogError(exception, "Governed recommendation persistence failed for {RecommendationId}",
                recommendation.RecommendationId);
            return Result.Failure<RecommendationRecord>(WmsErrors.FromException(
                exception,
                "recommendation.persistence_failed",
                "The recommendation could not be persisted."));
        }
    }

    private async Task<Result<RecommendationRecord>> SaveTransitionAsync(
        GovernedRecommendation entity,
        string eventType,
        string eventIdempotencyKey,
        string actorUserId,
        DateTimeOffset now,
        string? comment,
        CancellationToken cancellationToken,
        string? stateFingerprint = null,
        string? commandReference = null,
        string? outcomeCode = null)
    {
        context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
            entity.Id,
            eventIdempotencyKey,
            eventType,
            actorUserId,
            now,
            entity.Revision,
            Redact(comment),
            stateFingerprint,
            commandReference,
            outcomeCode));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(entity));
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or ConcurrencyConflictException)
        {
            context.ChangeTracker.Clear();
            return Result.Failure<RecommendationRecord>(RevisionConflict());
        }
    }

    private async Task<Result<RecommendationRecord>> RecordExecutionFailureAsync(
        string recommendationId,
        long expectedRevision,
        string idempotencyKey,
        string actorUserId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var entity = await FindAsync(recommendationId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<RecommendationRecord>(NotFound());
        }

        if (await FindReplayAsync(entity, "executed", idempotencyKey, cancellationToken))
        {
            return Result.Success(Map(entity));
        }

        if (entity.Revision != expectedRevision)
        {
            return Result.Failure<RecommendationRecord>(RevisionConflict());
        }

        if ((RecommendationStatus)entity.Status is not (RecommendationStatus.Approved or RecommendationStatus.ExecutionFailed))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.execution_state_invalid",
                "The recommendation changed while the normal command was being processed."));
        }

        var now = clock.UtcNow;
        entity.Transition(
            (int)RecommendationStatus.ExecutionFailed,
            "The normal WMS command failed; a new idempotency key may retry after the cause is corrected.",
            now,
            executionErrorCode: errorCode);
        context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
            entity.Id,
            EventKey("executed", idempotencyKey),
            "execution-failed",
            actorUserId,
            now,
            entity.Revision,
            entity.LastDispositionComment,
            entity.SourceStateFingerprint,
            outcomeCode: errorCode));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(Map(entity));
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or ConcurrencyConflictException)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return Result.Failure<RecommendationRecord>(RevisionConflict());
        }
    }

    private async Task<Result> AuthorizeReadAsync(
        GovernedRecommendation recommendation,
        CancellationToken cancellationToken)
    {
        var reportAuthorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReportsRead,
            recommendation.WarehouseId,
            cancellationToken);
        if (reportAuthorization.IsFailure)
        {
            return reportAuthorization;
        }

        return await warehouseAccessService.AuthorizeAsync(
            RecommendationPolicy.RequiredPermission((RecommendationType)recommendation.Type),
            recommendation.WarehouseId,
            cancellationToken);
    }

    private async Task<Result> AuthorizeManageAsync(
        GovernedRecommendation recommendation,
        CancellationToken cancellationToken)
    {
        var read = await AuthorizeReadAsync(recommendation, cancellationToken);
        if (read.IsFailure)
        {
            return read;
        }

        return await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ApprovalManage,
            recommendation.WarehouseId,
            cancellationToken);
    }

    private async Task<bool> FindReplayAsync(
        GovernedRecommendation entity,
        string eventType,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await context.GovernedRecommendationEvents.AsNoTracking().AnyAsync(
            entry => entry.RecommendationId == entity.Id &&
                     entry.IdempotencyKey == EventKey(eventType, idempotencyKey),
            cancellationToken);

    private static Result CheckRevision(GovernedRecommendation entity, long expectedRevision) =>
        entity.Revision == expectedRevision
            ? Result.Success()
            : Result.Failure(RevisionConflict());

    private bool ExpireWithoutSaving(
        GovernedRecommendation entity,
        string actorUserId,
        DateTimeOffset now)
    {
        if (entity.ExpiresAtUtc > now ||
            (RecommendationStatus)entity.Status is not (RecommendationStatus.Proposed or RecommendationStatus.Reviewed))
        {
            return false;
        }

        entity.Transition((int)RecommendationStatus.Expired, "Expired before approval.", now);
        context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
            entity.Id,
            EventKey("expired", $"system:{entity.Id}:{entity.Revision}"),
            "expired",
            actorUserId,
            now,
            entity.Revision,
            entity.LastDispositionComment,
            entity.SourceStateFingerprint,
            outcomeCode: "expiry-policy"));
        return true;
    }

    private async Task ExpireIfDueAsync(
        GovernedRecommendation entity,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (!ExpireWithoutSaving(entity, actorUserId, clock.UtcNow))
        {
            return;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or ConcurrencyConflictException)
        {
            context.ChangeTracker.Clear();
        }
    }

    private async Task ExpireDueAsync(
        WarehouseAccessScope scope,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lastId = 0;
        while (true)
        {
            var rows = context.GovernedRecommendations
                .Where(row => row.Id > lastId &&
                              (row.Status == (int)RecommendationStatus.Proposed ||
                               row.Status == (int)RecommendationStatus.Reviewed));
            if (warehouseId.HasValue)
            {
                rows = rows.Where(row => row.WarehouseId == warehouseId.Value);
            }
            else if (!scope.HasGlobalAccess)
            {
                var ids = scope.WarehouseIds.ToArray();
                rows = rows.Where(row => ids.Contains(row.WarehouseId));
            }

            var batch = await rows.OrderBy(row => row.Id)
                .Take(ExpirySweepBatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var entity in batch)
            {
                if (entity.ExpiresAtUtc <= now)
                {
                    ExpireWithoutSaving(entity, "system:recommendation-expiry", now);
                }
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException or ConcurrencyConflictException)
            {
                context.ChangeTracker.Clear();
                logger.LogDebug(exception, "A concurrent recommendation expiry sweep won the state transition.");
            }

            lastId = batch[^1].Id;
            context.ChangeTracker.Clear();
        }
    }

    private async Task<GovernedRecommendation?> FindAsync(
        string recommendationId,
        CancellationToken cancellationToken) =>
        IsRecommendationIdValid(recommendationId)
            ? await context.GovernedRecommendations.SingleOrDefaultAsync(
                row => row.RecommendationId == recommendationId,
                cancellationToken)
            : null;

    private static RecommendationRecord Map(GovernedRecommendation entity) =>
        new(
            entity.RecommendationId,
            (RecommendationType)entity.Type,
            (RecommendationStatus)entity.Status,
            entity.WarehouseId,
            entity.SourceSnapshotId,
            entity.SourceFromUtc,
            entity.SourceToUtc,
            entity.SourceStateFingerprint,
            entity.ProviderName,
            entity.ModelVersion,
            entity.DeterministicBaselineVersion,
            entity.Confidence,
            entity.Explanation,
            JsonSerializer.Deserialize<RecommendationAction>(entity.ActionJson, JsonOptions)
                ?? throw new InvalidOperationException("Stored recommendation action is invalid."),
            JsonSerializer.Deserialize<Dictionary<string, decimal>>(entity.NumericFeaturesJson, JsonOptions)
                ?? new Dictionary<string, decimal>(StringComparer.Ordinal),
            entity.ExpectedImpactLow,
            entity.ExpectedImpactHigh,
            entity.GeneratedAtUtc,
            entity.ExpiresAtUtc,
            entity.ShadowMode,
            entity.RequiresRevalidation,
            entity.CanMutateInventory,
            entity.LastDispositionComment,
            entity.Revision,
            entity.ExecutionReference,
            entity.LastExecutionErrorCode,
            entity.ExecutionAttempts,
            entity.ShadowComparison);

    private static ResultError NotFound() => WmsErrors.NotFound(
        "recommendation.not_found",
        "The requested recommendation was not found.");

    private static ResultError RevisionConflict() => WmsErrors.Conflict(
        "recommendation.revision_conflict",
        "The recommendation changed since it was read. Reload it and submit a new decision.");

    private static string EventKey(string eventType, string idempotencyKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim())))
            .ToLowerInvariant();
        return $"{eventType}:{hash}";
    }

    private static bool IsRecommendationIdValid(string recommendationId) =>
        !string.IsNullOrWhiteSpace(recommendationId) &&
        recommendationId.Length == 64 &&
        recommendationId.All(Uri.IsHexDigit);

    private static string? Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var safe = Regex.Replace(value.Trim(),
            @"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]+",
            "$1[redacted]");
        safe = Regex.Replace(safe,
            @"(?i)\b(api[_-]?key|password|token)\s*[:=]\s*[^\s,;]+",
            "$1=[redacted]");
        safe = Regex.Replace(safe,
            @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
            "[redacted-email]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return safe.Length <= RecommendationPolicy.MaximumExplanationLength
            ? safe
            : safe[..RecommendationPolicy.MaximumExplanationLength];
    }
}
