using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Produces deterministic, explainable slotting recommendations. Hard
/// location/item/capacity/work constraints are evaluated before weighted
/// scoring; approval delegates movement to existing replenishment work.
/// </summary>
public sealed class SlottingService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    IWarehouseWorkService warehouseWorkService,
    ILogger<SlottingService> logger) : ISlottingService
{
    private const string SourceDataVersion = "slotting-v1";
    private const int MaximumPolicyLimit = 2_000;
    private const int MaximumRecommendationLimit = 2_000;

    public async Task<Result<SlottingPolicyDto>> SavePolicyAsync(
        int? policyId,
        SlottingPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<SlottingPolicyDto>(WmsErrors.Validation(
                "inventory.slotting_actor_required",
                "An authenticated actor is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SlottingPolicyDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<SlottingPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            var policyKey = input.PolicyKey.Trim().ToUpperInvariant();
            var effectiveFromUtc = NormalizeUtc(input.EffectiveFromUtc);
            DateTime? effectiveToUtc = input.EffectiveToUtc.HasValue
                ? NormalizeUtc(input.EffectiveToUtc.Value)
                : null;
            var duplicate = await context.SlottingPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.PolicyKey == policyKey,
                    cancellationToken);
            if (duplicate)
            {
                return Result.Failure<SlottingPolicyDto>(WmsErrors.Conflict(
                    "inventory.slotting_policy_key_conflict",
                    "A slotting policy with this key already exists in the warehouse."));
            }

            var overlap = await context.SlottingPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.IsActive &&
                    policy.EffectiveFromUtc < (effectiveToUtc ?? DateTime.MaxValue) &&
                    (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > effectiveFromUtc),
                    cancellationToken);
            if (overlap)
            {
                return Result.Failure<SlottingPolicyDto>(WmsErrors.Conflict(
                    "inventory.slotting_policy_effective_overlap",
                    "Another active slotting policy overlaps the requested effective period."));
            }

            SlottingPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.SlottingPolicies
                    .SingleOrDefaultAsync(candidate => candidate.Id == policyId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException();
                if (policy.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<SlottingPolicyDto>(WmsErrors.Conflict(
                        "inventory.slotting_policy_identity_immutable",
                        "A slotting policy cannot be moved to another warehouse."));
                }

                policy.Update(
                    input.Name,
                    input.LookbackDays,
                    input.VelocityWeight,
                    input.TravelWeight,
                    input.SpaceWeight,
                    input.ReplenishmentWeight,
                    input.AffinityWeight,
                    input.MaxRecommendationsPerItem,
                    input.RecommendationExpiryDays,
                    effectiveFromUtc,
                    effectiveToUtc,
                    input.AllowedLocationTypes);
            }
            else
            {
                policy = new SlottingPolicy(
                    input.WarehouseId,
                    policyKey,
                    input.Name,
                    input.LookbackDays,
                    input.VelocityWeight,
                    input.TravelWeight,
                    input.SpaceWeight,
                    input.ReplenishmentWeight,
                    input.AffinityWeight,
                    input.MaxRecommendationsPerItem,
                    input.RecommendationExpiryDays,
                    effectiveFromUtc,
                    effectiveToUtc,
                    input.AllowedLocationTypes);
                await context.SlottingPolicies.AddAsync(policy, cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SlottingPolicyChanged,
                    WmsAuditEntityTypes.SlottingPolicy,
                    policyId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyKey"] = policy.PolicyKey,
                        ["lookbackDays"] = input.LookbackDays,
                        ["velocityWeight"] = input.VelocityWeight,
                        ["travelWeight"] = input.TravelWeight,
                        ["spaceWeight"] = input.SpaceWeight,
                        ["replenishmentWeight"] = input.ReplenishmentWeight,
                        ["affinityWeight"] = input.AffinityWeight,
                        ["effectiveFromUtc"] = effectiveFromUtc,
                        ["effectiveToUtc"] = effectiveToUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPolicy(policy, warehouse));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result.Failure<SlottingPolicyDto>(WmsErrors.NotFound(
                "inventory.slotting_policy_not_found",
                "The requested slotting policy was not found."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SlottingPolicyDto>(WmsErrors.Validation(
                "inventory.slotting_policy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting policy save failed");
            return Result.Failure<SlottingPolicyDto>(WmsErrors.FromException(
                exception,
                "inventory.slotting_policy_save_failed",
                "The slotting policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<SlottingPolicyDto>>> SearchPoliciesAsync(
        SlottingPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<SlottingPolicyDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = ApplyPolicyScope(context.SlottingPolicies.AsNoTracking(), scope)
                .Where(policy => query.IncludeInactive || policy.IsActive);
            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            var rows = await policies
                .Include(policy => policy.Warehouse)
                .OrderBy(policy => policy.Warehouse.Code)
                .ThenBy(policy => policy.PolicyKey)
                .ThenByDescending(policy => policy.EffectiveFromUtc)
                .Take(MaximumPolicyLimit)
                .ToListAsync(cancellationToken);
            return Result.Success<IReadOnlyList<SlottingPolicyDto>>(
                rows.Select(policy => MapPolicy(policy, policy.Warehouse)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting policy search failed");
            return Result.Failure<IReadOnlyList<SlottingPolicyDto>>(WmsErrors.FromException(
                exception,
                "inventory.slotting_policy_search_failed",
                "Slotting policies could not be loaded."));
        }
    }

    public async Task<Result<SlottingAnalysisResultDto>> AnalyzeAsync(
        SlottingAnalysisQuery query,
        string actorUserId,
        bool internalExecution = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<SlottingAnalysisResultDto>(WmsErrors.Validation(
                "inventory.slotting_actor_required",
                "An authenticated actor is required."));
        }

        if (!internalExecution)
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<SlottingAnalysisResultDto>();
            }
        }

        try
        {
            var asOfUtc = NormalizeUtc(query.AsOfUtc ?? clock.UtcNow.UtcDateTime);
            var scope = internalExecution
                ? new WarehouseAccessScope(true, new HashSet<int>())
                : await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policiesQuery = ApplyPolicyScope(
                    context.SlottingPolicies.AsNoTracking(),
                    scope)
                .Where(policy =>
                    policy.IsActive &&
                    policy.EffectiveFromUtc <= asOfUtc &&
                    (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > asOfUtc));
            if (query.WarehouseId.HasValue)
            {
                policiesQuery = policiesQuery.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.PolicyId.HasValue)
            {
                policiesQuery = policiesQuery.Where(policy => policy.Id == query.PolicyId.Value);
            }

            var policies = await policiesQuery
                .Include(policy => policy.Warehouse)
                .OrderBy(policy => policy.WarehouseId)
                .ThenBy(policy => policy.Id)
                .ToListAsync(cancellationToken);
            if (query.PolicyId.HasValue && policies.Count == 0)
            {
                return Result.Failure<SlottingAnalysisResultDto>(WmsErrors.NotFound(
                    "inventory.slotting_policy_not_found",
                    "No effective slotting policy matched the request."));
            }

            var state = new AnalysisState(query.DryRun);
            if (!query.DryRun)
            {
                var expired = await context.SlottingRecommendations
                    .Where(recommendation =>
                        recommendation.Status == SlottingRecommendationStatus.Pending &&
                        recommendation.ExpiresAtUtc <= asOfUtc &&
                        (!query.WarehouseId.HasValue || recommendation.WarehouseId == query.WarehouseId.Value))
                    .ToListAsync(cancellationToken);
                foreach (var recommendation in expired)
                {
                    recommendation.Expire(asOfUtc);
                }
            }

            foreach (var policy in policies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluation = await EvaluatePolicyAsync(
                    policy,
                    asOfUtc,
                    query.ItemId,
                    NormalizeLimit(query.Limit, MaximumRecommendationLimit),
                    query.DryRun,
                    actorUserId,
                    cancellationToken);
                state.Add(policy, evaluation);
            }

            if (!query.DryRun)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SlottingAnalysisCompleted,
                        WmsAuditEntityTypes.SlottingPolicy,
                        $"analysis:{asOfUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}",
                        query.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["policiesExamined"] = state.PoliciesExamined,
                            ["itemsExamined"] = state.ItemsExamined,
                            ["recommendationsCreated"] = state.RecommendationsCreated,
                            ["recommendationsReused"] = state.RecommendationsReused,
                            ["blocked"] = state.Blocked,
                            ["asOfUtc"] = asOfUtc
                        },
                        ActorUserId: actorUserId),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            var recommendations = query.DryRun
                ? state.DryRunRecommendations
                : await LoadRunRecommendationsAsync(state.RunKeys, cancellationToken);
            return Result.Success(new SlottingAnalysisResultDto(
                state.PoliciesExamined,
                state.ItemsExamined,
                state.RecommendationsCreated,
                state.RecommendationsReused,
                state.Blocked,
                query.DryRun,
                recommendations));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SlottingAnalysisResultDto>(WmsErrors.Validation(
                "inventory.slotting_analysis_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting analysis failed");
            return Result.Failure<SlottingAnalysisResultDto>(WmsErrors.FromException(
                exception,
                "inventory.slotting_analysis_failed",
                "Slotting recommendations could not be generated."));
        }
    }

    public async Task<Result<IReadOnlyList<SlottingRecommendationDto>>> SearchRecommendationsAsync(
        SlottingRecommendationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<SlottingRecommendationDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var recommendations = ApplyRecommendationScope(
                    context.SlottingRecommendations.AsNoTracking(),
                    scope)
                .Include(recommendation => recommendation.Warehouse)
                .Include(recommendation => recommendation.Item)
                .Include(recommendation => recommendation.SourceLocation)
                .Include(recommendation => recommendation.TargetLocation)
                .AsQueryable();
            if (query.WarehouseId.HasValue)
            {
                recommendations = recommendations.Where(
                    recommendation => recommendation.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                recommendations = recommendations.Where(
                    recommendation => recommendation.ItemId == query.ItemId.Value);
            }

            if (query.Status.HasValue)
            {
                recommendations = recommendations.Where(
                    recommendation => recommendation.Status == query.Status.Value);
            }
            else if (!query.IncludeHistorical)
            {
                recommendations = recommendations.Where(
                    recommendation => recommendation.Status == SlottingRecommendationStatus.Pending ||
                                       recommendation.Status == SlottingRecommendationStatus.Approved);
            }

            var rows = await recommendations
                .OrderBy(recommendation => recommendation.Status)
                .ThenByDescending(recommendation => recommendation.Score)
                .ThenBy(recommendation => recommendation.Id)
                .Take(NormalizeLimit(query.Limit, MaximumRecommendationLimit))
                .ToListAsync(cancellationToken);
            return Result.Success<IReadOnlyList<SlottingRecommendationDto>>(
                rows.Select(Map).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting recommendation search failed");
            return Result.Failure<IReadOnlyList<SlottingRecommendationDto>>(WmsErrors.FromException(
                exception,
                "inventory.slotting_recommendation_search_failed",
                "Slotting recommendations could not be loaded."));
        }
    }

    public async Task<Result<SlottingRecommendationDto>> ApproveAsync(
        int recommendationId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Validation(
                "inventory.slotting_actor_required",
                "An authenticated actor is required."));
        }

        var recommendation = await context.SlottingRecommendations
            .Include(value => value.Warehouse)
            .Include(value => value.Item)
            .Include(value => value.SourceLocation)
            .Include(value => value.TargetLocation)
            .SingleOrDefaultAsync(value => value.Id == recommendationId, cancellationToken);
        if (recommendation is null)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.NotFound(
                "inventory.slotting_recommendation_not_found",
                "The requested slotting recommendation was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            recommendation.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SlottingRecommendationDto>();
        }

        try
        {
            if (recommendation.Status == SlottingRecommendationStatus.WorkCreated)
            {
                return Result.Success(Map(recommendation));
            }

            var now = clock.UtcNow.UtcDateTime;
            if (recommendation.Status == SlottingRecommendationStatus.Pending && now >= recommendation.ExpiresAtUtc)
            {
                recommendation.Expire(now);
                await context.SaveChangesAsync(cancellationToken);
                return Result.Failure<SlottingRecommendationDto>(WmsErrors.Conflict(
                    "inventory.slotting_recommendation_expired",
                    "The slotting recommendation has expired and cannot be approved."));
            }

            if (recommendation.Status == SlottingRecommendationStatus.Pending)
            {
                recommendation.Approve(actorUserId, now);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SlottingRecommendationApproved,
                        WmsAuditEntityTypes.SlottingRecommendation,
                        recommendation.Id.ToString(CultureInfo.InvariantCulture),
                        recommendation.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["status"] = recommendation.Status.ToString(),
                            ["recommendationKey"] = recommendation.RecommendationKey
                        },
                        ActorUserId: actorUserId),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            if (recommendation.Status != SlottingRecommendationStatus.Approved)
            {
                return Result.Failure<SlottingRecommendationDto>(WmsErrors.Conflict(
                    "inventory.slotting_recommendation_not_approvable",
                    $"A recommendation in {recommendation.Status} cannot be approved."));
            }

            var workResult = await warehouseWorkService.CreateAsync(
                new WarehouseWorkInput(
                    CreationKey: $"slotting:{recommendation.Id.ToString(CultureInfo.InvariantCulture)}",
                    Type: WarehouseWorkType.Replenishment,
                    WarehouseId: recommendation.WarehouseId,
                    SourceEntityType: WmsAuditEntityTypes.SlottingRecommendation,
                    SourceEntityId: recommendation.Id.ToString(CultureInfo.InvariantCulture),
                    Priority: 40,
                    QueueCode: "SLOTTING",
                    Notes: "Approved deterministic slotting move; score estimates are advisory.",
                    MakeAvailable: true,
                    Lines:
                    [
                        new WarehouseWorkLineInput(
                            1,
                            recommendation.WarehouseId,
                            recommendation.ItemId,
                            recommendation.Quantity,
                            recommendation.BaseUnitOfMeasure,
                            recommendation.SourceLocationId,
                            recommendation.TargetLocationId,
                            SourceReference: recommendation.RecommendationKey,
                            DimensionsSnapshot: recommendation.ConstraintSnapshotJson)
                    ]),
                actorUserId,
                cancellationToken);
            if (workResult.IsFailure)
            {
                return workResult.ToFailure<SlottingRecommendationDto>();
            }

            recommendation.MarkWorkCreated(workResult.Value.Id, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SlottingWorkCreated,
                    WmsAuditEntityTypes.SlottingRecommendation,
                    recommendation.Id.ToString(CultureInfo.InvariantCulture),
                    recommendation.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["workId"] = workResult.Value.Id,
                        ["status"] = recommendation.Status.ToString()
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(recommendation));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Validation(
                "inventory.slotting_approval_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Conflict(
                "inventory.slotting_approval_conflict",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting recommendation approval failed for {RecommendationId}", recommendationId);
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.FromException(
                exception,
                "inventory.slotting_approval_failed",
                "The slotting recommendation could not be approved."));
        }
    }

    public async Task<Result<SlottingRecommendationDto>> RejectAsync(
        int recommendationId,
        SlottingRejectionInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Validation(
                "inventory.slotting_actor_required",
                "An authenticated actor is required."));
        }

        var recommendation = await context.SlottingRecommendations
            .Include(value => value.Warehouse)
            .Include(value => value.Item)
            .Include(value => value.SourceLocation)
            .Include(value => value.TargetLocation)
            .SingleOrDefaultAsync(value => value.Id == recommendationId, cancellationToken);
        if (recommendation is null)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.NotFound(
                "inventory.slotting_recommendation_not_found",
                "The requested slotting recommendation was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            recommendation.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SlottingRecommendationDto>();
        }

        try
        {
            var now = clock.UtcNow.UtcDateTime;
            if (recommendation.Status == SlottingRecommendationStatus.Pending && now >= recommendation.ExpiresAtUtc)
            {
                recommendation.Expire(now);
                await context.SaveChangesAsync(cancellationToken);
                return Result.Failure<SlottingRecommendationDto>(WmsErrors.Conflict(
                    "inventory.slotting_recommendation_expired",
                    "The slotting recommendation has expired and cannot be rejected as an active proposal."));
            }

            recommendation.Reject(actorUserId, input.Reason, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SlottingRecommendationRejected,
                    WmsAuditEntityTypes.SlottingRecommendation,
                    recommendation.Id.ToString(CultureInfo.InvariantCulture),
                    recommendation.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = recommendation.Status.ToString(),
                        ["reason"] = input.Reason
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(recommendation));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Validation(
                "inventory.slotting_rejection_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.Conflict(
                "inventory.slotting_rejection_conflict",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Slotting recommendation rejection failed for {RecommendationId}", recommendationId);
            return Result.Failure<SlottingRecommendationDto>(WmsErrors.FromException(
                exception,
                "inventory.slotting_rejection_failed",
                "The slotting recommendation could not be rejected."));
        }
    }

    private async Task<PolicyEvaluation> EvaluatePolicyAsync(
        SlottingPolicy policy,
        DateTime asOfUtc,
        int? requestedItemId,
        int limit,
        bool dryRun,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var fromUtc = asOfUtc.AddDays(-policy.LookbackDays);
        var runKey = BuildRunKey(policy, asOfUtc);
        var balances = await context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Item)
            .ThenInclude(item => item.Packagings)
            .Include(balance => balance.Location)
            .Include(balance => balance.InventoryStatus)
            .Where(balance =>
                balance.WarehouseId == policy.WarehouseId &&
                balance.OnHandQuantity > 0m &&
                balance.InventoryStatus.IsActive &&
                balance.InventoryStatus.IsAllocatable &&
                balance.InventoryStatus.IsPickable &&
                (!requestedItemId.HasValue || balance.ItemId == requestedItemId.Value))
            .ToListAsync(cancellationToken);

        var transactionRows = await context.InventoryTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.WarehouseId == policy.WarehouseId &&
                transaction.OccurredAtUtc >= fromUtc &&
                transaction.OccurredAtUtc < asOfUtc &&
                (transaction.Type == InventoryTransactionType.Pick ||
                 transaction.Type == InventoryTransactionType.Ship ||
                 transaction.Type == InventoryTransactionType.Replenishment))
            .Select(transaction => new TransactionMetric(
                transaction.ItemId,
                transaction.Type,
                transaction.QuantityDelta))
            .ToListAsync(cancellationToken);

        var orderRows = await context.SalesOrderLines
            .AsNoTracking()
            .Where(line =>
                line.SalesOrder.WarehouseId == policy.WarehouseId &&
                line.SalesOrder.OrderDate >= DateOnly.FromDateTime(fromUtc) &&
                line.SalesOrder.OrderDate <= DateOnly.FromDateTime(asOfUtc))
            .Select(line => new OrderMetric(line.ItemId, line.OrderedBaseQuantity))
            .ToListAsync(cancellationToken);

        var activeWorkRows = await context.WarehouseWorks
            .AsNoTracking()
            .Where(work =>
                work.WarehouseId == policy.WarehouseId &&
                work.Status != WarehouseWorkStatus.Completed &&
                work.Status != WarehouseWorkStatus.Cancelled)
            .SelectMany(work => work.Lines.Select(line => new OpenWorkMetric(
                line.ItemId,
                line.SourceLocationId,
                line.DestinationLocationId)))
            .ToListAsync(cancellationToken);

        var openWorkKeys = new HashSet<(int ItemId, int LocationId)>();
        foreach (var row in activeWorkRows)
        {
            if (row.SourceLocationId.HasValue)
            {
                openWorkKeys.Add((row.ItemId, row.SourceLocationId.Value));
            }

            if (row.DestinationLocationId.HasValue)
            {
                openWorkKeys.Add((row.ItemId, row.DestinationLocationId.Value));
            }
        }

        var locations = await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == policy.WarehouseId && location.IsActive)
            .OrderBy(location => location.Priority)
            .ThenBy(location => location.Code)
            .ToListAsync(cancellationToken);
        var allowedTypes = policy.GetAllowedLocationTypes();
        locations = locations
            .Where(location => allowedTypes.Contains(location.Type))
            .ToList();

        var summaries = BuildLocationSummaries(balances);
        var items = balances
            .GroupBy(balance => balance.ItemId)
            .Select(group => group.First().Item)
            .OrderBy(item => item.Sku)
            .ToArray();
        var movementByItem = transactionRows
            .GroupBy(row => row.ItemId)
            .ToDictionary(
                group => group.Key,
                group => new ActivityMetric(
                    group.Where(row => row.Type is InventoryTransactionType.Pick or InventoryTransactionType.Ship)
                        .Sum(row => Math.Abs(row.QuantityDelta)),
                    group.Count(row => row.Type is InventoryTransactionType.Pick or InventoryTransactionType.Ship),
                    group.Where(row => row.Type == InventoryTransactionType.Replenishment)
                        .Sum(row => Math.Abs(row.QuantityDelta)),
                    group.Count(row => row.Type == InventoryTransactionType.Replenishment)));
        var orderByItem = orderRows
            .GroupBy(row => row.ItemId)
            .ToDictionary(group => group.Key, group => new OrderActivityMetric(
                group.Count(),
                group.Sum(row => row.OrderedBaseQuantity)));
        var maxVelocity = movementByItem.Values.Select(value => value.PickQuantity).DefaultIfEmpty(0m).Max();
        var maxOrderCount = orderByItem.Values.Select(value => value.OrderCount).DefaultIfEmpty(0).Max();
        var maxPriority = locations.Select(location => location.Priority).DefaultIfEmpty(0).Max();
        var existing = dryRun
            ? new Dictionary<string, SlottingRecommendation>(StringComparer.Ordinal)
            : await context.SlottingRecommendations
                .Where(recommendation => recommendation.AnalysisRunKey == runKey)
                .ToDictionaryAsync(recommendation => recommendation.RecommendationKey, StringComparer.Ordinal, cancellationToken);
        var candidates = new List<RecommendationCandidate>();
        var blocked = 0;
        var reused = 0;
        var created = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = balances
                .Where(balance =>
                    balance.ItemId == item.Id &&
                    balance.AvailableQuantity > 0m &&
                    !openWorkKeys.Contains((item.Id, balance.LocationId)))
                .OrderByDescending(balance => balance.AvailableQuantity)
                .ThenBy(balance => balance.Location.Priority)
                .ThenBy(balance => balance.Id)
                .FirstOrDefault();
            if (source is null)
            {
                blocked++;
                continue;
            }

            var sourceSummary = summaries.TryGetValue(source.LocationId, out var loadedSourceSummary)
                ? loadedSourceSummary
                : LocationSummary.Empty;
            var activity = movementByItem.GetValueOrDefault(item.Id) ?? ActivityMetric.Empty;
            var orderActivity = orderByItem.GetValueOrDefault(item.Id) ?? OrderActivityMetric.Empty;
            var sourceEvaluation = ScoreLocation(
                item,
                source.Location,
                sourceSummary,
                source.AvailableQuantity,
                source.LicensePlateId.HasValue,
                activity,
                orderActivity,
                maxVelocity,
                maxOrderCount,
                maxPriority,
                policy);
            var possible = new List<CandidateEvaluation>();
            foreach (var target in locations)
            {
                if (target.Id == source.LocationId)
                {
                    continue;
                }

                var targetSummary = summaries.TryGetValue(target.Id, out var loadedTargetSummary)
                    ? loadedTargetSummary
                    : LocationSummary.Empty;
                var evaluation = EvaluateTarget(
                    item,
                    source,
                    target,
                    targetSummary,
                    openWorkKeys,
                    activity,
                    orderActivity,
                    maxVelocity,
                    maxOrderCount,
                    maxPriority,
                    policy);
                if (evaluation.IsAllowed)
                {
                    possible.Add(evaluation);
                }
            }

            var best = possible
                .Where(candidate => candidate.Score > sourceEvaluation.Score + 0.01m)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Target.Priority)
                .ThenBy(candidate => candidate.Target.Code)
                .Take(policy.MaxRecommendationsPerItem)
                .ToArray();
            if (best.Length == 0)
            {
                blocked++;
                continue;
            }

            foreach (var candidate in best)
            {
                var kind = candidate.Target.Type == LocationType.PickFace
                    ? SlottingRecommendationKind.PickFaceAssignment
                    : candidate.TargetSummary.ItemIds.Contains(item.Id)
                        ? SlottingRecommendationKind.Consolidation
                        : candidate.Target.Type == LocationType.Bulk
                            ? SlottingRecommendationKind.BulkLocation
                            : SlottingRecommendationKind.Relocation;
                var recommendationKey = BuildRecommendationKey(
                    policy,
                    item.Id,
                    source.LocationId,
                    candidate.Target.Id,
                    fromUtc,
                    asOfUtc);
                var factorSnapshotJson = JsonSerializer.Serialize(new
                {
                    source = sourceEvaluation.Factors,
                    target = candidate.Factors,
                    activity.PickQuantity,
                    activity.PickLineCount,
                    activity.ReplenishmentQuantity,
                    activity.ReplenishmentCount,
                    orderActivity.OrderCount,
                    orderActivity.OrderedQuantity,
                    scoreIsAnEstimate = true
                });
                var constraintSnapshotJson = JsonSerializer.Serialize(new
                {
                    source.Location.Code,
                    target = candidate.Target.Code,
                    allowedLocationType = policy.AllowedLocationTypes,
                    capacityBefore = candidate.TargetSummary.ToSnapshot(),
                    capacityAfter = candidate.CapacityAfter,
                    sourceAvailableQuantity = source.AvailableQuantity,
                    sourceReservedQuantity = source.ReservedQuantity,
                    openWorkConflict = false,
                    hardConstraintsPassed = true
                });
                var candidateModel = new RecommendationCandidate(
                    recommendationKey,
                    policy.WarehouseId,
                    policy.Warehouse,
                    item,
                    kind,
                    source,
                    candidate.Target,
                    source.AvailableQuantity,
                    source.BaseUnitOfMeasure,
                    candidate.Score,
                    sourceEvaluation.Score,
                    Math.Max(0m, candidate.Factors.TravelScore - sourceEvaluation.Factors.TravelScore),
                    Math.Max(0m, sourceEvaluation.Factors.ReplenishmentScore - candidate.Factors.ReplenishmentScore),
                    Math.Max(0m, candidate.Factors.SpaceScore - sourceEvaluation.Factors.SpaceScore),
                    fromUtc,
                    asOfUtc,
                    factorSnapshotJson,
                    constraintSnapshotJson,
                    runKey,
                    asOfUtc.AddDays(policy.RecommendationExpiryDays));
                candidates.Add(candidateModel);

                if (dryRun)
                {
                    continue;
                }

                if (existing.ContainsKey(recommendationKey))
                {
                    reused++;
                    continue;
                }

                var recommendation = new SlottingRecommendation(
                    candidateModel.RecommendationKey,
                    candidateModel.WarehouseId,
                    candidateModel.Item.Id,
                    candidateModel.Kind,
                    candidateModel.Source.LocationId,
                    candidateModel.Target.Id,
                    candidateModel.Quantity,
                    candidateModel.BaseUnitOfMeasure,
                    candidateModel.Score,
                    candidateModel.CurrentScore,
                    candidateModel.ExpectedTravelReduction,
                    candidateModel.ExpectedReplenishmentReduction,
                    candidateModel.ExpectedCongestionReduction,
                    candidateModel.SourcePeriodFromUtc,
                    candidateModel.SourcePeriodToUtc,
                    SourceDataVersion,
                    candidateModel.FactorSnapshotJson,
                    candidateModel.ConstraintSnapshotJson,
                    candidateModel.AnalysisRunKey,
                    candidateModel.ExpiresAtUtc);
                context.SlottingRecommendations.Add(recommendation);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SlottingRecommendationCreated,
                        WmsAuditEntityTypes.SlottingRecommendation,
                        candidateModel.RecommendationKey,
                        policy.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["itemId"] = item.Id,
                            ["sourceLocationId"] = source.LocationId,
                            ["targetLocationId"] = candidate.Target.Id,
                            ["score"] = candidate.Score,
                            ["sourceDataVersion"] = SourceDataVersion,
                            ["analysisRunKey"] = runKey
                        },
                        ActorUserId: actorUserId),
                    cancellationToken);
                created++;
            }
        }

        return new PolicyEvaluation(
            runKey,
            items.Length,
            created,
            reused,
            blocked,
            candidates.Select(candidate => MapDryRun(candidate)).ToArray());
    }

    private static CandidateEvaluation EvaluateTarget(
        Item item,
        InventoryBalance source,
        Location target,
        LocationSummary targetSummary,
        HashSet<(int ItemId, int LocationId)> openWorkKeys,
        ActivityMetric activity,
        OrderActivityMetric orderActivity,
        decimal maxVelocity,
        int maxOrderCount,
        int maxPriority,
        SlottingPolicy policy)
    {
        var reasons = new List<string>();
        if (openWorkKeys.Contains((item.Id, target.Id)))
        {
            reasons.Add("open-work-conflict");
        }

        if (item.StorageProfile is not null &&
            !string.Equals(item.StorageProfile, target.StorageProfile, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("storage-profile-mismatch");
        }

        if (item.IsHazardous && string.IsNullOrWhiteSpace(target.HazardClass))
        {
            reasons.Add("hazard-profile-missing");
        }

        if (item.TemperatureControlled &&
            (!target.MinimumTemperatureCelsius.HasValue || !target.MaximumTemperatureCelsius.HasValue ||
             (item.MinimumTemperatureCelsius.HasValue && target.MinimumTemperatureCelsius > item.MinimumTemperatureCelsius) ||
             (item.MaximumTemperatureCelsius.HasValue && target.MaximumTemperatureCelsius < item.MaximumTemperatureCelsius)))
        {
            reasons.Add("temperature-range-mismatch");
        }

        if (target.Type == LocationType.PickFace && !target.IsPickable)
        {
            reasons.Add("location-not-pickable");
        }

        if (!target.AllowMixedItems &&
            targetSummary.ItemIds.Any(itemId => itemId != item.Id))
        {
            reasons.Add("mixed-items-forbidden");
        }

        if (!target.AllowMixedLots &&
            targetSummary.GetLotIds(item.Id).Any(lotId => lotId != source.LotId))
        {
            reasons.Add("mixed-lots-forbidden");
        }

        var quantity = source.AvailableQuantity;
        var physical = PhysicalMetrics.For(item, quantity);
        var incoming = new LocationCapacitySnapshot(
            quantity,
            physical.WeightKg,
            physical.VolumeCubicMeters,
            0,
            source.LicensePlateId.HasValue ? 1 : 0);
        var violation = target.ValidateCapacity(targetSummary.ToSnapshot(), incoming);
        if (violation is not null)
        {
            reasons.Add(violation.Code);
        }

        var score = ScoreLocation(
            item,
            target,
            targetSummary,
            quantity,
            source.LicensePlateId.HasValue,
            activity,
            orderActivity,
            maxVelocity,
            maxOrderCount,
            maxPriority,
            policy);
        return new CandidateEvaluation(
            target,
            targetSummary,
            reasons.Count == 0,
            reasons,
            score.Score,
            score.Factors,
            targetSummary.ToSnapshot().Units + incoming.Units,
            targetSummary.ToSnapshot().WeightKg + incoming.WeightKg,
            targetSummary.ToSnapshot().VolumeCubicMeters + incoming.VolumeCubicMeters,
            targetSummary.ToSnapshot().Lpns + incoming.Lpns);
    }

    private static ScoreEvaluation ScoreLocation(
        Item item,
        Location location,
        LocationSummary summary,
        decimal incomingQuantity,
        bool addsLicensePlate,
        ActivityMetric activity,
        OrderActivityMetric orderActivity,
        decimal maxVelocity,
        int maxOrderCount,
        int maxPriority,
        SlottingPolicy policy)
    {
        var physical = PhysicalMetrics.For(item, incomingQuantity);
        var capacity = location.MaxUnits.HasValue && location.MaxUnits.Value > 0m
            ? Math.Clamp(
                (location.MaxUnits.Value - summary.Units - incomingQuantity) / location.MaxUnits.Value,
                0m,
                1m)
            : 0.5m;
        var travel = maxPriority <= 0
            ? 1m
            : Math.Clamp(1m - (decimal)location.Priority / maxPriority, 0m, 1m);
        var velocity = maxVelocity <= 0m
            ? 0m
            : Math.Clamp(activity.PickQuantity / maxVelocity, 0m, 1m);
        var replenishment = activity.PickQuantity <= 0m
            ? 0m
            : Math.Clamp(activity.ReplenishmentCount / (activity.PickLineCount + 1m), 0m, 1m);
        var affinity = summary.ItemIds.Contains(item.Id)
            ? 1m
            : maxOrderCount <= 0
                ? 0m
                : Math.Clamp(orderActivity.OrderCount / (decimal)maxOrderCount, 0m, 1m);
        var totalWeight = policy.VelocityWeight +
                          policy.TravelWeight +
                          policy.SpaceWeight +
                          policy.ReplenishmentWeight +
                          policy.AffinityWeight;
        var score = (
            velocity * policy.VelocityWeight +
            travel * policy.TravelWeight +
            capacity * policy.SpaceWeight +
            replenishment * policy.ReplenishmentWeight +
            affinity * policy.AffinityWeight) / totalWeight;
        _ = physical;
        _ = addsLicensePlate;
        return new ScoreEvaluation(
            Math.Round(score, 6, MidpointRounding.AwayFromZero),
            new SlottingFactors(
                velocity,
                travel,
                capacity,
                replenishment,
                affinity));
    }

    private static Dictionary<int, LocationSummary> BuildLocationSummaries(
        IReadOnlyList<InventoryBalance> balances)
    {
        var summaries = new Dictionary<int, LocationSummary>();
        foreach (var balance in balances)
        {
            if (!summaries.TryGetValue(balance.LocationId, out var summary))
            {
                summary = new LocationSummary();
                summaries.Add(balance.LocationId, summary);
            }

            var physical = PhysicalMetrics.For(balance.Item, balance.OnHandQuantity);
            summary.Units += balance.OnHandQuantity;
            summary.WeightKg += physical.WeightKg;
            summary.VolumeCubicMeters += physical.VolumeCubicMeters;
            if (balance.LicensePlateId.HasValue)
            {
                summary.LpnIds.Add(balance.LicensePlateId.Value);
            }

            summary.ItemIds.Add(balance.ItemId);
            if (!summary.LotIdsByItem.TryGetValue(balance.ItemId, out var lotIds))
            {
                lotIds = [];
                summary.LotIdsByItem.Add(balance.ItemId, lotIds);
            }

            lotIds.Add(balance.LotId);
        }

        return summaries;
    }

    private async Task<IReadOnlyList<SlottingRecommendationDto>> LoadRunRecommendationsAsync(
        IReadOnlyCollection<string> runKeys,
        CancellationToken cancellationToken)
    {
        if (runKeys.Count == 0)
        {
            return [];
        }

        var rows = await context.SlottingRecommendations
            .AsNoTracking()
            .Include(recommendation => recommendation.Warehouse)
            .Include(recommendation => recommendation.Item)
            .Include(recommendation => recommendation.SourceLocation)
            .Include(recommendation => recommendation.TargetLocation)
            .Where(recommendation => runKeys.Contains(recommendation.AnalysisRunKey))
            .OrderByDescending(recommendation => recommendation.Score)
            .ThenBy(recommendation => recommendation.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    private static string BuildRunKey(SlottingPolicy policy, DateTime asOfUtc) =>
        $"slotting:{policy.Id}:{policy.Revision}:{asOfUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

    private static string BuildRecommendationKey(
        SlottingPolicy policy,
        int itemId,
        int sourceLocationId,
        int targetLocationId,
        DateTime fromUtc,
        DateTime toUtc) =>
        $"slotting:{policy.Id}:{policy.Revision}:{itemId}:{sourceLocationId}:{targetLocationId}:" +
        $"{fromUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{toUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

    private static SlottingPolicyDto MapPolicy(SlottingPolicy policy, Warehouse warehouse) => new(
        policy.Id,
        policy.WarehouseId,
        warehouse.Code,
        policy.PolicyKey,
        policy.Name,
        policy.LookbackDays,
        policy.VelocityWeight,
        policy.TravelWeight,
        policy.SpaceWeight,
        policy.ReplenishmentWeight,
        policy.AffinityWeight,
        policy.MaxRecommendationsPerItem,
        policy.RecommendationExpiryDays,
        policy.AllowedLocationTypes,
        policy.EffectiveFromUtc,
        policy.EffectiveToUtc,
        policy.IsActive,
        policy.Revision);

    private static SlottingRecommendationDto Map(SlottingRecommendation recommendation) => new(
        recommendation.Id,
        recommendation.RecommendationKey,
        recommendation.WarehouseId,
        recommendation.Warehouse.Code,
        recommendation.ItemId,
        recommendation.Item.Sku,
        recommendation.Item.Name,
        recommendation.Kind,
        recommendation.SourceLocationId,
        recommendation.SourceLocation.Code,
        recommendation.TargetLocationId,
        recommendation.TargetLocation.Code,
        recommendation.Quantity,
        recommendation.BaseUnitOfMeasure,
        recommendation.Score,
        recommendation.CurrentScore,
        recommendation.ExpectedTravelReduction,
        recommendation.ExpectedReplenishmentReduction,
        recommendation.ExpectedCongestionReduction,
        recommendation.SourcePeriodFromUtc,
        recommendation.SourcePeriodToUtc,
        recommendation.SourceDataVersion,
        recommendation.FactorSnapshotJson,
        recommendation.ConstraintSnapshotJson,
        recommendation.AnalysisRunKey,
        recommendation.ExpiresAtUtc,
        recommendation.Status,
        recommendation.ApprovedByUserId,
        recommendation.ApprovedAtUtc,
        recommendation.RejectedByUserId,
        recommendation.RejectedAtUtc,
        recommendation.RejectionReason,
        recommendation.WorkId,
        recommendation.WorkCreatedAtUtc,
        recommendation.Revision);

    private static SlottingRecommendationDto MapDryRun(RecommendationCandidate candidate) => new(
        0,
        candidate.RecommendationKey,
        candidate.WarehouseId,
        candidate.Warehouse.Code,
        candidate.Item.Id,
        candidate.Item.Sku,
        candidate.Item.Name,
        candidate.Kind,
        candidate.Source.LocationId,
        candidate.Source.Location.Code,
        candidate.Target.Id,
        candidate.Target.Code,
        candidate.Quantity,
        candidate.BaseUnitOfMeasure,
        candidate.Score,
        candidate.CurrentScore,
        candidate.ExpectedTravelReduction,
        candidate.ExpectedReplenishmentReduction,
        candidate.ExpectedCongestionReduction,
        candidate.SourcePeriodFromUtc,
        candidate.SourcePeriodToUtc,
        SourceDataVersion,
        candidate.FactorSnapshotJson,
        candidate.ConstraintSnapshotJson,
        candidate.AnalysisRunKey,
        candidate.ExpiresAtUtc,
        SlottingRecommendationStatus.Pending,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        0);

    private static IQueryable<SlottingPolicy> ApplyPolicyScope(
        IQueryable<SlottingPolicy> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(policy => scope.WarehouseIds.Contains(policy.WarehouseId));

    private static IQueryable<SlottingRecommendation> ApplyRecommendationScope(
        IQueryable<SlottingRecommendation> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(recommendation => scope.WarehouseIds.Contains(recommendation.WarehouseId));

    private static int NormalizeLimit(int limit, int maximum) =>
        limit < 1 ? 1 : Math.Min(limit, maximum);

    private static DateTime NormalizeUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record TransactionMetric(int ItemId, InventoryTransactionType Type, decimal QuantityDelta);

    private sealed record OrderMetric(int ItemId, decimal OrderedBaseQuantity);

    private sealed record OpenWorkMetric(int ItemId, int? SourceLocationId, int? DestinationLocationId);

    private sealed record ActivityMetric(
        decimal PickQuantity,
        int PickLineCount,
        decimal ReplenishmentQuantity,
        int ReplenishmentCount)
    {
        public static ActivityMetric Empty { get; } = new(0m, 0, 0m, 0);
    }

    private sealed record OrderActivityMetric(int OrderCount, decimal OrderedQuantity)
    {
        public static OrderActivityMetric Empty { get; } = new(0, 0m);
    }

    private sealed record SlottingFactors(
        decimal VelocityScore,
        decimal TravelScore,
        decimal SpaceScore,
        decimal ReplenishmentScore,
        decimal AffinityScore);

    private sealed record ScoreEvaluation(decimal Score, SlottingFactors Factors);

    private sealed record CandidateEvaluation(
        Location Target,
        LocationSummary TargetSummary,
        bool IsAllowed,
        IReadOnlyList<string> RejectionReasons,
        decimal Score,
        SlottingFactors Factors,
        decimal CapacityAfterUnits,
        decimal CapacityAfterWeightKg,
        decimal CapacityAfterVolumeCubicMeters,
        int CapacityAfterLpns)
    {
        public LocationCapacitySnapshot CapacityAfter => new(
            CapacityAfterUnits,
            CapacityAfterWeightKg,
            CapacityAfterVolumeCubicMeters,
            0,
            CapacityAfterLpns);
    }

    private sealed record RecommendationCandidate(
        string RecommendationKey,
        int WarehouseId,
        Warehouse Warehouse,
        Item Item,
        SlottingRecommendationKind Kind,
        InventoryBalance Source,
        Location Target,
        decimal Quantity,
        string BaseUnitOfMeasure,
        decimal Score,
        decimal CurrentScore,
        decimal ExpectedTravelReduction,
        decimal ExpectedReplenishmentReduction,
        decimal ExpectedCongestionReduction,
        DateTime SourcePeriodFromUtc,
        DateTime SourcePeriodToUtc,
        string FactorSnapshotJson,
        string ConstraintSnapshotJson,
        string AnalysisRunKey,
        DateTime ExpiresAtUtc);

    private sealed record PolicyEvaluation(
        string RunKey,
        int ItemsExamined,
        int RecommendationsCreated,
        int RecommendationsReused,
        int Blocked,
        IReadOnlyList<SlottingRecommendationDto> DryRunRecommendations);

    private sealed class AnalysisState(bool dryRun)
    {
        private readonly List<SlottingRecommendationDto> _dryRunRecommendations = [];
        private readonly List<string> _runKeys = [];

        public bool DryRun { get; } = dryRun;
        public int PoliciesExamined { get; private set; }
        public int ItemsExamined { get; private set; }
        public int RecommendationsCreated { get; private set; }
        public int RecommendationsReused { get; private set; }
        public int Blocked { get; private set; }
        public IReadOnlyList<SlottingRecommendationDto> DryRunRecommendations => _dryRunRecommendations;
        public IReadOnlyList<string> RunKeys => _runKeys;

        public void Add(SlottingPolicy policy, PolicyEvaluation evaluation)
        {
            PoliciesExamined++;
            ItemsExamined += evaluation.ItemsExamined;
            RecommendationsCreated += evaluation.RecommendationsCreated;
            RecommendationsReused += evaluation.RecommendationsReused;
            Blocked += evaluation.Blocked;
            _runKeys.Add(evaluation.RunKey);
            if (DryRun)
            {
                _dryRunRecommendations.AddRange(evaluation.DryRunRecommendations);
            }
        }
    }

    private sealed class LocationSummary
    {
        public static LocationSummary Empty => new();

        public decimal Units { get; set; }
        public decimal WeightKg { get; set; }
        public decimal VolumeCubicMeters { get; set; }
        public HashSet<int> LpnIds { get; } = [];
        public HashSet<int> ItemIds { get; } = [];
        public Dictionary<int, HashSet<int?>> LotIdsByItem { get; } = [];

        public LocationCapacitySnapshot ToSnapshot() => new(
            Units,
            WeightKg,
            VolumeCubicMeters,
            0,
            LpnIds.Count);

        public HashSet<int?> GetLotIds(int itemId) =>
            LotIdsByItem.TryGetValue(itemId, out var lotIds) ? lotIds : [];
    }

    private readonly record struct PhysicalMetrics(decimal WeightKg, decimal VolumeCubicMeters)
    {
        public static PhysicalMetrics For(Item item, decimal quantity)
        {
            var packaging = item.Packagings
                .Where(value => value.IsActive)
                .OrderByDescending(value => value.IsDefaultStorage || value.IsDefault)
                .ThenBy(value => value.UnitsPerPackage)
                .FirstOrDefault();
            var weightPerUnit = item.NetWeightKg ??
                (packaging?.GrossWeightKg.HasValue == true
                    ? packaging.GrossWeightKg.Value / packaging.UnitsPerPackage
                    : 0m);
            var volumePerUnit = item.VolumeCubicMeters ??
                (packaging?.VolumeCubicMeters.HasValue == true
                    ? packaging.VolumeCubicMeters.Value / packaging.UnitsPerPackage
                    : 0m);
            return new PhysicalMetrics(
                Math.Max(0m, quantity) * weightPerUnit,
                Math.Max(0m, quantity) * volumePerUnit);
        }
    }
}
