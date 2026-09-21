using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Outbound;

public sealed class PickingStrategyService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<PickingStrategyService> logger) : IPickingStrategyService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<PickingStrategyPolicyDto>> SavePolicyAsync(
        int? policyId,
        PickingStrategyPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingStrategyPolicyDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(value => value.Id == input.WarehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    $"Warehouse '{input.WarehouseId}' was not found."));
            }

            await ValidatePolicyReferencesAsync(input, cancellationToken);
            PickingStrategyPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.PickingStrategyPolicies
                    .SingleOrDefaultAsync(value => value.Id == policyId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException($"Picking strategy policy '{policyId}' was not found.");
                if (policy.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.Validation(
                        "picking.policy_warehouse_mismatch",
                        "A picking policy cannot be moved between warehouses."));
                }

                policy.Update(
                    input.Name,
                    input.Strategy,
                    input.Priority,
                    input.MaxOrders,
                    input.MaxContainers,
                    input.MaxWeightKg,
                    input.MaxVolumeCubicMeters,
                    input.WaveTemplateId,
                    input.OrderProfileCode,
                    input.ItemId,
                    input.LocationZoneId,
                    input.PackageProfileCode,
                    input.SequenceMode,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc);
            }
            else
            {
                var normalizedKey = input.PolicyKey.Trim().ToUpperInvariant();
                if (await context.PickingStrategyPolicies.AnyAsync(
                        value => value.WarehouseId == input.WarehouseId && value.PolicyKey == normalizedKey,
                        cancellationToken))
                {
                    return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.Conflict(
                        "picking.policy_key_exists",
                        $"Picking strategy policy '{normalizedKey}' already exists in this warehouse."));
                }

                policy = new PickingStrategyPolicy(
                    input.WarehouseId,
                    input.PolicyKey,
                    input.Name,
                    input.Strategy,
                    input.Priority,
                    input.MaxOrders,
                    input.MaxContainers,
                    input.MaxWeightKg,
                    input.MaxVolumeCubicMeters,
                    input.WaveTemplateId,
                    input.OrderProfileCode,
                    input.ItemId,
                    input.LocationZoneId,
                    input.PackageProfileCode,
                    input.SequenceMode,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc);
                context.PickingStrategyPolicies.Add(policy);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PickingStrategyPolicyChanged,
                    WmsAuditEntityTypes.PickingStrategyPolicy,
                    policy.Id == 0 ? null : policy.Id.ToString(CultureInfo.InvariantCulture),
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyKey"] = policy.PolicyKey,
                        ["strategy"] = policy.Strategy.ToString(),
                        ["priority"] = policy.Priority,
                        ["maxOrders"] = policy.MaxOrders,
                        ["maxContainers"] = policy.MaxContainers
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            return Result.Success(MapPolicy(policy, warehouse.Code));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.NotFound(
                "picking.policy_not_found",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.Validation(
                "picking.policy_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.BusinessRule(
                "picking.policy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Picking strategy policy save failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<PickingStrategyPolicyDto>(WmsErrors.FromException(
                exception,
                "picking.policy_save_failed",
                "The picking strategy policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<PickingStrategyPolicyDto>>> SearchPoliciesAsync(
        PickingStrategyPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<PickingStrategyPolicyDto>>();
        }

        var policies = context.PickingStrategyPolicies
            .Include(policy => policy.Warehouse)
            .AsNoTracking()
            .Where(policy => query.WarehouseId == null || policy.WarehouseId == query.WarehouseId)
            .Where(policy => query.IncludeInactive || policy.IsActive)
            .Where(policy => query.Strategy == null || policy.Strategy == query.Strategy)
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.PolicyKey);
        return Result.Success<IReadOnlyList<PickingStrategyPolicyDto>>(
            await policies.Select(policy => MapPolicyExpression(policy)).ToListAsync(cancellationToken));
    }

    public Task<Result<PickingPlanDto>> CreatePlanAsync(
        PickingPlanCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(input, actorUserId, persist: true, cancellationToken);

    public Task<Result<PickingPlanDto>> SimulateAsync(
        PickingPlanCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(input, actorUserId, persist: false, cancellationToken);

    public async Task<Result<PickingPlanPageDto>> SearchAsync(
        PickingPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = NormalizePage(query.Page, query.PageSize);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanPageDto>();
        }

        var plans = context.PickingPlans
            .Include(plan => plan.Lines)
            .Include(plan => plan.Containers)
            .Include(plan => plan.Handoffs)
            .AsNoTracking()
            .Where(plan => query.WarehouseId == null || plan.WarehouseId == query.WarehouseId)
            .Where(plan => query.WaveId == null || plan.WaveId == query.WaveId)
            .Where(plan => query.Strategy == null || plan.Strategy == query.Strategy)
            .Where(plan => query.Status == null || plan.Status == query.Status)
            .OrderByDescending(plan => plan.Id);
        var totalCount = await plans.CountAsync(cancellationToken);
        var items = await plans
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new PickingPlanPageDto(
            items.Select(MapPlan).ToArray(),
            page.Page,
            page.PageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)page.PageSize)));
    }

    public async Task<Result<PickingPlanDto>> GetAsync(
        int planId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, tracked: false, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.plan_not_found",
                $"Picking plan '{planId}' was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        return Result.Success(MapPlan(plan));
    }

    public async Task<Result<PickingPlanDto>> ScanContainerAsync(
        int planId,
        int containerId,
        PickingContainerScanInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, tracked: true, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.plan_not_found",
                $"Picking plan '{planId}' was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PickingExecute,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || string.IsNullOrWhiteSpace(input.ScanCode))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.container_scan_invalid",
                "A target container scan and idempotency key are required."));
        }

        var container = plan.Containers.SingleOrDefault(value => value.Id == containerId);
        if (container is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.container_not_found",
                $"Container '{containerId}' is not part of picking plan '{planId}'."));
        }

        if (!string.Equals(
                container.ExpectedScanCode,
                input.ScanCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.wrong_target_container",
                "The scanned target container does not match the planned order or tote."));
        }

        try
        {
            container.Scan(input.ScanCode, actorUserId, clock.UtcNow.UtcDateTime);
            if (plan.Status == PickingPlanStatus.Planned)
            {
                plan.SetStatus(PickingPlanStatus.InProgress);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PickingContainerScanned,
                    WmsAuditEntityTypes.PickingPlanContainer,
                    container.Id.ToString(CultureInfo.InvariantCulture),
                    plan.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Id,
                        ["containerKey"] = container.ContainerKey,
                        ["scanCode"] = container.ExpectedScanCode
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPlan(plan));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.container_scan_invalid",
                exception.Message));
        }
    }

    public async Task<Result<PickingPlanDto>> CompleteHandoffAsync(
        int planId,
        int handoffId,
        PickingHandoffInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, tracked: true, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.plan_not_found",
                $"Picking plan '{planId}' was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PickingExecute,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        var handoff = plan.Handoffs.SingleOrDefault(value => value.Id == handoffId);
        if (handoff is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.handoff_not_found",
                $"Handoff '{handoffId}' is not part of picking plan '{planId}'."));
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || string.IsNullOrWhiteSpace(input.ContainerScanCode))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.handoff_invalid",
                "A container scan and idempotency key are required for a zone handoff."));
        }

        var previous = plan.Handoffs
            .Where(value => value.Sequence < handoff.Sequence)
            .OrderByDescending(value => value.Sequence)
            .FirstOrDefault();
        if (previous is not null && previous.Status != PickingHandoffStatus.Completed)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Conflict(
                "picking.handoff_out_of_order",
                $"Handoff sequence {handoff.Sequence} cannot complete before sequence {previous.Sequence}."));
        }

        var container = plan.Containers.Single(value => value.Id == handoff.PickingPlanContainerId);
        if (!string.Equals(
                handoff.ExpectedContainerScanCode,
                input.ContainerScanCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.wrong_handoff_container",
                "The scanned container does not match the expected zone handoff."));
        }

        try
        {
            if (container.TargetScanRequired && container.Status == PickingContainerStatus.Open)
            {
                container.Scan(input.ContainerScanCode, actorUserId, clock.UtcNow.UtcDateTime);
            }

            handoff.MakeReady();
            handoff.Complete(input.ContainerScanCode, actorUserId, clock.UtcNow.UtcDateTime);
            container.MarkHandedOff();
            if (plan.Status == PickingPlanStatus.Planned)
            {
                plan.SetStatus(PickingPlanStatus.InProgress);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PickingHandoffCompleted,
                    WmsAuditEntityTypes.PickingPlanHandoff,
                    handoff.Id.ToString(CultureInfo.InvariantCulture),
                    plan.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Id,
                        ["sequence"] = handoff.Sequence,
                        ["containerKey"] = container.ContainerKey
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPlan(plan));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.handoff_invalid",
                exception.Message));
        }
    }

    public async Task<Result<PickingPlanDto>> RefreshAsync(
        int planId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, tracked: true, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.plan_not_found",
                $"Picking plan '{planId}' was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PickingExecute,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        var workLineIds = plan.Lines.Select(line => line.WarehouseWorkLineId).ToArray();
        var workLines = await context.WarehouseWorkLines
            .Include(line => line.Work)
            .Where(line => workLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        foreach (var line in plan.Lines)
        {
            if (workLines.TryGetValue(line.WarehouseWorkLineId, out var workLine))
            {
                line.SyncFromWork(workLine.Work.Status, workLine.ActualQuantity);
            }
        }

        var lineStatuses = plan.Lines.Select(line => line.Status).ToArray();
        var nextStatus = lineStatuses.Length > 0 && lineStatuses.All(status => status == PickingPlanLineStatus.Picked)
            ? PickingPlanStatus.Completed
            : lineStatuses.Any(status => status is PickingPlanLineStatus.ShortPick or PickingPlanLineStatus.Exception)
                ? PickingPlanStatus.Exception
                : lineStatuses.Any(status => status == PickingPlanLineStatus.InProgress)
                    ? PickingPlanStatus.InProgress
                    : PickingPlanStatus.Planned;
        if (!plan.IsTerminal || nextStatus == plan.Status)
        {
            plan.SetStatus(nextStatus);
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(MapPlan(plan));
    }

    public async Task<Result<PickingPlanDto>> CancelAsync(
        int planId,
        string idempotencyKey,
        string reason,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, tracked: true, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                "picking.plan_not_found",
                $"Picking plan '{planId}' was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.plan_cancel_invalid",
                "An idempotency key and cancellation reason are required."));
        }

        var workLines = await context.WarehouseWorkLines
            .Include(line => line.Work)
            .Where(line => plan.Lines.Select(value => value.WarehouseWorkLineId).Contains(line.Id))
            .ToListAsync(cancellationToken);
        if (workLines.Any(line => line.Work.Status is
                WarehouseWorkStatus.InProgress or
                WarehouseWorkStatus.Paused or
                WarehouseWorkStatus.Completed))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Conflict(
                "picking.plan_execution_started",
                "A picking plan cannot be cancelled after a worker has started or completed its warehouse work."));
        }

        try
        {
            plan.Cancel(reason);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPlan(plan));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.BusinessRule(
                "picking.plan_cancel_invalid",
                exception.Message));
        }
    }

    private async Task<Result<PickingPlanDto>> CreateCoreAsync(
        PickingPlanCreateInput input,
        string actorUserId,
        bool persist,
        CancellationToken cancellationToken)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            persist ? WmsPermissions.AllocationManage : WmsPermissions.InventoryRead,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PickingPlanDto>();
        }

        if (string.IsNullOrWhiteSpace(input.CreationKey))
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.plan_creation_key_required",
                "A creation key is required for a picking plan."));
        }

        try
        {
            var existing = await LoadPlanByCreationKeyAsync(input.WarehouseId, input.CreationKey, cancellationToken);
            if (existing is not null)
            {
                return Result.Success(MapPlan(existing));
            }

            if (input.WaveId.HasValue)
            {
                var wave = await context.Waves
                    .Include(value => value.Lines)
                    .SingleOrDefaultAsync(value => value.Id == input.WaveId.Value, cancellationToken);
                if (wave is null)
                {
                    return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                        "wave.not_found",
                        $"Wave '{input.WaveId}' was not found."));
                }

                if (wave.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                        "picking.wave_warehouse_mismatch",
                        "A picking plan wave must belong to the selected warehouse."));
                }
            }

            var candidates = await LoadCandidatesAsync(input, cancellationToken);
            if (candidates.Count == 0)
            {
                return Result.Failure<PickingPlanDto>(WmsErrors.BusinessRule(
                    "picking.no_pick_work",
                    "No unplanned reservation-backed pick work matched the selected warehouse or wave."));
            }

            var policy = await ResolvePolicyAsync(input, candidates, cancellationToken);
            if (input.PolicyId.HasValue && policy is null)
            {
                return Result.Failure<PickingPlanDto>(WmsErrors.NotFound(
                    "picking.policy_not_found",
                    $"Picking strategy policy '{input.PolicyId}' was not found for this warehouse."));
            }

            var strategy = input.Strategy ?? policy?.Strategy ?? PickingStrategyKind.SingleOrder;
            if (input.Strategy.HasValue && policy is not null && input.Strategy != policy.Strategy)
            {
                return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                    "picking.strategy_policy_mismatch",
                    "The requested picking strategy does not match the selected policy."));
            }

            var maxOrders = Math.Min(ValidatePositive(input.MaxOrders, nameof(input.MaxOrders)), policy?.MaxOrders ?? input.MaxOrders);
            var maxContainers = Math.Min(ValidatePositive(input.MaxContainers, nameof(input.MaxContainers)), policy?.MaxContainers ?? input.MaxContainers);
            var maxWeightKg = input.MaxWeightKg ?? policy?.MaxWeightKg;
            var maxVolumeCubicMeters = input.MaxVolumeCubicMeters ?? policy?.MaxVolumeCubicMeters;
            var selected = SelectCandidates(candidates, strategy, maxOrders, maxContainers, maxWeightKg, maxVolumeCubicMeters);
            if (selected.Length == 0)
            {
                return Result.Failure<PickingPlanDto>(WmsErrors.BusinessRule(
                    "picking.capacity_exceeded",
                    "No complete order fitted the configured picking order, container, weight, or volume limits."));
            }

            var groups = BuildGroups(selected, strategy);
            if (strategy == PickingStrategyKind.PickAndPass && groups.Any(group => !group.ZoneLocationId.HasValue))
            {
                return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                    "picking.zone_required",
                    "Pick-and-pass work requires every source line to resolve to a warehouse zone."));
            }

            if (!persist)
            {
                return Result.Success(BuildSimulation(
                    input,
                    strategy,
                    policy,
                    maxOrders,
                    maxContainers,
                    maxWeightKg,
                    maxVolumeCubicMeters,
                    groups));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var plan = new PickingPlan(
                input.WarehouseId,
                $"PICK-{input.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}",
                input.CreationKey,
                strategy,
                input.WaveId,
                policy?.Id,
                maxOrders,
                maxContainers,
                maxWeightKg,
                maxVolumeCubicMeters,
                actorUserId);
            context.PickingPlans.Add(plan);
            await context.SaveChangesAsync(cancellationToken);

            var containers = new Dictionary<string, PickingPlanContainer>(StringComparer.Ordinal);
            var requireTargetScan = strategy is PickingStrategyKind.Cluster or PickingStrategyKind.PickAndPass;
            var containerIndex = 0;
            foreach (var group in groups)
            {
                containerIndex++;
                var containerKey = BuildContainerKey(plan.Id, strategy, group, containerIndex);
                var container = new PickingPlanContainer(
                    plan.Id,
                    containerIndex,
                    containerKey,
                    containerKey,
                    requireTargetScan,
                    group.OrderId,
                    group.ZoneLocationId);
                plan.AddContainer(container);
                context.PickingPlanContainers.Add(container);
                containers.Add(group.GroupKey, container);
            }

            await context.SaveChangesAsync(cancellationToken);

            var handoffs = new Dictionary<string, PickingPlanHandoff>(StringComparer.Ordinal);
            if (strategy == PickingStrategyKind.PickAndPass)
            {
                var handoffIndex = 0;
                foreach (var group in groups)
                {
                    handoffIndex++;
                    var container = containers[group.GroupKey];
                    var handoff = new PickingPlanHandoff(
                        plan.Id,
                        handoffIndex,
                        container.Id,
                        group.ZoneLocationId!.Value,
                        null,
                        container.ExpectedScanCode,
                        handoffIndex == 1 ? PickingHandoffStatus.Ready : PickingHandoffStatus.Pending);
                    plan.AddHandoff(handoff);
                    context.PickingPlanHandoffs.Add(handoff);
                    handoffs.Add(group.GroupKey, handoff);
                }

                await context.SaveChangesAsync(cancellationToken);
            }

            var lineSequence = 0;
            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var group = groups[groupIndex];
                var container = containers[group.GroupKey];
                var handoffId = handoffs.TryGetValue(group.GroupKey, out var handoff)
                    ? handoff.Id
                    : (int?)null;
                var containerSequence = 0;
                foreach (var candidate in group.Candidates)
                {
                    lineSequence++;
                    containerSequence++;
                    var line = new PickingPlanLine(
                        plan.Id,
                        lineSequence,
                        candidate.Work.Id,
                        candidate.Line.Id,
                        candidate.Order.Id,
                        candidate.OrderLine.Id,
                        candidate.Order.DocumentNumber,
                        candidate.Line.ItemId,
                        candidate.OrderLine.ItemSkuSnapshot,
                        candidate.Line.PlannedQuantity,
                        candidate.Line.BaseUnitOfMeasure,
                        candidate.Line.SourceLocationId,
                        candidate.ZoneLocationId,
                        candidate.Line.LotId,
                        candidate.Line.SerialNumberId,
                        candidate.Line.SerialNumber,
                        candidate.Line.LicensePlateId,
                        candidate.Line.InventoryStatusId,
                        candidate.Line.ReservationId,
                        candidate.Line.ReservationAllocationId,
                        candidate.Item.NetWeightKg,
                        candidate.Item.VolumeCubicMeters);
                    line.AssignRouting(
                        group.GroupKey,
                        container.Id,
                        containerSequence,
                        groupIndex + 1,
                        handoffId);
                    plan.AddLine(line);
                    context.PickingPlanLines.Add(line);
                }
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PickingPlanCreated,
                    WmsAuditEntityTypes.PickingPlan,
                    plan.Id.ToString(CultureInfo.InvariantCulture),
                    plan.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["strategy"] = plan.Strategy.ToString(),
                        ["waveId"] = plan.WaveId,
                        ["policyId"] = plan.PolicyId,
                        ["orders"] = selected.Select(value => value.Order.Id).Distinct().Count(),
                        ["lines"] = selected.Length,
                        ["containers"] = containers.Count,
                        ["handoffs"] = handoffs.Count
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapPlan(plan));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.Validation(
                "picking.plan_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PickingPlanDto>(WmsErrors.BusinessRule(
                "picking.plan_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Picking plan creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<PickingPlanDto>(WmsErrors.FromException(
                exception,
                "picking.plan_create_failed",
                "The picking plan could not be created."));
        }
    }

    private async Task ValidatePolicyReferencesAsync(
        PickingStrategyPolicyInput input,
        CancellationToken cancellationToken)
    {
        if (input.WaveTemplateId.HasValue && !await context.WaveTemplates.AnyAsync(
                value => value.Id == input.WaveTemplateId && value.WarehouseId == input.WarehouseId,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Wave template '{input.WaveTemplateId}' was not found in the warehouse.");
        }

        if (input.ItemId.HasValue && !await context.Items.AnyAsync(value => value.Id == input.ItemId, cancellationToken))
        {
            throw new KeyNotFoundException($"Item '{input.ItemId}' was not found.");
        }

        if (input.LocationZoneId.HasValue && !await context.Locations.AnyAsync(
                value => value.Id == input.LocationZoneId && value.WarehouseId == input.WarehouseId,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Location zone '{input.LocationZoneId}' was not found in the warehouse.");
        }
    }

    private async Task<PickingStrategyPolicy?> ResolvePolicyAsync(
        PickingPlanCreateInput input,
        IReadOnlyList<Candidate> candidates,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var policies = await context.PickingStrategyPolicies
            .Where(policy => policy.WarehouseId == input.WarehouseId && policy.IsActive)
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.Id)
            .ToListAsync(cancellationToken);
        if (input.PolicyId.HasValue)
        {
            return policies.SingleOrDefault(policy => policy.Id == input.PolicyId.Value && policy.IsEffective(now));
        }

        var waveTemplateId = input.WaveId.HasValue
            ? await context.Waves
                .Where(wave => wave.Id == input.WaveId.Value)
                .Select(wave => wave.TemplateId)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return policies.FirstOrDefault(policy =>
            policy.IsEffective(now) &&
            (input.Strategy is null || policy.Strategy == input.Strategy) &&
            (policy.WaveTemplateId is null || policy.WaveTemplateId == waveTemplateId) &&
            candidates.All(candidate =>
                (policy.ItemId is null || policy.ItemId == candidate.Line.ItemId) &&
                (policy.LocationZoneId is null || policy.LocationZoneId == candidate.ZoneLocationId) &&
                (policy.OrderProfileCode is null || policy.OrderProfileCode == candidate.Order.SourceType) &&
                (policy.PackageProfileCode is null ||
                 policy.PackageProfileCode == candidate.Order.PackagingProfileSnapshot ||
                 policy.PackageProfileCode == candidate.OrderLine.PackagingCodeSnapshot)));
    }

    private async Task<IReadOnlyList<Candidate>> LoadCandidatesAsync(
        PickingPlanCreateInput input,
        CancellationToken cancellationToken)
    {
        HashSet<int>? waveLineIds = null;
        if (input.WaveId.HasValue)
        {
            waveLineIds = (await context.WaveLines
                    .Where(line => line.WaveId == input.WaveId.Value &&
                                   line.Status != WaveLineStatus.Removed &&
                                   line.Status != WaveLineStatus.Cancelled)
                    .Select(line => line.SalesOrderLineId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        var works = await context.WarehouseWorks
            .Include(work => work.Lines)
            .AsNoTracking()
            .Where(work => work.WarehouseId == input.WarehouseId &&
                           work.Type == WarehouseWorkType.Pick &&
                           work.Status != WarehouseWorkStatus.Completed &&
                           work.Status != WarehouseWorkStatus.Cancelled)
            .OrderByDescending(work => work.Priority)
            .ThenBy(work => work.DueAtUtc)
            .ThenBy(work => work.Id)
            .ToListAsync(cancellationToken);
        var workLineIds = works.SelectMany(work => work.Lines).Select(line => line.Id).ToArray();
        if (workLineIds.Length == 0)
        {
            return [];
        }

        var alreadyPlanned = await context.PickingPlanLines
            .Where(line => workLineIds.Contains(line.WarehouseWorkLineId) &&
                          line.Plan.Status != PickingPlanStatus.Cancelled &&
                          line.Plan.Status != PickingPlanStatus.Completed)
            .Select(line => line.WarehouseWorkLineId)
            .ToHashSetAsync(cancellationToken);
        var orderLineIds = works
            .Select(work => int.TryParse(work.SourceEntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Where(id => waveLineIds is null || waveLineIds.Contains(id))
            .Distinct()
            .ToArray();
        if (orderLineIds.Length == 0)
        {
            return [];
        }

        var orderLines = await context.SalesOrderLines
            .Include(line => line.SalesOrder)
            .Include(line => line.Item)
            .Where(line => orderLineIds.Contains(line.Id) && line.SalesOrder.WarehouseId == input.WarehouseId)
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        var locations = await context.Locations
            .Where(location => location.WarehouseId == input.WarehouseId)
            .ToDictionaryAsync(location => location.Id, cancellationToken);
        var candidates = new List<Candidate>();
        foreach (var work in works)
        {
            if (!int.TryParse(work.SourceEntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderLineId) ||
                (waveLineIds is not null && !waveLineIds.Contains(orderLineId)) ||
                !orderLines.TryGetValue(orderLineId, out var orderLine))
            {
                continue;
            }

            foreach (var line in work.Lines.OrderBy(value => value.Sequence))
            {
                if (alreadyPlanned.Contains(line.Id) || line.PlannedQuantity <= line.ActualQuantity)
                {
                    continue;
                }

                candidates.Add(new Candidate(
                    work,
                    line,
                    orderLine,
                    orderLine.SalesOrder,
                    orderLine.Item,
                    ResolveZoneLocationId(line.SourceLocationId, locations)));
            }
        }

        return candidates;
    }

    private static Candidate[] SelectCandidates(
        IReadOnlyList<Candidate> candidates,
        PickingStrategyKind strategy,
        int maxOrders,
        int maxContainers,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters)
    {
        var selected = new List<Candidate>();
        var orderGroups = candidates
            .GroupBy(candidate => candidate.Order.Id)
            .OrderByDescending(group => group.Max(candidate => candidate.Order.Priority))
            .ThenBy(group => group.Min(candidate => candidate.Order.RequestedShipDate ?? DateOnly.MaxValue))
            .ThenBy(group => group.Key);
        foreach (var order in orderGroups)
        {
            if (selected.Select(candidate => candidate.Order.Id).Distinct().Count() >= maxOrders)
            {
                break;
            }

            var proposed = selected.Concat(order).ToArray();
            var proposedWeight = proposed.Sum(candidate => candidate.WeightKg);
            var proposedVolume = proposed.Sum(candidate => candidate.VolumeCubicMeters);
            var proposedContainers = proposed
                .GroupBy(candidate => GetGroupKey(candidate, strategy))
                .Count();
            if (maxWeightKg.HasValue && proposedWeight > maxWeightKg.Value ||
                maxVolumeCubicMeters.HasValue && proposedVolume > maxVolumeCubicMeters.Value ||
                proposedContainers > maxContainers)
            {
                break;
            }

            selected.AddRange(order);
        }

        return selected
            .OrderBy(candidate => candidate.Order.Id)
            .ThenBy(candidate => candidate.ZoneLocationId)
            .ThenBy(candidate => candidate.Line.SourceLocationId)
            .ThenBy(candidate => candidate.Line.ItemId)
            .ThenBy(candidate => candidate.Line.Id)
            .ToArray();
    }

    private static RoutingGroup[] BuildGroups(
        IReadOnlyList<Candidate> candidates,
        PickingStrategyKind strategy) =>
        candidates
            .GroupBy(candidate => GetGroupKey(candidate, strategy))
            .OrderBy(group => group.Key)
            .Select(group => new RoutingGroup(
                group.Key,
                group.Select(candidate => candidate.Order.Id).Distinct().Count() == 1
                    ? group.First().Order.Id
                    : null,
                group.Select(candidate => candidate.ZoneLocationId).Distinct().Count() == 1
                    ? group.First().ZoneLocationId
                    : null,
                group.OrderBy(candidate => candidate.Line.SourceLocationId)
                    .ThenBy(candidate => candidate.Line.ItemId)
                    .ThenBy(candidate => candidate.Order.Id)
                    .ThenBy(candidate => candidate.Line.Id)
                    .ToArray()))
            .ToArray();

    private static string GetGroupKey(Candidate candidate, PickingStrategyKind strategy) => strategy switch
    {
        PickingStrategyKind.Batch =>
            $"batch:{candidate.Line.ItemId}:{candidate.Line.SourceLocationId}:{candidate.Line.LotId}:" +
            $"{candidate.Line.SerialNumberId}:{candidate.Line.SerialNumber}:{candidate.Line.LicensePlateId}:" +
            $"{candidate.Line.InventoryStatusId}",
        PickingStrategyKind.Cluster or PickingStrategyKind.SingleOrder =>
            $"order:{candidate.Order.Id}",
        PickingStrategyKind.Zone => $"zone:{candidate.ZoneLocationId?.ToString(CultureInfo.InvariantCulture) ?? "0"}",
        PickingStrategyKind.PickAndPass =>
            $"pass:{candidate.Order.Id}:{candidate.ZoneLocationId?.ToString(CultureInfo.InvariantCulture) ?? "0"}",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    private static string BuildContainerKey(
        int planId,
        PickingStrategyKind strategy,
        RoutingGroup group,
        int index)
    {
        var suffix = strategy switch
        {
            PickingStrategyKind.Batch => $"BATCH-{index}",
            PickingStrategyKind.Zone => $"ZONE-{group.ZoneLocationId?.ToString(CultureInfo.InvariantCulture) ?? "0"}",
            PickingStrategyKind.PickAndPass =>
                $"ORDER-{group.OrderId?.ToString(CultureInfo.InvariantCulture) ?? "0"}-ZONE-{group.ZoneLocationId?.ToString(CultureInfo.InvariantCulture) ?? "0"}",
            _ => $"ORDER-{group.OrderId?.ToString(CultureInfo.InvariantCulture) ?? index.ToString(CultureInfo.InvariantCulture)}"
        };
        return $"PICK-{planId.ToString(CultureInfo.InvariantCulture)}-{suffix}";
    }

    private static int? ResolveZoneLocationId(int? sourceLocationId, Dictionary<int, Location> locations)
    {
        if (!sourceLocationId.HasValue)
        {
            return null;
        }

        var visited = new HashSet<int>();
        var current = sourceLocationId;
        while (current.HasValue && visited.Add(current.Value) && locations.TryGetValue(current.Value, out var location))
        {
            if (location.Type == LocationType.Zone)
            {
                return location.Id;
            }

            current = location.ParentLocationId;
        }

        return null;
    }

    private async Task<PickingPlan?> LoadPlanAsync(
        int planId,
        bool tracked,
        CancellationToken cancellationToken) =>
        await PlanQuery(tracked)
            .SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    private async Task<PickingPlan?> LoadPlanByCreationKeyAsync(
        int warehouseId,
        string creationKey,
        CancellationToken cancellationToken) =>
        await PlanQuery(tracked: false)
            .SingleOrDefaultAsync(
                plan => plan.WarehouseId == warehouseId && plan.CreationKey == creationKey.Trim(),
                cancellationToken);

    private IQueryable<PickingPlan> PlanQuery(bool tracked)
    {
        var query = context.PickingPlans
            .Include(plan => plan.Lines)
            .Include(plan => plan.Containers)
            .ThenInclude(container => container.Lines)
            .Include(plan => plan.Handoffs);
        return tracked ? query : query.AsNoTracking();
    }

    private static PickingPlanDto BuildSimulation(
        PickingPlanCreateInput input,
        PickingStrategyKind strategy,
        PickingStrategyPolicy? policy,
        int maxOrders,
        int maxContainers,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        IReadOnlyList<RoutingGroup> groups)
    {
        var requiredScan = strategy is PickingStrategyKind.Cluster or PickingStrategyKind.PickAndPass;
        var containers = groups.Select((group, index) => new PickingPlanContainerDto(
            0,
            index + 1,
            BuildContainerKey(0, strategy, group, index + 1),
            BuildContainerKey(0, strategy, group, index + 1),
            requiredScan,
            group.OrderId,
            group.ZoneLocationId,
            null,
            PickingContainerStatus.Open,
            null,
            null,
            group.Candidates.Count,
            1)).ToArray();
        var handoffs = strategy == PickingStrategyKind.PickAndPass
            ? groups.Select((group, index) => new PickingPlanHandoffDto(
                0,
                index + 1,
                0,
                group.ZoneLocationId!.Value,
                null,
                containers[index].ExpectedScanCode,
                index == 0 ? PickingHandoffStatus.Ready : PickingHandoffStatus.Pending,
                null,
                null,
                1)).ToArray()
            : [];
        var lines = groups
            .SelectMany((group, groupIndex) => group.Candidates.Select((candidate, lineIndex) =>
                new PickingPlanLineDto(
                    0,
                    groups.Take(groupIndex).Sum(group => group.Candidates.Count) + lineIndex + 1,
                    candidate.Work.Id,
                    candidate.Line.Id,
                    candidate.Order.Id,
                    candidate.OrderLine.Id,
                    candidate.Order.DocumentNumber,
                    candidate.Line.ItemId,
                    candidate.OrderLine.ItemSkuSnapshot,
                    candidate.Line.PlannedQuantity,
                    candidate.Line.ActualQuantity,
                    candidate.Line.BaseUnitOfMeasure,
                    candidate.Line.SourceLocationId,
                    candidate.ZoneLocationId,
                    candidate.Line.LotId,
                    candidate.Line.SerialNumberId,
                    candidate.Line.SerialNumber,
                    candidate.Line.LicensePlateId,
                    candidate.Line.InventoryStatusId,
                    candidate.Line.ReservationId,
                    candidate.Line.ReservationAllocationId,
                    group.GroupKey,
                    0,
                    strategy == PickingStrategyKind.PickAndPass ? 0 : null,
                    groupIndex + 1,
                    lineIndex + 1,
                    PickingPlanLineStatus.Planned,
                    null,
                    1)))
            .ToArray();
        return new PickingPlanDto(
            0,
            input.WarehouseId,
            "SIMULATION",
            input.CreationKey,
            strategy,
            input.WaveId,
            policy?.Id,
            maxOrders,
            maxContainers,
            maxWeightKg,
            maxVolumeCubicMeters,
            PickingPlanStatus.Planned,
            null,
            1,
            lines,
            containers,
            handoffs,
            lines.Select(line => line.SalesOrderId).Distinct().Count(),
            lines.Sum(line => line.PlannedQuantity),
            lines.Sum(line => line.PickedQuantity),
            groups.SelectMany(group => group.Candidates).Sum(candidate => candidate.WeightKg),
            groups.SelectMany(group => group.Candidates).Sum(candidate => candidate.VolumeCubicMeters));
    }

    private static PickingStrategyPolicyDto MapPolicy(PickingStrategyPolicy policy, string warehouseCode) =>
        new(
            policy.Id,
            policy.WarehouseId,
            warehouseCode,
            policy.PolicyKey,
            policy.Name,
            policy.Strategy,
            policy.Priority,
            policy.MaxOrders,
            policy.MaxContainers,
            policy.MaxWeightKg,
            policy.MaxVolumeCubicMeters,
            policy.WaveTemplateId,
            policy.OrderProfileCode,
            policy.ItemId,
            policy.LocationZoneId,
            policy.PackageProfileCode,
            policy.SequenceMode,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private static PickingStrategyPolicyDto MapPolicyExpression(PickingStrategyPolicy policy) =>
        MapPolicy(policy, policy.Warehouse.Code);

    private static PickingPlanDto MapPlan(PickingPlan plan)
    {
        var lines = plan.Lines
            .OrderBy(line => line.Sequence)
            .Select(line => new PickingPlanLineDto(
                line.Id,
                line.Sequence,
                line.WarehouseWorkId,
                line.WarehouseWorkLineId,
                line.SalesOrderId,
                line.SalesOrderLineId,
                line.OrderNumberSnapshot,
                line.ItemId,
                line.ItemSkuSnapshot,
                line.PlannedQuantity,
                line.PickedQuantity,
                line.BaseUnitOfMeasure,
                line.SourceLocationId,
                line.ZoneLocationId,
                line.LotId,
                line.SerialNumberId,
                line.SerialNumber,
                line.SourceLicensePlateId,
                line.InventoryStatusId,
                line.ReservationId,
                line.ReservationAllocationId,
                line.BatchKey,
                line.PickingPlanContainerId,
                line.PickingPlanHandoffId,
                line.ZoneSequence,
                line.ContainerSequence,
                line.Status,
                line.LastError,
                line.Revision))
            .ToArray();
        var containers = plan.Containers
            .OrderBy(container => container.Sequence)
            .Select(container => new PickingPlanContainerDto(
                container.Id,
                container.Sequence,
                container.ContainerKey,
                container.ExpectedScanCode,
                container.TargetScanRequired,
                container.SalesOrderId,
                container.ZoneLocationId,
                container.TargetLicensePlateId,
                container.Status,
                container.ScannedByUserId,
                container.ScannedAtUtc,
                plan.Lines.Count(line => line.PickingPlanContainerId == container.Id),
                container.Revision))
            .ToArray();
        var handoffs = plan.Handoffs
            .OrderBy(handoff => handoff.Sequence)
            .Select(handoff => new PickingPlanHandoffDto(
                handoff.Id,
                handoff.Sequence,
                handoff.PickingPlanContainerId,
                handoff.FromZoneLocationId,
                handoff.ToZoneLocationId,
                handoff.ExpectedContainerScanCode,
                handoff.Status,
                handoff.CompletedByUserId,
                handoff.CompletedAtUtc,
                handoff.Revision))
            .ToArray();
        return new PickingPlanDto(
            plan.Id,
            plan.WarehouseId,
            plan.PlanNumber,
            plan.CreationKey,
            plan.Strategy,
            plan.WaveId,
            plan.PolicyId,
            plan.MaxOrders,
            plan.MaxContainers,
            plan.MaxWeightKg,
            plan.MaxVolumeCubicMeters,
            plan.Status,
            plan.LastError,
            plan.Revision,
            lines,
            containers,
            handoffs,
            lines.Select(line => line.SalesOrderId).Distinct().Count(),
            lines.Sum(line => line.PlannedQuantity),
            lines.Sum(line => line.PickedQuantity),
            plan.Lines.Sum(line => line.PlannedQuantity * (line.UnitWeightKg ?? 0m)),
            plan.Lines.Sum(line => line.PlannedQuantity * (line.UnitVolumeCubicMeters ?? 0m)));
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaximumPageSize));

    private static int ValidatePositive(int value, string parameterName) =>
        value is < 1 or > 100_000
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private sealed record Candidate(
        WarehouseWorkEntity Work,
        WarehouseWorkLine Line,
        SalesOrderLine OrderLine,
        SalesOrder Order,
        Item Item,
        int? ZoneLocationId)
    {
        public decimal WeightKg => Line.PlannedQuantity * (Item.NetWeightKg ?? 0m);
        public decimal VolumeCubicMeters => Line.PlannedQuantity * (Item.VolumeCubicMeters ?? 0m);
    }

    private sealed record RoutingGroup(
        string GroupKey,
        int? OrderId,
        int? ZoneLocationId,
        IReadOnlyList<Candidate> Candidates);
}
