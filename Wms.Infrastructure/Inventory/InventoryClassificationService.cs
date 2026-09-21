using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Calculates transparent ABC classes from warehouse-local movement, shipment,
/// inventory-value, or operational-criticality inputs. The service keeps the
/// active result separate from append-only history and never mutates stock.
/// </summary>
public sealed class InventoryClassificationService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InventoryClassificationService> logger)
    : IInventoryClassificationService
{
    private const string CalculationInputVersion = "abc-v1";
    private const int MaximumPolicyLimit = 2_000;
    private const int MaximumQueryLimit = 2_000;

    public async Task<Result<InventoryClassificationPolicyDto>> SavePolicyAsync(
        int? policyId,
        InventoryClassificationPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.Validation(
                "inventory.classification_actor_required",
                "An authenticated actor is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryClassificationPolicyDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            var policyKey = input.PolicyKey.Trim().ToUpperInvariant();
            var effectiveFromUtc = NormalizeUtc(input.EffectiveFromUtc);
            DateTime? effectiveToUtc = input.EffectiveToUtc.HasValue
                ? NormalizeUtc(input.EffectiveToUtc.Value)
                : null;
            var duplicate = await context.InventoryClassificationPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.PolicyKey == policyKey,
                    cancellationToken);
            if (duplicate)
            {
                return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.Conflict(
                    "inventory.classification_policy_key_conflict",
                    "A classification policy with this key already exists in the warehouse."));
            }

            var overlap = await context.InventoryClassificationPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.IsActive &&
                    policy.EffectiveFromUtc < (effectiveToUtc ?? DateTime.MaxValue) &&
                    (!policy.EffectiveToUtc.HasValue ||
                     policy.EffectiveToUtc.Value > effectiveFromUtc),
                    cancellationToken);
            if (overlap)
            {
                return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.Conflict(
                    "inventory.classification_policy_effective_overlap",
                    "Another active classification policy overlaps the requested effective period."));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);
            InventoryClassificationPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.InventoryClassificationPolicies
                    .SingleOrDefaultAsync(candidate => candidate.Id == policyId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException();
                if (policy.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.Conflict(
                        "inventory.classification_policy_identity_immutable",
                        "A classification policy cannot be moved to another warehouse."));
                }

                policy.Update(
                    policyKey,
                    input.Method,
                    input.LookbackDays,
                    input.AThresholdPercent,
                    input.BThresholdPercent,
                    input.MinimumActivityValue,
                    effectiveFromUtc,
                    effectiveToUtc);
            }
            else
            {
                policy = new InventoryClassificationPolicy(
                    input.WarehouseId,
                    policyKey,
                    input.Method,
                    input.LookbackDays,
                    input.AThresholdPercent,
                    input.BThresholdPercent,
                    input.MinimumActivityValue,
                    effectiveFromUtc,
                    effectiveToUtc);
                await context.InventoryClassificationPolicies.AddAsync(policy, cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryClassificationPolicyChanged,
                    WmsAuditEntityTypes.InventoryClassificationPolicy,
                    policyId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyKey"] = policyKey,
                        ["method"] = input.Method.ToString(),
                        ["lookbackDays"] = input.LookbackDays,
                        ["aThresholdPercent"] = input.AThresholdPercent,
                        ["bThresholdPercent"] = input.BThresholdPercent,
                        ["minimumActivityValue"] = input.MinimumActivityValue,
                        ["effectiveFromUtc"] = effectiveFromUtc,
                        ["effectiveToUtc"] = effectiveToUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapPolicy(policy, warehouse));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.NotFound(
                "inventory.classification_policy_not_found",
                "The requested classification policy was not found."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.Validation(
                "inventory.classification_policy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification policy save failed");
            return Result.Failure<InventoryClassificationPolicyDto>(WmsErrors.FromException(
                exception,
                "inventory.classification_policy_save_failed",
                "The inventory classification policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryClassificationPolicyDto>>> SearchPoliciesAsync(
        InventoryClassificationPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryClassificationPolicyDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = ApplyPolicyScope(
                    context.InventoryClassificationPolicies.AsNoTracking(),
                    scope)
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
            return Result.Success<IReadOnlyList<InventoryClassificationPolicyDto>>(
                rows.Select(policy => MapPolicy(policy, policy.Warehouse)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification policy search failed");
            return Result.Failure<IReadOnlyList<InventoryClassificationPolicyDto>>(
                WmsErrors.FromException(
                    exception,
                    "inventory.classification_policy_search_failed",
                    "Inventory classification policies could not be loaded."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryClassificationDto>>> SearchAsync(
        InventoryClassificationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryClassificationDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var classifications = ApplyClassificationScope(
                    context.InventoryClassifications.AsNoTracking(),
                    scope)
                .Include(classification => classification.Warehouse)
                .Include(classification => classification.Item)
                .AsQueryable();
            if (query.WarehouseId.HasValue)
            {
                classifications = classifications.Where(
                    classification => classification.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                classifications = classifications.Where(
                    classification => classification.ItemId == query.ItemId.Value);
            }

            if (query.Classification.HasValue)
            {
                classifications = classifications.Where(
                    classification => classification.Classification == query.Classification.Value);
            }

            var rows = await classifications
                .OrderBy(classification => classification.Warehouse.Code)
                .ThenBy(classification => classification.Item.Sku)
                .Take(NormalizeLimit(query.Limit, MaximumQueryLimit))
                .ToListAsync(cancellationToken);
            return Result.Success<IReadOnlyList<InventoryClassificationDto>>(
                rows.Select(classification => MapClassification(
                    classification,
                    classification.Item,
                    classification.Warehouse)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification search failed");
            return Result.Failure<IReadOnlyList<InventoryClassificationDto>>(
                WmsErrors.FromException(
                    exception,
                    "inventory.classification_search_failed",
                    "Inventory classifications could not be loaded."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryClassificationHistoryDto>>> SearchHistoryAsync(
        InventoryClassificationHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryClassificationHistoryDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var history = ApplyHistoryScope(
                    context.InventoryClassificationHistories.AsNoTracking(),
                    scope)
                .Include(entry => entry.Warehouse)
                .Include(entry => entry.Item)
                .AsQueryable();
            if (query.WarehouseId.HasValue)
            {
                history = history.Where(entry => entry.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                history = history.Where(entry => entry.ItemId == query.ItemId.Value);
            }

            var rows = await history
                .OrderByDescending(entry => entry.ChangedAtUtc)
                .ThenBy(entry => entry.WarehouseId)
                .ThenBy(entry => entry.ItemId)
                .Take(NormalizeLimit(query.Limit, MaximumQueryLimit))
                .ToListAsync(cancellationToken);
            return Result.Success<IReadOnlyList<InventoryClassificationHistoryDto>>(
                rows.Select(MapHistory).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification history search failed");
            return Result.Failure<IReadOnlyList<InventoryClassificationHistoryDto>>(
                WmsErrors.FromException(
                    exception,
                    "inventory.classification_history_search_failed",
                    "Inventory classification history could not be loaded."));
        }
    }

    public async Task<Result<InventoryClassificationRecalculationResultDto>> RecalculateAsync(
        InventoryClassificationRecalculationQuery query,
        string actorUserId,
        bool internalExecution = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryClassificationRecalculationResultDto>(WmsErrors.Validation(
                "inventory.classification_actor_required",
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
                return authorization.ToFailure<InventoryClassificationRecalculationResultDto>();
            }
        }

        try
        {
            var asOfUtc = NormalizeUtc(query.AsOfUtc ?? clock.UtcNow.UtcDateTime);
            var scope = internalExecution
                ? new WarehouseAccessScope(true, new HashSet<int>())
                : await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = ApplyPolicyScope(
                    context.InventoryClassificationPolicies.AsNoTracking(),
                    scope)
                .Where(policy =>
                    policy.IsActive &&
                    policy.EffectiveFromUtc <= asOfUtc &&
                    (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > asOfUtc));
            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.PolicyId.HasValue)
            {
                policies = policies.Where(policy => policy.Id == query.PolicyId.Value);
            }

            var policyRows = await policies
                .OrderBy(policy => policy.WarehouseId)
                .ThenBy(policy => policy.Id)
                .ToListAsync(cancellationToken);
            if (query.PolicyId.HasValue && policyRows.Count == 0)
            {
                return Result.Failure<InventoryClassificationRecalculationResultDto>(WmsErrors.NotFound(
                    "inventory.classification_policy_not_found",
                    "No effective classification policy matched the request."));
            }

            var state = new RecalculationState(query.DryRun);
            if (!query.DryRun)
            {
                await using var transaction = await context.Database.BeginTransactionAsync(
                    cancellationToken);
                foreach (var policy in policyRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var evaluation = await EvaluatePolicyAsync(
                        policy,
                        asOfUtc,
                        NormalizeLimit(query.Limit, MaximumQueryLimit),
                        query.DryRun,
                        cancellationToken);
                    state.Add(policy, evaluation);
                    await auditWriter.RecordAsync(
                        new AuditRecord(
                            WmsAuditActions.InventoryClassificationRecalculated,
                            WmsAuditEntityTypes.InventoryClassification,
                            evaluation.RunKey,
                            policy.WarehouseId,
                            After: new Dictionary<string, object?>
                            {
                                ["policyId"] = policy.Id,
                                ["method"] = policy.Method.ToString(),
                                ["asOfUtc"] = asOfUtc,
                                ["itemsExamined"] = evaluation.ItemsExamined,
                                ["classificationsChanged"] = evaluation.ClassificationsChanged,
                                ["manualOverridesSkipped"] = evaluation.ManualOverridesSkipped
                            },
                            ActorUserId: actorUserId),
                        cancellationToken);
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                foreach (var policy in policyRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var evaluation = await EvaluatePolicyAsync(
                        policy,
                        asOfUtc,
                        NormalizeLimit(query.Limit, MaximumQueryLimit),
                        query.DryRun,
                        cancellationToken);
                    state.Add(policy, evaluation);
                }
            }

            return Result.Success(state.ToDto());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryClassificationRecalculationResultDto>(WmsErrors.Validation(
                "inventory.classification_recalculation_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification recalculation failed");
            return Result.Failure<InventoryClassificationRecalculationResultDto>(WmsErrors.FromException(
                exception,
                "inventory.classification_recalculation_failed",
                "Inventory classifications could not be recalculated."));
        }
    }

    public async Task<Result<InventoryClassificationDto>> OverrideAsync(
        InventoryClassificationOverrideInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryClassificationDto>(WmsErrors.Validation(
                "inventory.classification_actor_required",
                "An authenticated actor is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryClassificationDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            var item = await context.Items
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.ItemId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryClassificationDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            if (item is null)
            {
                return Result.Failure<InventoryClassificationDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The requested active item was not found."));
            }

            var changedAtUtc = NormalizeUtc(clock.UtcNow.UtcDateTime);
            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);
            var classification = await context.InventoryClassifications
                .SingleOrDefaultAsync(
                    candidate => candidate.WarehouseId == input.WarehouseId &&
                        candidate.ItemId == input.ItemId,
                    cancellationToken);
            var previousClassification = classification?.Classification;
            if (classification is null)
            {
                var policy = await context.InventoryClassificationPolicies
                    .Where(candidate =>
                        candidate.WarehouseId == input.WarehouseId &&
                        candidate.IsActive &&
                        candidate.EffectiveFromUtc <= changedAtUtc &&
                        (!candidate.EffectiveToUtc.HasValue || candidate.EffectiveToUtc.Value > changedAtUtc))
                    .OrderByDescending(candidate => candidate.EffectiveFromUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                var runKey = BuildManualRunKey(input.WarehouseId, input.ItemId, changedAtUtc);
                classification = new InventoryClassification(
                    input.WarehouseId,
                    input.ItemId,
                    InventoryClassificationClass.Unclassified,
                    InventoryClassificationSource.Automatic,
                    0m,
                    0m,
                    0m,
                    0,
                    0m,
                    0m,
                    0m,
                    changedAtUtc,
                    changedAtUtc,
                    policy?.Id,
                    policy?.Revision ?? 0,
                    CalculationInputVersion,
                    runKey,
                    changedAtUtc);
                await context.InventoryClassifications.AddAsync(classification, cancellationToken);
            }

            var overrideRunKey = BuildManualRunKey(input.WarehouseId, input.ItemId, changedAtUtc);
            classification.ApplyManualOverride(
                input.Classification,
                input.Reason,
                input.ExpiresAtUtc,
                overrideRunKey,
                changedAtUtc);
            await context.InventoryClassificationHistories.AddAsync(
                new InventoryClassificationHistory(
                    input.WarehouseId,
                    input.ItemId,
                    previousClassification,
                    input.Classification,
                    InventoryClassificationSource.ManualOverride,
                    classification.MetricValue,
                    classification.CumulativePercent,
                    classification.ShippedQuantity,
                    classification.ShippedLineCount,
                    classification.MovementQuantity,
                    classification.InventoryValue,
                    classification.CriticalityScore,
                    classification.LookbackFromUtc,
                    classification.LookbackToUtc,
                    classification.PolicyId,
                    classification.PolicyRevision,
                    classification.CalculationInputVersion,
                    overrideRunKey,
                    input.Reason,
                    changedAtUtc,
                    input.ExpiresAtUtc),
                cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryClassificationOverrideChanged,
                    WmsAuditEntityTypes.InventoryClassification,
                    $"{input.WarehouseId}:{input.ItemId}",
                    input.WarehouseId,
                    Before: previousClassification.HasValue
                        ? new Dictionary<string, object?>
                        {
                            ["classification"] = previousClassification.Value.ToString()
                        }
                        : null,
                    After: new Dictionary<string, object?>
                    {
                        ["classification"] = input.Classification.ToString(),
                        ["reason"] = input.Reason,
                        ["expiresAtUtc"] = input.ExpiresAtUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapClassification(classification, item, warehouse));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryClassificationDto>(WmsErrors.Validation(
                "inventory.classification_override_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory classification override failed");
            return Result.Failure<InventoryClassificationDto>(WmsErrors.FromException(
                exception,
                "inventory.classification_override_failed",
                "The inventory classification override could not be saved."));
        }
    }

    private async Task<PolicyEvaluation> EvaluatePolicyAsync(
        InventoryClassificationPolicy policy,
        DateTime asOfUtc,
        int limit,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var lookbackFromUtc = asOfUtc.AddDays(-policy.LookbackDays);
        var items = await context.Items
            .AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var movementRows = itemIds.Length == 0
            ? []
            : await context.Movements
                .AsNoTracking()
                .Where(movement =>
                    itemIds.Contains(movement.ItemId) &&
                    movement.Timestamp >= lookbackFromUtc &&
                    movement.Timestamp < asOfUtc &&
                    (movement.Type == MovementType.Pick ||
                     movement.Type == MovementType.Ship ||
                     movement.Type == MovementType.Transfer) &&
                    movement.FromLocation != null &&
                    movement.FromLocation.WarehouseId == policy.WarehouseId)
                .Select(movement => new MovementMetricRow(
                    movement.ItemId,
                    movement.Type,
                    movement.Quantity.Value))
                .ToListAsync(cancellationToken);
        var balanceRows = itemIds.Length == 0
            ? []
            : await context.InventoryBalances
                .AsNoTracking()
                .Where(balance =>
                    balance.WarehouseId == policy.WarehouseId &&
                    itemIds.Contains(balance.ItemId))
                .Select(balance => new BalanceMetricRow(
                    balance.ItemId,
                    balance.OnHandQuantity,
                    balance.Item.StandardCost ?? 0m))
                .ToListAsync(cancellationToken);
        var currentRows = await context.InventoryClassifications
            .Where(classification =>
                classification.WarehouseId == policy.WarehouseId &&
                itemIds.Contains(classification.ItemId))
            .ToDictionaryAsync(classification => classification.ItemId, cancellationToken);

        var metrics = items
            .Select(item => BuildMetricSnapshot(
                item,
                movementRows.Where(row => row.ItemId == item.Id).ToArray(),
                balanceRows.Where(row => row.ItemId == item.Id).ToArray(),
                policy))
            .ToArray();
        var totalMetric = metrics.Sum(metric => metric.MetricValue);
        var ranked = metrics
            .OrderByDescending(metric => metric.MetricValue)
            .ThenBy(metric => metric.ItemId)
            .ToArray();
        var comparisons = new List<InventoryClassificationComparisonDto>(ranked.Length);
        var runKey = BuildRunKey(policy, asOfUtc);
        var cumulativeMetric = 0m;
        var classificationsChanged = 0;
        var manualOverridesSkipped = 0;
        foreach (var metric in ranked)
        {
            var previousCumulativePercent = totalMetric <= 0m
                ? 0m
                : Math.Round(cumulativeMetric / totalMetric * 100m, 4, MidpointRounding.AwayFromZero);
            cumulativeMetric += metric.MetricValue;
            var cumulativePercent = totalMetric <= 0m
                ? 0m
                : Math.Round(cumulativeMetric / totalMetric * 100m, 4, MidpointRounding.AwayFromZero);
            var proposedClassification = DetermineClassification(
                metric.MetricValue,
                previousCumulativePercent,
                cumulativePercent,
                totalMetric,
                policy);
            currentRows.TryGetValue(metric.ItemId, out var current);
            var manualOverrideSkipped = current?.HasActiveManualOverride(asOfUtc) == true;
            if (manualOverrideSkipped)
            {
                manualOverridesSkipped++;
            }

            var materiallyDifferent = current is null ||
                current.Source != InventoryClassificationSource.Automatic ||
                current.Classification != proposedClassification ||
                current.MetricValue != metric.MetricValue ||
                current.CumulativePercent != cumulativePercent ||
                current.ShippedQuantity != metric.ShippedQuantity ||
                current.ShippedLineCount != metric.ShippedLineCount ||
                current.MovementQuantity != metric.MovementQuantity ||
                current.InventoryValue != metric.InventoryValue ||
                current.CriticalityScore != metric.CriticalityScore ||
                current.PolicyId != policy.Id ||
                current.PolicyRevision != policy.Revision;
            if (!manualOverrideSkipped && materiallyDifferent)
            {
                classificationsChanged++;
            }

            comparisons.Add(new InventoryClassificationComparisonDto(
                policy.WarehouseId,
                metric.ItemId,
                metric.ItemSku,
                current?.Classification.ToString() ?? "None",
                proposedClassification.ToString(),
                metric.MetricValue,
                cumulativePercent,
                manualOverrideSkipped,
                current?.ManualOverrideReason));

            if (dryRun || manualOverrideSkipped || !materiallyDifferent && current?.CalculationRunKey == runKey)
            {
                continue;
            }

            var previousClassification = current?.Classification;
            var previousSource = current?.Source;
            var historyRequired = materiallyDifferent;
            if (current is null)
            {
                current = new InventoryClassification(
                    policy.WarehouseId,
                    metric.ItemId,
                    proposedClassification,
                    InventoryClassificationSource.Automatic,
                    metric.MetricValue,
                    cumulativePercent,
                    metric.ShippedQuantity,
                    metric.ShippedLineCount,
                    metric.MovementQuantity,
                    metric.InventoryValue,
                    metric.CriticalityScore,
                    lookbackFromUtc,
                    asOfUtc,
                    policy.Id,
                    policy.Revision,
                    CalculationInputVersion,
                    runKey,
                    asOfUtc);
                await context.InventoryClassifications.AddAsync(current, cancellationToken);
                currentRows[metric.ItemId] = current;
            }
            else
            {
                current.ApplyAutomatic(
                    proposedClassification,
                    metric.MetricValue,
                    cumulativePercent,
                    metric.ShippedQuantity,
                    metric.ShippedLineCount,
                    metric.MovementQuantity,
                    metric.InventoryValue,
                    metric.CriticalityScore,
                    lookbackFromUtc,
                    asOfUtc,
                    policy.Id,
                    policy.Revision,
                    CalculationInputVersion,
                    runKey,
                    asOfUtc);
            }

            if (historyRequired)
            {
                var reason = previousSource == InventoryClassificationSource.ManualOverride
                    ? "Manual override expired; automatic recalculation restored the policy result."
                    : previousClassification is null
                        ? $"Initial classification from {policy.Method} using a {policy.LookbackDays}-day window."
                        : previousClassification != proposedClassification
                            ? $"Automatic recalculation changed the class from {previousClassification} to {proposedClassification}."
                            : "Automatic recalculation refreshed the classification input metrics.";
                await context.InventoryClassificationHistories.AddAsync(
                    new InventoryClassificationHistory(
                        policy.WarehouseId,
                        metric.ItemId,
                        previousClassification,
                        proposedClassification,
                        InventoryClassificationSource.Automatic,
                        metric.MetricValue,
                        cumulativePercent,
                        metric.ShippedQuantity,
                        metric.ShippedLineCount,
                        metric.MovementQuantity,
                        metric.InventoryValue,
                        metric.CriticalityScore,
                        lookbackFromUtc,
                        asOfUtc,
                        policy.Id,
                        policy.Revision,
                        CalculationInputVersion,
                        runKey,
                        reason,
                        asOfUtc),
                    cancellationToken);
            }
        }

        return new PolicyEvaluation(
            runKey,
            items.Count,
            classificationsChanged,
            manualOverridesSkipped,
            comparisons);
    }

    private static MetricSnapshot BuildMetricSnapshot(
        Item item,
        IReadOnlyList<MovementMetricRow> movements,
        IReadOnlyList<BalanceMetricRow> balances,
        InventoryClassificationPolicy policy)
    {
        var shippedQuantity = movements
            .Where(row => row.Type == MovementType.Ship)
            .Sum(row => Math.Abs(row.Quantity));
        var shippedLineCount = movements.Count(row => row.Type == MovementType.Ship);
        var movementQuantity = movements.Sum(row => Math.Abs(row.Quantity));
        var inventoryValue = balances.Sum(row =>
            Math.Max(0m, row.OnHandQuantity) * Math.Max(0m, row.StandardCost));
        var criticalityScore = CalculateCriticalityScore(item);
        var metricValue = policy.Method switch
        {
            InventoryClassificationMethod.ShippedQuantity => shippedQuantity,
            InventoryClassificationMethod.ShippedLines => shippedLineCount,
            InventoryClassificationMethod.MovementVelocity =>
                movementQuantity / Math.Max(1, policy.LookbackDays),
            InventoryClassificationMethod.InventoryValue => inventoryValue,
            InventoryClassificationMethod.Criticality => criticalityScore,
            _ => 0m
        };

        return new MetricSnapshot(
            item.Id,
            item.Sku,
            Math.Max(0m, metricValue),
            shippedQuantity,
            shippedLineCount,
            movementQuantity,
            inventoryValue,
            criticalityScore);
    }

    private static decimal CalculateCriticalityScore(Item item)
    {
        var score = 0m;
        if (item.IsHazardous)
        {
            score += 4m;
        }

        if (item.TemperatureControlled)
        {
            score += 3m;
        }

        if (item.QualityInspectionRequired)
        {
            score += 2m;
        }

        if (item.SpecialHandlingRequired)
        {
            score += 1m;
        }

        if (item.StandardCost is > 0m && item.SalesPrice is > 0m)
        {
            score += Math.Min(
                2m,
                Math.Max(0m, item.SalesPrice.Value - item.StandardCost.Value) /
                Math.Max(1m, item.SalesPrice.Value) * 2m);
        }

        return score;
    }

    private static InventoryClassificationClass DetermineClassification(
        decimal metricValue,
        decimal previousCumulativePercent,
        decimal cumulativePercent,
        decimal totalMetric,
        InventoryClassificationPolicy policy) =>
        totalMetric <= 0m || metricValue <= policy.MinimumActivityValue
            ? InventoryClassificationClass.Unclassified
            : cumulativePercent <= policy.AThresholdPercent ||
              previousCumulativePercent < policy.AThresholdPercent
                ? InventoryClassificationClass.A
                : cumulativePercent <= policy.BThresholdPercent ||
                  previousCumulativePercent < policy.BThresholdPercent
                    ? InventoryClassificationClass.B
                    : InventoryClassificationClass.C;

    private static InventoryClassificationPolicyDto MapPolicy(
        InventoryClassificationPolicy policy,
        Warehouse warehouse) =>
        new(
            policy.Id,
            policy.PolicyKey,
            policy.WarehouseId,
            warehouse.Code,
            policy.Method,
            policy.LookbackDays,
            policy.AThresholdPercent,
            policy.BThresholdPercent,
            policy.MinimumActivityValue,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private static InventoryClassificationDto MapClassification(
        InventoryClassification classification,
        Item item,
        Warehouse warehouse) =>
        new(
            classification.Id,
            classification.WarehouseId,
            warehouse.Code,
            classification.ItemId,
            item.Sku,
            item.Name,
            classification.Classification,
            classification.Source,
            classification.MetricValue,
            classification.CumulativePercent,
            classification.ShippedQuantity,
            classification.ShippedLineCount,
            classification.MovementQuantity,
            classification.InventoryValue,
            classification.CriticalityScore,
            classification.LookbackFromUtc,
            classification.LookbackToUtc,
            classification.PolicyId,
            classification.PolicyRevision,
            classification.CalculationInputVersion,
            classification.CalculatedAtUtc,
            classification.ManualOverrideReason,
            classification.ManualOverrideExpiresAtUtc,
            classification.Revision);

    private static InventoryClassificationHistoryDto MapHistory(
        InventoryClassificationHistory history) =>
        new(
            history.Id,
            history.WarehouseId,
            history.Warehouse.Code,
            history.ItemId,
            history.Item.Sku,
            history.Item.Name,
            history.PreviousClassification,
            history.Classification,
            history.Source,
            history.MetricValue,
            history.CumulativePercent,
            history.ShippedQuantity,
            history.ShippedLineCount,
            history.MovementQuantity,
            history.InventoryValue,
            history.CriticalityScore,
            history.LookbackFromUtc,
            history.LookbackToUtc,
            history.PolicyId,
            history.PolicyRevision,
            history.CalculationInputVersion,
            history.CalculationRunKey,
            history.Reason,
            history.ChangedAtUtc,
            history.ManualOverrideExpiresAtUtc);

    private static IQueryable<InventoryClassificationPolicy> ApplyPolicyScope(
        IQueryable<InventoryClassificationPolicy> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(policy => scope.WarehouseIds.Contains(policy.WarehouseId));

    private static IQueryable<InventoryClassification> ApplyClassificationScope(
        IQueryable<InventoryClassification> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(classification => scope.WarehouseIds.Contains(classification.WarehouseId));

    private static IQueryable<InventoryClassificationHistory> ApplyHistoryScope(
        IQueryable<InventoryClassificationHistory> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(history => scope.WarehouseIds.Contains(history.WarehouseId));

    private static int NormalizeLimit(int limit, int maximum)
    {
        if (limit < 1)
        {
            return 1;
        }

        return Math.Min(limit, maximum);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string BuildRunKey(
        InventoryClassificationPolicy policy,
        DateTime asOfUtc) =>
        $"abc:{policy.Id}:{policy.Revision}:{asOfUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

    private static string BuildManualRunKey(int warehouseId, int itemId, DateTime changedAtUtc) =>
        $"abc-manual:{warehouseId}:{itemId}:{changedAtUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}:{Guid.NewGuid():N}";

    private sealed record MovementMetricRow(int ItemId, MovementType Type, decimal Quantity);

    private sealed record BalanceMetricRow(int ItemId, decimal OnHandQuantity, decimal StandardCost);

    private sealed record MetricSnapshot(
        int ItemId,
        string ItemSku,
        decimal MetricValue,
        decimal ShippedQuantity,
        int ShippedLineCount,
        decimal MovementQuantity,
        decimal InventoryValue,
        decimal CriticalityScore);

    private sealed record PolicyEvaluation(
        string RunKey,
        int ItemsExamined,
        int ClassificationsChanged,
        int ManualOverridesSkipped,
        IReadOnlyList<InventoryClassificationComparisonDto> Comparisons);

    private sealed class RecalculationState(bool dryRun)
    {
        private readonly List<InventoryClassificationComparisonDto> _comparisons = [];

        public bool DryRun { get; } = dryRun;
        public int PoliciesExamined { get; private set; }
        public int ItemsExamined { get; private set; }
        public int ClassificationsChanged { get; private set; }
        public int ManualOverridesSkipped { get; private set; }

        public void Add(
            InventoryClassificationPolicy policy,
            PolicyEvaluation evaluation)
        {
            _ = policy;
            PoliciesExamined++;
            ItemsExamined += evaluation.ItemsExamined;
            ClassificationsChanged += evaluation.ClassificationsChanged;
            ManualOverridesSkipped += evaluation.ManualOverridesSkipped;
            _comparisons.AddRange(evaluation.Comparisons);
        }

        public InventoryClassificationRecalculationResultDto ToDto() =>
            new(
                PoliciesExamined,
                ItemsExamined,
                ClassificationsChanged,
                ManualOverridesSkipped,
                DryRun,
                _comparisons);
    }
}
