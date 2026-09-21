using System.Globalization;
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

public sealed class CycleCountService(
    WmsDbContext context,
    IWarehouseWorkService warehouseWorkService,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<CycleCountService> logger) : ICycleCountService
{
    public async Task<Result<CycleCountPlanDto>> SavePlanAsync(
        int? planId,
        CycleCountPlanInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<CycleCountPlanDto>(WmsErrors.Validation(
                "cycle_count.actor_required",
                "An authenticated actor is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountPlanDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    value => value.Id == input.WarehouseId && value.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<CycleCountPlanDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            Location? location = null;
            if (input.LocationId.HasValue)
            {
                location = await context.Locations.SingleOrDefaultAsync(
                    value => value.Id == input.LocationId.Value &&
                            value.WarehouseId == input.WarehouseId &&
                            value.IsActive,
                    cancellationToken);
                if (location is null)
                {
                    return Result.Failure<CycleCountPlanDto>(WmsErrors.NotFound(
                        "location.not_found",
                        "The requested active location was not found in the warehouse."));
                }
            }

            Item? item = null;
            if (input.ItemId.HasValue)
            {
                item = await context.Items.SingleOrDefaultAsync(
                    value => value.Id == input.ItemId.Value && value.IsActive,
                    cancellationToken);
                if (item is null)
                {
                    return Result.Failure<CycleCountPlanDto>(WmsErrors.NotFound(
                        "item.not_found",
                        "The requested active item was not found."));
                }
            }

            var duplicate = await context.CycleCountPlans.AnyAsync(
                value => value.Id != (planId ?? 0) &&
                         value.WarehouseId == input.WarehouseId &&
                         value.PlanKey == input.PlanKey.Trim(),
                cancellationToken);
            if (duplicate)
            {
                return Result.Failure<CycleCountPlanDto>(WmsErrors.Conflict(
                    "cycle_count.plan_key_conflict",
                    "A cycle-count plan with this key already exists in the warehouse."));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            CycleCountPlan plan;
            if (planId.HasValue)
            {
                plan = await context.CycleCountPlans.SingleOrDefaultAsync(
                    value => value.Id == planId.Value,
                    cancellationToken) ?? throw new KeyNotFoundException();
                if (plan.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<CycleCountPlanDto>(WmsErrors.Conflict(
                        "cycle_count.plan_identity_immutable",
                        "A cycle-count plan cannot be moved to another warehouse."));
                }

                plan.Update(
                    input.LocationId,
                    input.ItemId,
                    input.ItemClass,
                    input.FrequencyDays,
                    input.ThresholdQuantity,
                    input.Blind,
                    input.FreezePolicy,
                    input.NextDueAtUtc);
            }
            else
            {
                plan = new CycleCountPlan(
                    input.PlanKey,
                    input.WarehouseId,
                    input.LocationId,
                    input.ItemId,
                    input.ItemClass,
                    input.FrequencyDays,
                    input.ThresholdQuantity,
                    input.Blind,
                    input.FreezePolicy,
                    input.NextDueAtUtc);
                await context.CycleCountPlans.AddAsync(plan, cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CycleCountPlanChanged,
                    WmsAuditEntityTypes.Count,
                    planId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["planKey"] = input.PlanKey,
                        ["locationId"] = input.LocationId,
                        ["itemId"] = input.ItemId,
                        ["frequencyDays"] = input.FrequencyDays,
                        ["thresholdQuantity"] = input.ThresholdQuantity,
                        ["blind"] = input.Blind,
                        ["freezePolicy"] = input.FreezePolicy.ToString(),
                        ["nextDueAtUtc"] = input.NextDueAtUtc
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
        catch (KeyNotFoundException)
        {
            return Result.Failure<CycleCountPlanDto>(WmsErrors.NotFound(
                "cycle_count.plan_not_found",
                "The requested cycle-count plan was not found."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CycleCountPlanDto>(WmsErrors.Validation(
                "cycle_count.plan_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Cycle-count plan save failed");
            return Result.Failure<CycleCountPlanDto>(WmsErrors.FromException(
                exception,
                "cycle_count.plan_save_failed",
                "The cycle-count plan could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<CycleCountPlanDto>>> SearchPlansAsync(
        CycleCountPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<CycleCountPlanDto>>();
        }

        var plans = context.CycleCountPlans
            .AsNoTracking()
            .Where(plan => query.IncludeInactive || plan.IsActive);
        if (query.WarehouseId.HasValue)
        {
            plans = plans.Where(plan => plan.WarehouseId == query.WarehouseId.Value);
        }

        var rows = await plans
            .OrderBy(plan => plan.WarehouseId)
            .ThenBy(plan => plan.PlanKey)
            .Take(2_000)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<CycleCountPlanDto>>(
            rows.Select(MapPlan).ToArray());
    }

    public async Task<Result<CycleCountGenerationResultDto>> GenerateAsync(
        CycleCountGenerationQuery query,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<CycleCountGenerationResultDto>(WmsErrors.Validation(
                "cycle_count.actor_required",
                "An authenticated actor is required to generate count work."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountGenerationResultDto>();
        }

        var limit = query.Limit switch
        {
            < 1 => 1,
            > 1_000 => 1_000,
            _ => query.Limit
        };
        var nowUtc = clock.UtcNow.UtcDateTime;
        var plansQuery = context.CycleCountPlans
            .Where(plan =>
                plan.IsActive &&
                plan.NextDueAtUtc <= nowUtc &&
                (!query.WarehouseId.HasValue || plan.WarehouseId == query.WarehouseId.Value) &&
                (!query.PlanId.HasValue || plan.Id == query.PlanId.Value));
        var plans = await plansQuery
            .OrderBy(plan => plan.NextDueAtUtc)
            .ThenBy(plan => plan.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var summaries = new List<CycleCountTaskSummaryDto>();
        var created = 0;
        var reused = 0;
        var linesCreated = 0;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var generated = await GenerateForPlanAsync(
                    plan,
                    actorUserId,
                    nowUtc,
                    cancellationToken);
                created += generated.Tasks.Count(value => !value.Reused);
                reused += generated.Tasks.Count(value => value.Reused);
                linesCreated += generated.Tasks
                    .Where(value => !value.Reused)
                    .Sum(value => value.LineCount);
                summaries.AddRange(generated.Tasks.Select(value =>
                    MapTask(value.Task, includeExpected: !value.Task.Blind)));
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(new CycleCountGenerationResultDto(
                plans.Count,
                created,
                reused,
                linesCreated,
                summaries));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CycleCountGenerationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountGenerationResultDto>(exception.Error);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Cycle-count work generation failed");
            return Result.Failure<CycleCountGenerationResultDto>(WmsErrors.FromException(
                exception,
                "cycle_count.generation_failed",
                "Cycle-count work could not be generated."));
        }
    }

    private async Task<PlanGenerationResult> GenerateForPlanAsync(
        CycleCountPlan plan,
        string actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var taskGroups = await LoadBalanceGroupsAsync(plan, cancellationToken);
        if (taskGroups.Count == 0)
        {
            if (!plan.LocationId.HasValue || !plan.ItemId.HasValue)
            {
                plan.MarkGenerated(nowUtc);
                throw new CycleCountGenerationException(WmsErrors.Dependency(
                    "cycle_count.scope_empty",
                    $"Plan '{plan.PlanKey}' has no eligible balance scope to count.",
                    isRetryable: false));
            }

            taskGroups.Add(new BalanceGroup(plan.LocationId.Value, []));
        }

        var dueAtUtc = plan.NextDueAtUtc;
        var generatedTasks = new List<GeneratedTask>();
        foreach (var group in taskGroups)
        {
            var taskKey = BuildTaskKey(plan, group.LocationId, dueAtUtc);
            var existing = await context.CycleCountTasks
                .Include(task => task.Lines)
                .SingleOrDefaultAsync(task => task.TaskKey == taskKey, cancellationToken);
            if (existing is not null)
            {
                generatedTasks.Add(new GeneratedTask(existing, 0, true));
                continue;
            }

            var task = new CycleCountTask(
                taskKey,
                $"CC-{plan.WarehouseId}-{Guid.NewGuid():N}",
                plan.Id,
                plan.WarehouseId,
                group.LocationId,
                plan.Blind,
                plan.FreezePolicy,
                nowUtc,
                actorUserId);
            await context.CycleCountTasks.AddAsync(task, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var countLines = group.Balances.Count == 0
                ? await CreateEmptyLineAsync(task, plan, group.LocationId, cancellationToken)
                : group.Balances
                    .Select((balance, index) => new CycleCountLine(
                        task.Id,
                        index + 1,
                        plan.WarehouseId,
                        balance.LocationId,
                        balance.ItemId,
                        balance.OnHandQuantity,
                        balance.BaseUnitOfMeasure,
                        balance.LotId,
                        balance.SerialNumberId,
                        balance.SerialNumber,
                        balance.LicensePlateId,
                        balance.InventoryStatusId,
                        emptyLocationCandidate: false))
                    .ToArray();
            foreach (var line in countLines)
            {
                task.AddLine(line);
                await context.CycleCountLines.AddAsync(line, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            var workResult = await warehouseWorkService.CreateAsync(
                new WarehouseWorkInput(
                    $"cycle-count:{task.Id}",
                    WarehouseWorkType.Count,
                    plan.WarehouseId,
                    "CycleCountTask",
                    task.Id.ToString(CultureInfo.InvariantCulture),
                    Priority: plan.ThresholdQuantity > 0m ? 20 : 50,
                    SourceLineReference: task.TaskNumber,
                    QueueCode: "COUNT",
                    Notes: plan.Blind
                        ? "Blind count: expected quantities are not part of the count contract."
                        : "Guided count: verify each inventory dimension against the captured snapshot.",
                    MakeAvailable: true,
                    Lines: countLines.Select(line => new WarehouseWorkLineInput(
                        line.Sequence,
                        plan.WarehouseId,
                        line.ItemId,
                        plan.Blind ? 1m : Math.Max(line.ExpectedQuantity, 1m),
                        line.BaseUnitOfMeasure,
                        line.LocationId,
                        line.LocationId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        line.InventoryStatusId,
                        SourceReference: $"cycle-count-line:{line.Id}"))
                        .ToArray()),
                actorUserId,
                cancellationToken);
            if (workResult.IsFailure)
            {
                throw new CycleCountGenerationException(workResult.FirstError!);
            }

            task.LinkWork(workResult.Value.Id);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CycleCountTaskGenerated,
                    WmsAuditEntityTypes.Count,
                    task.TaskNumber,
                    plan.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Id,
                        ["workId"] = workResult.Value.Id,
                        ["lineCount"] = countLines.Count,
                        ["blind"] = plan.Blind,
                        ["snapshotAtUtc"] = task.SnapshotAtUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            generatedTasks.Add(new GeneratedTask(task, countLines.Count, false));
        }

        plan.MarkGenerated(nowUtc);
        return new PlanGenerationResult(generatedTasks);
    }

    private async Task<List<BalanceGroup>> LoadBalanceGroupsAsync(
        CycleCountPlan plan,
        CancellationToken cancellationToken)
    {
        var balances = context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Location)
            .Include(balance => balance.Item)
            .Where(balance =>
                balance.WarehouseId == plan.WarehouseId &&
                balance.Location.IsActive &&
                balance.InventoryStatus.IsCountable &&
                (!plan.LocationId.HasValue || balance.LocationId == plan.LocationId.Value) &&
                (!plan.ItemId.HasValue || balance.ItemId == plan.ItemId.Value));
        if (!string.IsNullOrWhiteSpace(plan.ItemClass))
        {
            if (!Enum.TryParse<InventoryClassificationClass>(
                    plan.ItemClass,
                    ignoreCase: true,
                    out var classificationClass))
            {
                return [];
            }

            var asOfUtc = DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
            var classifiedItemIds = await context.InventoryClassifications
                .AsNoTracking()
                .Where(classification =>
                    classification.WarehouseId == plan.WarehouseId &&
                    classification.Classification == classificationClass &&
                    (classification.Source != InventoryClassificationSource.ManualOverride ||
                     !classification.ManualOverrideExpiresAtUtc.HasValue ||
                     classification.ManualOverrideExpiresAtUtc.Value > asOfUtc))
                .Select(classification => classification.ItemId)
                .ToListAsync(cancellationToken);
            balances = balances.Where(balance => classifiedItemIds.Contains(balance.ItemId));
        }

        var rows = await balances
            .OrderBy(balance => balance.LocationId)
            .ThenBy(balance => balance.ItemId)
            .ThenBy(balance => balance.Id)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(balance => balance.LocationId)
            .Select(group => new BalanceGroup(group.Key, group.ToArray()))
            .ToList();
    }

    private async Task<IReadOnlyList<CycleCountLine>> CreateEmptyLineAsync(
        CycleCountTask task,
        CycleCountPlan plan,
        int locationId,
        CancellationToken cancellationToken)
    {
        var statusId = await context.InventoryStatuses
            .Where(status => status.IsActive && status.IsCountable)
            .OrderBy(status => status.Id)
            .Select(status => status.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (statusId <= 0 || !plan.ItemId.HasValue)
        {
            throw new CycleCountGenerationException(WmsErrors.Dependency(
                "cycle_count.empty_scope_unavailable",
                "An empty-location count requires an item and a countable inventory status.",
                isRetryable: false));
        }

        var item = await context.Items.SingleAsync(value => value.Id == plan.ItemId.Value, cancellationToken);
        return
        [
            new CycleCountLine(
                task.Id,
                1,
                plan.WarehouseId,
                locationId,
                item.Id,
                0m,
                item.UnitOfMeasure,
                null,
                null,
                null,
                null,
                statusId,
                emptyLocationCandidate: true)
        ];
    }

    private static string BuildTaskKey(
        CycleCountPlan plan,
        int locationId,
        DateTime dueAtUtc) =>
        $"cycle-count:{plan.Id}:{dueAtUtc:yyyyMMddHHmmss}:{locationId}";

    private static CycleCountPlanDto MapPlan(CycleCountPlan plan) =>
        new(
            plan.Id,
            plan.PlanKey,
            plan.WarehouseId,
            plan.LocationId,
            plan.ItemId,
            plan.ItemClass,
            plan.FrequencyDays,
            plan.ThresholdQuantity,
            plan.Blind,
            plan.FreezePolicy,
            plan.NextDueAtUtc,
            plan.IsActive,
            plan.Revision);

    private static CycleCountTaskSummaryDto MapTask(
        CycleCountTask task,
        bool includeExpected)
    {
        var lines = task.Lines.ToArray();
        return new CycleCountTaskSummaryDto(
            task.Id,
            task.TaskNumber,
            task.PlanId,
            task.WarehouseId,
            task.LocationId,
            task.WarehouseWorkId,
            task.Blind,
            task.SnapshotAtUtc,
            task.Status,
            lines.Length,
            includeExpected ? lines.Sum(line => line.ExpectedQuantity) : null,
            lines.All(line => line.CountedQuantity.HasValue)
                ? lines.Sum(line => line.CountedQuantity!.Value)
                : null,
            includeExpected && lines.All(line => line.VarianceQuantity.HasValue)
                ? lines.Sum(line => line.VarianceQuantity!.Value)
                : null);
    }

    private sealed record BalanceGroup(int LocationId, IReadOnlyList<InventoryBalance> Balances);

    private sealed record GeneratedTask(CycleCountTask Task, int LineCount, bool Reused);

    private sealed record PlanGenerationResult(IReadOnlyList<GeneratedTask> Tasks);

    private sealed class CycleCountGenerationException(ResultError error) : Exception(error.Message)
    {
        public ResultError Error { get; } = error;
    }
}
