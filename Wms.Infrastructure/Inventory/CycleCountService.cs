using System.Data;
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
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

public sealed class CycleCountService(
    WmsDbContext context,
    Lazy<IWarehouseWorkService> warehouseWorkService,
    IStockMovementService stockMovementService,
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
                            value.IsActive &&
                            value.IsCountable,
                    cancellationToken);
                if (location is null)
                {
                    return Result.Failure<CycleCountPlanDto>(WmsErrors.NotFound(
                        "location.not_found",
                        "The requested active countable location was not found in the warehouse."));
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

    public async Task<Result<CycleCountTaskDto>> GetTaskAsync(
        int taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.NotFound(
                "cycle_count.task_not_found",
                "The requested cycle-count task was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            task.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountTaskDto>();
        }

        var includeExpected = !task.Blind;
        if (task.Blind && task.Status is CycleCountTaskStatus.AwaitingApproval or
            CycleCountTaskStatus.Approved or CycleCountTaskStatus.Completed)
        {
            var reviewAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                task.WarehouseId,
                cancellationToken);
            includeExpected = reviewAuthorization.IsSuccess;
        }

        return Result.Success(MapTaskDetails(task, includeExpected));
    }

    public async Task<Result<CycleCountTaskDto>> StartTaskAsync(
        int taskId,
        CycleCountTaskStartInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.actor_required",
                "An authenticated actor is required to start a count."));
        }

        var task = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.NotFound(
                "cycle_count.task_not_found",
                "The requested cycle-count task was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.CountingExecute,
            task.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountTaskDto>();
        }

        if (task.StartedByUserId == actorUserId &&
            task.Status is CycleCountTaskStatus.InProgress or CycleCountTaskStatus.AwaitingApproval or
                CycleCountTaskStatus.Approved or CycleCountTaskStatus.Completed)
        {
            return Result.Success(MapTaskDetails(task, includeExpected: false));
        }

        if (task.Status != CycleCountTaskStatus.Planned)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                "cycle_count.task_cannot_start",
                "Only a planned cycle-count task can start. Generate a new task when a recount is required."));
        }

        if (task.Revision != input.ExpectedRevision)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Concurrency(
                "cycle_count.task_revision_conflict",
                "The cycle-count task changed. Reload it before starting."));
        }

        if (task.WarehouseWorkId is null)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Dependency(
                "cycle_count.work_missing",
                "The cycle-count task has no executable warehouse work item.",
                isRetryable: false));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            task.Start(actorUserId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CountTaskStarted,
                    WmsAuditEntityTypes.Count,
                    task.TaskNumber,
                    task.WarehouseId,
                    Before: new Dictionary<string, object?> { ["status"] = CycleCountTaskStatus.Planned.ToString() },
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = task.Status.ToString(),
                        ["warehouseWorkId"] = task.WarehouseWorkId,
                        ["lineCount"] = task.Lines.Count
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapTaskDetails(task, includeExpected: false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Concurrency(
                "cycle_count.task_revision_conflict",
                "The cycle-count task changed while it was starting. Reload it and retry."));
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                "cycle_count.task_cannot_start",
                exception.Message));
        }
    }

    public async Task<Result<CycleCountTaskDto>> RecordCountsAsync(
        int taskId,
        IReadOnlyList<CycleCountLineCountInput> counts,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (counts is null || counts.Count == 0)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.counts_required",
                "A physical quantity is required for every count line."));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.actor_required",
                "An authenticated actor is required to record a count."));
        }

        var task = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.NotFound(
                "cycle_count.task_not_found",
                "The requested cycle-count task was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.CountingExecute,
            task.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountTaskDto>();
        }

        if (task.Status != CycleCountTaskStatus.InProgress)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                "cycle_count.task_not_in_progress",
                "Counts can only be recorded for a task that is in progress."));
        }

        if (counts.GroupBy(value => value.LineId).Any(group => group.Count() != 1) ||
            counts.Count != task.Lines.Count ||
            task.Lines.Any(line => counts.All(value => value.LineId != line.Id)))
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.lines_incomplete",
                "Count submission must contain exactly one physical quantity for every task line."));
        }

        var unitCodes = task.Lines.Select(line => line.BaseUnitOfMeasure).Distinct().ToArray();
        var precisions = await context.UnitOfMeasures
            .Where(unit => unitCodes.Contains(unit.Code) && unit.IsActive)
            .ToDictionaryAsync(unit => unit.Code, unit => unit.Precision, cancellationToken);
        var itemIds = task.Lines.Select(line => line.ItemId).Distinct().ToArray();
        var serialControlledItems = await context.Items
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.RequiresSerial, cancellationToken);

        foreach (var line in task.Lines)
        {
            var count = counts.Single(value => value.LineId == line.Id).CountedQuantity;
            if (count < 0m)
            {
                return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                    "cycle_count.quantity_negative",
                    $"Count line {line.Sequence} cannot have a negative quantity."));
            }

            var precision = precisions.GetValueOrDefault(line.BaseUnitOfMeasure, 12);
            if (decimal.Round(count, precision, MidpointRounding.ToZero) != count)
            {
                return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                    "cycle_count.quantity_precision",
                    $"Count line {line.Sequence} exceeds the precision allowed for {line.BaseUnitOfMeasure}."));
            }

            if (serialControlledItems.TryGetValue(line.ItemId, out var itemRequiresSerial) &&
                itemRequiresSerial && count is not (0m or 1m))
            {
                return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                    "cycle_count.serial_quantity_invalid",
                    $"Serial-controlled count line {line.Sequence} must be counted as zero or one."));
            }
        }

        try
        {
            foreach (var line in task.Lines)
            {
                var count = counts.Single(value => value.LineId == line.Id).CountedQuantity;
                line.RecordCount(count, line.EmptyLocationCandidate && count == 0m);
            }

            task.Submit(clock.UtcNow.UtcDateTime);
            var zeroVariance = task.Status == CycleCountTaskStatus.Approved;
            if (zeroVariance)
            {
                task.Complete(clock.UtcNow.UtcDateTime);
                foreach (var line in task.Lines)
                {
                    line.MarkCompleted();
                }
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CountSubmitted,
                    WmsAuditEntityTypes.Count,
                    task.TaskNumber,
                    task.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = task.Status.ToString(),
                        ["lineCount"] = task.Lines.Count,
                        ["varianceLineCount"] = task.Lines.Count(line => line.VarianceQuantity != 0m),
                        ["countedQuantity"] = task.Lines.Sum(line => line.CountedQuantity ?? 0m)
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            return Result.Success(MapTaskDetails(task, includeExpected: false));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.count_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                "cycle_count.count_submit_conflict",
                exception.Message));
        }
    }

    public async Task<Result<CycleCountTaskDto>> ApproveTaskAsync(
        int taskId,
        CycleCountTaskApprovalInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.actor_required",
                "An authenticated actor is required to approve a count."));
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1_000)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.approval_reason_required",
                "A count approval reason of 1 to 1000 characters is required."));
        }

        var task = await LoadTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.NotFound(
                "cycle_count.task_not_found",
                "The requested cycle-count task was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            task.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CycleCountTaskDto>();
        }

        if (task.Status == CycleCountTaskStatus.Completed)
        {
            return Result.Success(MapTaskDetails(task, includeExpected: true));
        }

        if (task.Revision != input.ExpectedRevision)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Concurrency(
                "cycle_count.task_revision_conflict",
                "The cycle-count task changed. Reload it before approving."));
        }

        if (task.Status != CycleCountTaskStatus.AwaitingApproval)
        {
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                "cycle_count.approval_not_pending",
                "Only a submitted count with variance can be approved."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var inventoryStatusIds = task.Lines.Select(line => line.InventoryStatusId).Distinct().ToArray();
            var countableStatusIds = await context.InventoryStatuses
                .Where(status => inventoryStatusIds.Contains(status.Id) && status.IsActive && status.IsCountable)
                .Select(status => status.Id)
                .ToHashSetAsync(cancellationToken);
            var locationIds = task.Lines.Select(line => line.LocationId).Distinct().ToArray();
            var countableLocationIds = await context.Locations
                .Where(location => locationIds.Contains(location.Id) &&
                                   location.WarehouseId == task.WarehouseId &&
                                   location.IsActive && location.IsCountable)
                .Select(location => location.Id)
                .ToHashSetAsync(cancellationToken);
            var itemIds = task.Lines.Select(line => line.ItemId).Distinct().ToArray();
            var balancesByKey = (await context.InventoryBalances
                    .Where(balance => balance.WarehouseId == task.WarehouseId &&
                                      locationIds.Contains(balance.LocationId) &&
                                      itemIds.Contains(balance.ItemId))
                    .ToListAsync(cancellationToken))
                .ToDictionary(balance => balance.GetKey());

            foreach (var line in task.Lines.OrderBy(value => value.Sequence))
            {
                balancesByKey.TryGetValue(CreateBalanceKey(line), out var balance);
                if ((balance?.OnHandQuantity ?? 0m) != line.ExpectedQuantity)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                        "cycle_count.snapshot_stale",
                        $"Inventory for count line {line.Sequence} changed after the task snapshot. Generate a new count before approving."));
                }

                if (line.CountedQuantity is null ||
                    (balance?.ReservedQuantity ?? 0m) > line.CountedQuantity.Value)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Result.Failure<CycleCountTaskDto>(WmsErrors.BusinessRule(
                        "cycle_count.reserved_quantity_exceeds_count",
                        $"The counted quantity for line {line.Sequence} is below its reserved quantity."));
                }

                if (!countableStatusIds.Contains(line.InventoryStatusId) ||
                    !countableLocationIds.Contains(line.LocationId))
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Result.Failure<CycleCountTaskDto>(WmsErrors.Conflict(
                        "cycle_count.dimension_no_longer_countable",
                        $"The inventory status or location for count line {line.Sequence} is no longer active and countable."));
                }
            }

            foreach (var line in task.Lines.OrderBy(value => value.Sequence))
            {
                if (line.VarianceQuantity != 0m)
                {
                    await stockMovementService.CountVarianceAsync(
                        task,
                        line,
                        actorUserId,
                        input.Reason.Trim(),
                        cancellationToken);
                    line.Approve();
                }
            }

            var nowUtc = clock.UtcNow.UtcDateTime;
            task.Approve(actorUserId, input.Reason, nowUtc);
            task.Complete(nowUtc);
            foreach (var line in task.Lines)
            {
                line.MarkCompleted();
            }
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CountApproved,
                    WmsAuditEntityTypes.Count,
                    task.TaskNumber,
                    task.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = task.Status.ToString(),
                        ["reason"] = task.ApprovalReason,
                        ["varianceLineCount"] = task.Lines.Count(line => line.VarianceQuantity != 0m),
                        ["varianceQuantity"] = task.Lines.Sum(line => line.VarianceQuantity ?? 0m)
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapTaskDetails(task, includeExpected: true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Concurrency(
                "cycle_count.approval_concurrency_conflict",
                "Inventory or the count task changed during approval. Reload and retry."));
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountTaskDto>(WmsErrors.Validation(
                "cycle_count.approval_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<CycleCountTaskDto>(WmsErrors.BusinessRule(
                "cycle_count.approval_failed",
                exception.Message));
        }
    }

    private Task<CycleCountTask?> LoadTaskAsync(int taskId, CancellationToken cancellationToken) =>
        context.CycleCountTasks
            .Include(task => task.Lines)
            .SingleOrDefaultAsync(task => task.Id == taskId, cancellationToken);

    private static InventoryBalanceKey CreateBalanceKey(CycleCountLine line) => new(
        line.WarehouseId,
        line.LocationId,
        line.ItemId,
        line.LotId,
        line.SerialNumberId,
        line.SerialNumber,
        line.LicensePlateId,
        line.InventoryStatusId,
        line.BaseUnitOfMeasure,
        line.OwnerKind,
        line.InventoryOwnerId,
        line.OwnerCodeSnapshot);

    private static CycleCountTaskDto MapTaskDetails(CycleCountTask task, bool includeExpected) =>
        new(
            task.Id,
            task.TaskNumber,
            task.PlanId,
            task.WarehouseId,
            task.LocationId,
            task.WarehouseWorkId,
            task.Blind,
            task.FreezePolicy,
            task.SnapshotAtUtc,
            task.Status,
            task.CreatedByUserId,
            task.StartedByUserId,
            task.StartedAtUtc,
            task.SubmittedAtUtc,
            task.ApprovedByUserId,
            task.ApprovedAtUtc,
            task.ApprovalReason,
            task.Revision,
            task.Lines
                .OrderBy(line => line.Sequence)
                .Select(line => new CycleCountTaskLineDto(
                    line.Id,
                    line.Sequence,
                    line.ItemId,
                    line.LocationId,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.InventoryStatusId,
                    line.BaseUnitOfMeasure,
                    line.OwnerKind,
                    line.InventoryOwnerId,
                    line.OwnerCodeSnapshot,
                    line.EmptyLocationCandidate,
                    includeExpected ? line.ExpectedQuantity : null,
                    line.CountedQuantity,
                    includeExpected ? line.VarianceQuantity : null,
                    line.Status,
                    line.Revision))
                .ToArray());

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
                        emptyLocationCandidate: false,
                        balance.OwnerKind,
                        balance.InventoryOwnerId,
                        balance.OwnerCodeSnapshot))
                    .ToArray();
            foreach (var line in countLines)
            {
                task.AddLine(line);
                await context.CycleCountLines.AddAsync(line, cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            var workResult = await warehouseWorkService.Value.CreateAsync(
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
                        SourceReference: $"cycle-count-line:{line.Id}",
                        OwnerKind: line.OwnerKind,
                        InventoryOwnerId: line.InventoryOwnerId,
                        OwnerCodeSnapshot: line.OwnerCodeSnapshot))
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
                balance.Location.IsCountable &&
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
                emptyLocationCandidate: true,
                ownerKind: InventoryOwnerKind.CompanyOwned,
                ownerCodeSnapshot: InventoryOwnershipDimension.CompanyOwnerCode)
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
