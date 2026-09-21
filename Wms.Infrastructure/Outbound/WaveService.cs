using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.SalesOrders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Outbound;

/// <summary>
/// Coordinates outbound demand into durable, resumable waves. Allocation and
/// work creation remain owned by their existing services; this service records
/// the wave boundary, selection snapshot, step outcomes, and retry keys.
/// </summary>
public sealed class WaveService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    ISalesOrderAllocationService salesOrderAllocationService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<WaveService> logger,
    IReplenishmentExecutionService? replenishmentExecutionService = null) : IWaveService
{
    private const int MaximumPageSize = 200;
    private const int MaximumBatchSize = 1_000;

    private static readonly SalesOrderStatus[] EligibleOrderStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.Allocating,
        SalesOrderStatus.PartiallyAllocated
    ];

    public async Task<Result<WaveTemplateDto>> SaveTemplateAsync(
        int? templateId,
        WaveTemplateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveTemplateDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WaveTemplateDto>();
        }

        try
        {
            var warehouse = await LoadActiveWarehouseAsync(input.WarehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<WaveTemplateDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            await ValidateCustomerAsync(input.CustomerId, input.WarehouseId, cancellationToken);
            var templateKey = NormalizeRequired(input.TemplateKey, 80, nameof(input.TemplateKey));
            var duplicate = await context.WaveTemplates
                .AsNoTracking()
                .AnyAsync(template =>
                    template.Id != (templateId ?? 0) &&
                    template.WarehouseId == input.WarehouseId &&
                    template.TemplateKey == templateKey,
                    cancellationToken);
            if (duplicate)
            {
                return Result.Failure<WaveTemplateDto>(WmsErrors.Conflict(
                    "wave.template_key_conflict",
                    "A wave template with this key already exists in the warehouse."));
            }

            WaveTemplate template;
            if (templateId.HasValue)
            {
                template = await context.WaveTemplates
                    .SingleOrDefaultAsync(candidate => candidate.Id == templateId.Value, cancellationToken)
                    ?? throw new KeyNotFoundException();
                if (template.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<WaveTemplateDto>(WmsErrors.Conflict(
                        "wave.template_identity_immutable",
                        "A wave template cannot be moved to another warehouse."));
                }

                template.Update(
                    input.Name,
                    input.TriggerType,
                    input.Priority,
                    input.Limit,
                    input.ReleaseToWarehouse,
                    input.ScheduleCron,
                    input.RequestedShipDateFrom,
                    input.RequestedShipDateTo,
                    input.CarrierCode,
                    input.CustomerId,
                    input.MinimumPriority,
                    input.SourceType);
            }
            else
            {
                template = new WaveTemplate(
                    input.WarehouseId,
                    templateKey,
                    input.Name,
                    input.TriggerType,
                    input.Priority,
                    input.Limit,
                    input.ReleaseToWarehouse,
                    input.ScheduleCron,
                    input.RequestedShipDateFrom,
                    input.RequestedShipDateTo,
                    input.CarrierCode,
                    input.CustomerId,
                    input.MinimumPriority,
                    input.SourceType);
                context.WaveTemplates.Add(template);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WaveTemplateChanged,
                    WmsAuditEntityTypes.WaveTemplate,
                    templateId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["templateKey"] = template.TemplateKey,
                        ["name"] = template.Name,
                        ["triggerType"] = template.TriggerType.ToString(),
                        ["limit"] = template.Limit,
                        ["releaseToWarehouse"] = template.ReleaseToWarehouse,
                        ["scheduleCron"] = template.ScheduleCron,
                        ["customerId"] = template.CustomerId,
                        ["sourceType"] = template.SourceType
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapTemplate(template, warehouse.Code));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result.Failure<WaveTemplateDto>(WmsErrors.NotFound(
                "wave.template_not_found",
                "The requested wave template was not found."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WaveTemplateDto>(WmsErrors.Validation(
                "wave.template_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave template save failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<WaveTemplateDto>(WmsErrors.FromException(
                exception,
                "wave.template_save_failed",
                "The wave template could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<WaveTemplateDto>>> SearchTemplatesAsync(
        WaveTemplateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<WaveTemplateDto>>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var templates = context.WaveTemplates
            .AsNoTracking()
            .Include(template => template.Warehouse)
            .AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            var warehouseIds = scope.WarehouseIds.ToArray();
            templates = templates.Where(template => warehouseIds.Contains(template.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            templates = templates.Where(template => template.WarehouseId == query.WarehouseId.Value);
        }

        if (!query.IncludeInactive)
        {
            templates = templates.Where(template => template.IsActive);
        }

        var rows = await templates
            .OrderBy(template => template.WarehouseId)
            .ThenBy(template => template.TemplateKey)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<WaveTemplateDto>>(
            rows.Select(template => MapTemplate(template, template.Warehouse.Code)).ToArray());
    }

    public Task<Result<WaveDto>> CreateAsync(
        WaveCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default) =>
        CreateCoreAsync(input, actorUserId, internalExecution: false, cancellationToken: cancellationToken);

    private async Task<Result<WaveDto>> CreateCoreAsync(
        WaveCreateInput input,
        string actorUserId,
        bool internalExecution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        if (!internalExecution)
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.AllocationManage,
                input.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<WaveDto>();
            }
        }

        var creationKey = NormalizeRequired(input.CreationKey, 250, nameof(input.CreationKey));
        try
        {
            var warehouse = await LoadActiveWarehouseAsync(input.WarehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<WaveDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var existing = await LoadByCreationKeyAsync(
                input.WarehouseId,
                creationKey,
                cancellationToken);
            if (existing is not null)
            {
                return Result.Success(MapWave(existing));
            }

            if (input.TemplateId.HasValue)
            {
                var template = await context.WaveTemplates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(value => value.Id == input.TemplateId.Value, cancellationToken);
                if (template is null || template.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<WaveDto>(WmsErrors.NotFound(
                        "wave.template_not_found",
                        "The requested wave template was not found in this warehouse."));
                }
            }

            await ValidateCustomerAsync(input.CustomerId, input.WarehouseId, cancellationToken);
            ValidateSelectionDates(input.RequestedShipDateFrom, input.RequestedShipDateTo);
            var selected = await SelectDemandAsync(input, cancellationToken);
            if (selected.Count == 0)
            {
                return Result.Failure<WaveDto>(WmsErrors.BusinessRule(
                    "wave.no_eligible_demand",
                    "No eligible outbound demand matched the requested wave criteria."));
            }

            var now = clock.UtcNow.UtcDateTime;
            var criteriaJson = BuildCriteriaJson(input);
            var wave = new Wave(
                input.WarehouseId,
                BuildWaveNumber(warehouse.Code, now),
                creationKey,
                input.TemplateId,
                NormalizeOptional(input.TemplateKey, 80)?.ToUpperInvariant(),
                input.TriggerType,
                input.Priority,
                NormalizeUtc(input.PlannedStartAtUtc),
                NormalizeUtc(input.PlannedReleaseAtUtc),
                criteriaJson,
                input.Limit,
                actorUserId);

            context.Waves.Add(wave);
            await context.SaveChangesAsync(cancellationToken);

            foreach (var selection in selected)
            {
                var line = new WaveLine(
                    wave.Id,
                    selection.Order.Id,
                    selection.Line.Id,
                    selection.Line.LineNumber,
                    selection.Line.ItemId,
                    selection.Line.ItemSkuSnapshot,
                    selection.Quantity);
                wave.AddLine(line);
                context.WaveLines.Add(line);
            }

            var selectionHistory = BeginHistory(
                wave,
                WaveStepType.SelectDemand,
                $"{creationKey}:select",
                now);
            selectionHistory.Complete(
                WaveStepStatus.Succeeded,
                selected.Count,
                selected.Count,
                0,
                now,
                detailsJson: JsonSerializer.Serialize(new
                {
                    selectedLines = selected.Count,
                    capacity = input.Limit
                }));

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WaveCreated,
                    WmsAuditEntityTypes.Wave,
                    wave.Id.ToString(CultureInfo.InvariantCulture),
                    wave.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["waveNumber"] = wave.WaveNumber,
                        ["creationKey"] = wave.CreationKey,
                        ["triggerType"] = wave.TriggerType.ToString(),
                        ["selectedLines"] = selected.Count,
                        ["criteria"] = criteriaJson
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(MapWave(wave));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.Validation(
                "wave.invalid",
                exception.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<WaveDto>(WmsErrors.Concurrency(
                "wave.concurrency_conflict",
                "The wave or its demand changed while the wave was being created."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<WaveDto>(WmsErrors.FromException(
                exception,
                "wave.create_failed",
                "The outbound wave could not be created."));
        }
    }

    public async Task<Result<WaveDto>> SimulateAsync(
        WaveCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WaveDto>();
        }

        try
        {
            var warehouse = await LoadActiveWarehouseAsync(input.WarehouseId, cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<WaveDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            await ValidateCustomerAsync(input.CustomerId, input.WarehouseId, cancellationToken);
            ValidateSelectionDates(input.RequestedShipDateFrom, input.RequestedShipDateTo);
            var selected = await SelectDemandAsync(input, cancellationToken);
            var now = clock.UtcNow.UtcDateTime;
            var lines = selected
                .Select((selection, index) => new WaveLineDto(
                    index + 1,
                    selection.Order.Id,
                    selection.Line.Id,
                    selection.Line.LineNumber,
                    selection.Line.ItemId,
                    selection.Line.ItemSkuSnapshot,
                    selection.Quantity,
                    0m,
                    0m,
                    null,
                    0,
                    WaveLineStatus.Selected,
                    null,
                    null,
                    "Selected by deterministic wave criteria.",
                    null,
                    1L))
                .ToArray();
            var history = new WaveStepDto(
                0,
                WaveStepType.SelectDemand,
                1,
                "simulation:select",
                WaveStepStatus.Succeeded,
                now,
                now,
                lines.Length,
                lines.Length,
                0,
                null,
                JsonSerializer.Serialize(new { isSimulation = true }));
            return Result.Success(new WaveDto(
                0,
                input.WarehouseId,
                "SIMULATION",
                NormalizeRequired(input.CreationKey, 250, nameof(input.CreationKey)),
                input.TemplateId,
                NormalizeOptional(input.TemplateKey, 80)?.ToUpperInvariant(),
                input.TriggerType,
                input.Priority,
                NormalizeUtc(input.PlannedStartAtUtc),
                NormalizeUtc(input.PlannedReleaseAtUtc),
                input.Limit,
                WaveStatus.Planned,
                null,
                null,
                null,
                1,
                true,
                lines,
                [history],
                lines.Length,
                0,
                0,
                0));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.Validation(
                "wave.simulation_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave simulation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<WaveDto>(WmsErrors.FromException(
                exception,
                "wave.simulation_failed",
                "The outbound wave could not be simulated."));
        }
    }

    public async Task<Result<WavePageDto>> SearchAsync(
        WaveQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WavePageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var waves = context.Waves
            .AsNoTracking()
            .AsSplitQuery()
            .Include(wave => wave.Warehouse)
            .Include(wave => wave.Lines)
            .Include(wave => wave.History)
            .AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            var warehouseIds = scope.WarehouseIds.ToArray();
            waves = waves.Where(wave => warehouseIds.Contains(wave.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            waves = waves.Where(wave => wave.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Status.HasValue)
        {
            waves = waves.Where(wave => wave.Status == query.Status.Value);
        }

        if (query.TemplateId.HasValue)
        {
            waves = waves.Where(wave => wave.TemplateId == query.TemplateId.Value);
        }

        var totalCount = await waves.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var rows = await waves
            .OrderByDescending(wave => wave.Priority)
            .ThenBy(wave => wave.PlannedReleaseAtUtc)
            .ThenBy(wave => wave.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new WavePageDto(
            rows.Select(MapWave).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<WaveDto>> GetAsync(
        int waveId,
        CancellationToken cancellationToken = default)
    {
        var wave = await LoadWaveAsync(waveId, cancellationToken);
        if (wave is null)
        {
            return Result.Failure<WaveDto>(WmsErrors.NotFound(
                "wave.not_found",
                "The requested outbound wave was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            wave.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<WaveDto>()
            : Result.Success(MapWave(wave));
    }

    public async Task<Result<WaveDto>> ProcessAsync(
        int waveId,
        WaveProcessInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var wave = await LoadWaveAsync(waveId, cancellationToken);
        if (wave is null)
        {
            return Result.Failure<WaveDto>(WmsErrors.NotFound(
                "wave.not_found",
                "The requested outbound wave was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            wave.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WaveDto>();
        }

        var idempotencyKey = NormalizeRequired(input.IdempotencyKey, 250, nameof(input.IdempotencyKey));
        if (input.BatchSize is < 1 or > MaximumBatchSize)
        {
            return Result.Failure<WaveDto>(WmsErrors.Validation(
                "wave.batch_size_invalid",
                $"Batch size must be between 1 and {MaximumBatchSize}."));
        }

        if (wave.History.Any(history =>
                history.IdempotencyKey == idempotencyKey &&
                history.Step == WaveStepType.Complete &&
                history.Status is WaveStepStatus.Succeeded or WaveStepStatus.PartiallySucceeded))
        {
            return Result.Success(MapWave(wave));
        }

        if (!wave.CanProcess)
        {
            return Result.Failure<WaveDto>(WmsErrors.BusinessRule(
                "wave.not_processable",
                $"Wave '{wave.WaveNumber}' cannot be processed while it is {wave.Status}."));
        }

        var now = clock.UtcNow.UtcDateTime;
        try
        {
            wave.StartProcessing(idempotencyKey, now);
            await context.SaveChangesAsync(cancellationToken);

            var candidates = wave.Lines
                .Where(line => line.Status is
                    WaveLineStatus.Selected or
                    WaveLineStatus.Failed or
                    WaveLineStatus.Shortage or
                    WaveLineStatus.Processing)
                .OrderBy(line => line.Id)
                .Take(input.BatchSize)
                .ToArray();
            var allocationHistory = BeginHistory(
                wave,
                WaveStepType.Allocate,
                $"{idempotencyKey}:allocate",
                now);
            await context.SaveChangesAsync(cancellationToken);

            var succeeded = 0;
            var failed = 0;
            var shortage = 0;
            var workCreated = 0;
            foreach (var line in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                line.StartProcessing(now);
                await context.SaveChangesAsync(cancellationToken);

                var allocation = await salesOrderAllocationService.AllocateAsync(
                    line.SalesOrderId,
                    new SalesOrderAllocationCommand(
                        [line.SalesOrderLineId],
                        input.ReleaseToWarehouse,
                        Replan: false,
                        Simulation: false,
                        $"{idempotencyKey}:line:{line.Id.ToString(CultureInfo.InvariantCulture)}",
                        input.Reason ?? $"Outbound wave {wave.WaveNumber}"),
                    actorUserId,
                    cancellationToken);
                if (allocation.IsFailure)
                {
                    line.RecordFailure(
                        allocation.ErrorCode.Length == 0 ? "wave.allocation_failed" : allocation.ErrorCode,
                        allocation.Error.Length == 0 ? "The sales-order line could not be allocated." : allocation.Error,
                        clock.UtcNow.UtcDateTime);
                    failed++;
                    await context.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var resultLine = allocation.Value.Lines
                    .SingleOrDefault(value => value.LineId == line.SalesOrderLineId);
                if (resultLine is null)
                {
                    line.RecordFailure(
                        "wave.allocation_result_missing",
                        "The allocation service returned no result for the selected line.",
                        clock.UtcNow.UtcDateTime);
                    failed++;
                    await context.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var lineWorkCount = allocation.Value.Work.Count(work => work.LineId == line.SalesOrderLineId);
                workCreated += lineWorkCount;
                line.RecordResult(
                    resultLine.AllocatedBaseQuantity,
                    resultLine.BackorderBaseQuantity,
                    resultLine.ReservationId,
                    lineWorkCount,
                    resultLine.Explanation,
                    clock.UtcNow.UtcDateTime);
                if (resultLine.BackorderBaseQuantity > 0m)
                {
                    shortage++;
                }
                else
                {
                    succeeded++;
                }

                await context.SaveChangesAsync(cancellationToken);
            }

            allocationHistory.Complete(
                failed == 0
                    ? WaveStepStatus.Succeeded
                    : succeeded == 0
                        ? WaveStepStatus.Failed
                        : WaveStepStatus.PartiallySucceeded,
                candidates.Length,
                succeeded,
                failed,
                clock.UtcNow.UtcDateTime,
                failed == 0 ? null : $"{failed} line(s) failed allocation.",
                JsonSerializer.Serialize(new { shortageLines = shortage }));

            if (workCreated > 0)
            {
                var workHistory = BeginHistory(
                    wave,
                    WaveStepType.CreateWork,
                    $"{idempotencyKey}:work",
                    now);
                workHistory.Complete(
                    WaveStepStatus.Succeeded,
                    succeeded + shortage,
                    workCreated,
                    0,
                    clock.UtcNow.UtcDateTime,
                    detailsJson: JsonSerializer.Serialize(new { workCreated }));
            }

            string? replenishmentError = null;
            if (shortage > 0 && replenishmentExecutionService is not null)
            {
                var replenishmentHistory = BeginHistory(
                    wave,
                    WaveStepType.Replenishment,
                    $"{idempotencyKey}:replenishment",
                    now);
                var replenishment = await replenishmentExecutionService.GenerateAsync(
                    new ReplenishmentGenerationQuery(wave.WarehouseId, Limit: input.BatchSize),
                    actorUserId,
                    cancellationToken);
                if (replenishment.IsFailure)
                {
                    replenishmentError = replenishment.Error;
                    replenishmentHistory.Complete(
                        WaveStepStatus.Failed,
                        shortage,
                        0,
                        shortage,
                        clock.UtcNow.UtcDateTime,
                        replenishment.Error);
                }
                else
                {
                    replenishmentHistory.Complete(
                        replenishment.Value.Blocked > 0
                            ? WaveStepStatus.PartiallySucceeded
                            : WaveStepStatus.Succeeded,
                        replenishment.Value.SignalsExamined,
                        replenishment.Value.WorkCreated + replenishment.Value.WorkReused,
                        replenishment.Value.Blocked,
                        clock.UtcNow.UtcDateTime,
                        detailsJson: JsonSerializer.Serialize(new
                        {
                            replenishment.Value.WorkCreated,
                            replenishment.Value.WorkReused,
                            replenishment.Value.Blocked
                        }));
                }
            }

            var activeLines = wave.Lines
                .Where(line => line.Status is not (WaveLineStatus.Removed or WaveLineStatus.Cancelled))
                .ToArray();
            var pending = activeLines.Count(line => line.Status is
                WaveLineStatus.Selected or WaveLineStatus.Processing);
            var lineFailures = activeLines.Count(line => line.Status == WaveLineStatus.Failed);
            var lineShortages = activeLines.Count(line => line.Status == WaveLineStatus.Shortage);
            var successfulLines = activeLines.Count(line =>
                line.Status is WaveLineStatus.Allocated or WaveLineStatus.Released);
            var validationHistory = BeginHistory(
                wave,
                WaveStepType.Validate,
                $"{idempotencyKey}:validate",
                now);
            var validationSucceeded = pending == 0 && lineFailures == 0 && lineShortages == 0;
            validationHistory.Complete(
                validationSucceeded ? WaveStepStatus.Succeeded : WaveStepStatus.PartiallySucceeded,
                activeLines.Length,
                successfulLines,
                lineFailures + lineShortages + pending,
                clock.UtcNow.UtcDateTime,
                replenishmentError);

            if (input.ReleaseToWarehouse)
            {
                var releaseHistory = BeginHistory(
                    wave,
                    WaveStepType.Release,
                    $"{idempotencyKey}:release",
                    now);
                releaseHistory.Complete(
                    validationSucceeded && activeLines.Length > 0 &&
                    activeLines.All(line => line.Status == WaveLineStatus.Released)
                        ? WaveStepStatus.Succeeded
                        : WaveStepStatus.PartiallySucceeded,
                    activeLines.Length,
                    activeLines.Count(line => line.Status == WaveLineStatus.Released),
                    activeLines.Count(line => line.Status != WaveLineStatus.Released),
                    clock.UtcNow.UtcDateTime,
                    replenishmentError);
            }

            var finalStatus = ResolveFinalStatus(activeLines, input.ReleaseToWarehouse);
            wave.SetResult(finalStatus, replenishmentError, clock.UtcNow.UtcDateTime);
            var completionHistory = BeginHistory(
                wave,
                WaveStepType.Complete,
                idempotencyKey,
                now);
            completionHistory.Complete(
                finalStatus is WaveStatus.Released or WaveStatus.Completed
                    ? WaveStepStatus.Succeeded
                    : finalStatus == WaveStatus.Failed
                        ? WaveStepStatus.Failed
                        : WaveStepStatus.PartiallySucceeded,
                activeLines.Length,
                successfulLines,
                lineFailures + lineShortages + pending,
                clock.UtcNow.UtcDateTime,
                replenishmentError);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WaveProcessed,
                    WmsAuditEntityTypes.Wave,
                    wave.Id.ToString(CultureInfo.InvariantCulture),
                    wave.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = wave.Status.ToString(),
                        ["selectedLines"] = activeLines.Length,
                        ["successfulLines"] = successfulLines,
                        ["failedLines"] = lineFailures,
                        ["shortageLines"] = lineShortages,
                        ["workCreated"] = workCreated
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapWave(wave));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<WaveDto>(WmsErrors.Concurrency(
                "wave.process_concurrency_conflict",
                "The wave or its outbound demand changed while processing was in progress."));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave processing failed for {WaveId}", waveId);
            try
            {
                wave.SetResult(WaveStatus.Failed, exception.Message, clock.UtcNow.UtcDateTime);
                await context.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveException)
            {
                logger.LogError(saveException, "Failed to persist failed state for wave {WaveId}", waveId);
            }

            return Result.Failure<WaveDto>(WmsErrors.FromException(
                exception,
                "wave.process_failed",
                "The outbound wave could not be processed."));
        }
    }

    public async Task<Result<WaveDto>> RemoveLineAsync(
        int waveId,
        int salesOrderLineId,
        WaveLineRemovalInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var wave = await LoadWaveAsync(waveId, cancellationToken);
        if (wave is null)
        {
            return Result.Failure<WaveDto>(WmsErrors.NotFound(
                "wave.not_found",
                "The requested outbound wave was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            wave.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WaveDto>();
        }

        try
        {
            var idempotencyKey = NormalizeRequired(input.IdempotencyKey, 250, nameof(input.IdempotencyKey));
            if (wave.History.Any(history => history.IdempotencyKey == idempotencyKey))
            {
                return Result.Success(MapWave(wave));
            }

            var line = wave.Lines.SingleOrDefault(value => value.SalesOrderLineId == salesOrderLineId);
            if (line is null)
            {
                return Result.Failure<WaveDto>(WmsErrors.NotFound(
                    "wave.line_not_found",
                    "The requested sales-order line is not in this wave."));
            }

            if (line.ReservationId.HasValue || line.WorkCount > 0 ||
                line.Status is not (WaveLineStatus.Selected or WaveLineStatus.Failed))
            {
                return Result.Failure<WaveDto>(WmsErrors.Conflict(
                    "wave.line_already_processed",
                    "A wave line with allocation or execution history cannot be removed."));
            }

            var now = clock.UtcNow.UtcDateTime;
            wave.RemoveLine(salesOrderLineId, input.Reason);
            var history = BeginHistory(
                wave,
                WaveStepType.SelectDemand,
                idempotencyKey,
                now);
            history.Complete(WaveStepStatus.Succeeded, 1, 1, 0, now, detailsJson: JsonSerializer.Serialize(new
            {
                action = "remove",
                salesOrderLineId
            }));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WaveLineRemoved,
                    WmsAuditEntityTypes.WaveLine,
                    line.Id.ToString(CultureInfo.InvariantCulture),
                    wave.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["waveId"] = wave.Id,
                        ["salesOrderLineId"] = salesOrderLineId,
                        ["reason"] = input.Reason
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapWave(wave));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.Validation(
                "wave.line_removal_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.BusinessRule(
                "wave.line_removal_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave line removal failed for {WaveId} and line {SalesOrderLineId}", waveId, salesOrderLineId);
            return Result.Failure<WaveDto>(WmsErrors.FromException(
                exception,
                "wave.line_removal_failed",
                "The wave line could not be removed."));
        }
    }

    public async Task<Result<WaveDto>> CancelAsync(
        int waveId,
        WaveCancellationInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var actorValidation = ValidateActor<WaveDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var wave = await LoadWaveAsync(waveId, cancellationToken);
        if (wave is null)
        {
            return Result.Failure<WaveDto>(WmsErrors.NotFound(
                "wave.not_found",
                "The requested outbound wave was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            wave.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WaveDto>();
        }

        try
        {
            var idempotencyKey = NormalizeRequired(input.IdempotencyKey, 250, nameof(input.IdempotencyKey));
            if (wave.History.Any(history =>
                    history.IdempotencyKey == idempotencyKey &&
                    history.Step == WaveStepType.Cancel))
            {
                return Result.Success(MapWave(wave));
            }

            if (wave.Lines.Any(line =>
                    line.Status == WaveLineStatus.Released ||
                    line.WorkCount > 0))
            {
                return Result.Failure<WaveDto>(WmsErrors.Conflict(
                    "wave.release_immutable",
                    "A released or execution-started wave cannot be cancelled."));
            }

            var now = clock.UtcNow.UtcDateTime;
            foreach (var line in wave.Lines.Where(line =>
                         line.ReservationId.HasValue &&
                         line.Status is not (WaveLineStatus.Removed or WaveLineStatus.Cancelled)))
            {
                var result = await salesOrderAllocationService.CancelAsync(
                    line.SalesOrderId,
                    new SalesOrderAllocationCommand(
                        [line.SalesOrderLineId],
                        ReleaseToWarehouse: false,
                        Replan: false,
                        Simulation: false,
                        $"{idempotencyKey}:line:{line.Id.ToString(CultureInfo.InvariantCulture)}",
                        input.Reason),
                    actorUserId,
                    cancellationToken);
                if (result.IsFailure)
                {
                    return result.ToFailure<WaveDto>();
                }
            }

            wave.Cancel(input.Reason, now);
            var history = BeginHistory(wave, WaveStepType.Cancel, idempotencyKey, now);
            history.Complete(
                WaveStepStatus.Succeeded,
                wave.Lines.Count,
                wave.Lines.Count(line => line.Status is WaveLineStatus.Cancelled or WaveLineStatus.Removed),
                0,
                now,
                detailsJson: JsonSerializer.Serialize(new { reason = input.Reason }));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WaveCancelled,
                    WmsAuditEntityTypes.Wave,
                    wave.Id.ToString(CultureInfo.InvariantCulture),
                    wave.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = wave.Status.ToString(),
                        ["reason"] = input.Reason
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapWave(wave));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.Validation(
                "wave.cancellation_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<WaveDto>(WmsErrors.BusinessRule(
                "wave.cancellation_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Wave cancellation failed for {WaveId}", waveId);
            return Result.Failure<WaveDto>(WmsErrors.FromException(
                exception,
                "wave.cancellation_failed",
                "The outbound wave could not be cancelled."));
        }
    }

    public async Task<Result<WaveScheduledRunDto>> RunScheduledAsync(
        int? warehouseId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actorValidation = ValidateActor<WaveScheduledRunDto>(actorUserId);
        if (actorValidation is not null)
        {
            return actorValidation;
        }

        var templates = context.WaveTemplates
            .AsNoTracking()
            .Where(template => template.IsActive && template.TriggerType == WaveTriggerType.Scheduled);
        if (warehouseId.HasValue)
        {
            templates = templates.Where(template => template.WarehouseId == warehouseId.Value);
        }

        var rows = await templates
            .OrderBy(template => template.WarehouseId)
            .ThenBy(template => template.Id)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var slot = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var createdIds = new List<int>();
        var linesSelected = 0;
        foreach (var template in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCronDue(template.ScheduleCron, now))
            {
                continue;
            }

            var result = await CreateCoreAsync(
                new WaveCreateInput(
                    template.WarehouseId,
                    $"template:{template.Id}:{slot}",
                    WaveTriggerType.Scheduled,
                    template.Priority,
                    null,
                    null,
                    template.RequestedShipDateFrom,
                    template.RequestedShipDateTo,
                    template.CarrierCode,
                    template.CustomerId,
                    template.MinimumPriority,
                    template.SourceType,
                    template.Limit,
                    template.ReleaseToWarehouse,
                    template.Id,
                    template.TemplateKey),
                actorUserId,
                internalExecution: true,
                cancellationToken: cancellationToken);
            if (result.IsFailure && result.ErrorCode == "wave.no_eligible_demand")
            {
                continue;
            }

            if (result.IsFailure)
            {
                return result.ToFailure<WaveScheduledRunDto>();
            }

            createdIds.Add(result.Value.Id);
            linesSelected += result.Value.SelectedLineCount;
        }

        return Result.Success(new WaveScheduledRunDto(
            rows.Count,
            createdIds.Distinct().Count(),
            linesSelected,
            createdIds.Distinct().ToArray()));
    }

    private async Task<List<SelectedDemand>> SelectDemandAsync(
        WaveCreateInput input,
        CancellationToken cancellationToken)
    {
        var occupiedLineIds = (await context.WaveLines
            .AsNoTracking()
            .Where(line =>
                line.Wave.WarehouseId == input.WarehouseId &&
                line.Wave.Status != WaveStatus.Cancelled &&
                line.Wave.Status != WaveStatus.Completed &&
                line.Status != WaveLineStatus.Removed &&
                line.Status != WaveLineStatus.Cancelled)
            .Select(line => line.SalesOrderLineId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var orders = context.SalesOrders
            .AsNoTracking()
            .Include(order => order.Lines)
            .Where(order =>
                order.WarehouseId == input.WarehouseId &&
                EligibleOrderStatuses.Contains(order.Status));
        if (input.RequestedShipDateFrom.HasValue)
        {
            orders = orders.Where(order =>
                order.RequestedShipDate.HasValue &&
                order.RequestedShipDate.Value >= input.RequestedShipDateFrom.Value);
        }

        if (input.RequestedShipDateTo.HasValue)
        {
            orders = orders.Where(order =>
                order.RequestedShipDate.HasValue &&
                order.RequestedShipDate.Value <= input.RequestedShipDateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.CarrierCode))
        {
            var carrierCode = input.CarrierCode.Trim().ToUpperInvariant();
            orders = orders.Where(order => order.DefaultCarrierCodeSnapshot == carrierCode);
        }

        if (input.CustomerId.HasValue)
        {
            orders = orders.Where(order => order.CustomerId == input.CustomerId.Value);
        }

        if (input.MinimumPriority.HasValue)
        {
            orders = orders.Where(order => order.Priority >= input.MinimumPriority.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.SourceType))
        {
            var sourceType = input.SourceType.Trim().ToUpperInvariant();
            orders = orders.Where(order => order.SourceType == sourceType);
        }

        var orderRows = await orders
            .OrderByDescending(order => order.Priority)
            .ThenBy(order => order.RequestedShipDate ?? DateOnly.MaxValue)
            .ThenBy(order => order.Id)
            .ToListAsync(cancellationToken);

        var selected = new List<SelectedDemand>(Math.Min(input.Limit, 1_000));
        foreach (var order in orderRows)
        {
            foreach (var line in order.Lines.OrderBy(value => value.LineNumber).ThenBy(value => value.Id))
            {
                if (selected.Count >= input.Limit || occupiedLineIds.Contains(line.Id))
                {
                    break;
                }

                var quantity = Math.Max(
                    0m,
                    line.OrderedBaseQuantity - line.CancelledBaseQuantity - line.AllocatedBaseQuantity);
                if (quantity <= 0m)
                {
                    continue;
                }

                selected.Add(new SelectedDemand(order, line, quantity));
            }

            if (selected.Count >= input.Limit)
            {
                break;
            }
        }

        return selected;
    }

    private async Task<Wave?> LoadWaveAsync(int waveId, CancellationToken cancellationToken) =>
        await context.Waves
            .Include(wave => wave.Warehouse)
            .Include(wave => wave.Lines)
            .Include(wave => wave.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(wave => wave.Id == waveId, cancellationToken);

    private async Task<Wave?> LoadByCreationKeyAsync(
        int warehouseId,
        string creationKey,
        CancellationToken cancellationToken) =>
        await context.Waves
            .Include(wave => wave.Warehouse)
            .Include(wave => wave.Lines)
            .Include(wave => wave.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                wave => wave.WarehouseId == warehouseId && wave.CreationKey == creationKey,
                cancellationToken);

    private async Task<Warehouse?> LoadActiveWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken) =>
        await context.Warehouses
            .SingleOrDefaultAsync(warehouse => warehouse.Id == warehouseId && warehouse.IsActive, cancellationToken);

    private async Task ValidateCustomerAsync(
        int? customerId,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
        {
            return;
        }

        var exists = await context.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == customerId.Value && customer.IsActive, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException(
                $"Customer {customerId.Value} is not an active customer for wave selection in warehouse {warehouseId}.");
        }
    }

    private WaveProcessingHistory BeginHistory(
        Wave wave,
        WaveStepType step,
        string idempotencyKey,
        DateTime startedAtUtc)
    {
        var attempt = wave.History
            .Where(history => history.Step == step)
            .Select(history => history.Attempt)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var history = new WaveProcessingHistory(
            wave.Id,
            step,
            attempt,
            idempotencyKey,
            startedAtUtc);
        wave.AddHistory(history);
        context.WaveProcessingHistory.Add(history);
        return history;
    }

    private static WaveStatus ResolveFinalStatus(
        WaveLine[] lines,
        bool releaseToWarehouse)
    {
        if (lines.Length == 0)
        {
            return WaveStatus.Completed;
        }

        if (lines.Any(line => line.Status is WaveLineStatus.Selected or WaveLineStatus.Processing))
        {
            return WaveStatus.PartiallyProcessed;
        }

        if (lines.Any(line => line.Status is WaveLineStatus.Failed or WaveLineStatus.Shortage))
        {
            return lines.All(line => line.Status == WaveLineStatus.Failed)
                ? WaveStatus.Failed
                : WaveStatus.PartiallyProcessed;
        }

        if (releaseToWarehouse && lines.All(line => line.Status == WaveLineStatus.Released))
        {
            return WaveStatus.Released;
        }

        return WaveStatus.Completed;
    }

    private static WaveTemplateDto MapTemplate(WaveTemplate template, string? warehouseCode = null) =>
        new(
            template.Id,
            template.WarehouseId,
            template.Warehouse?.Code ?? warehouseCode ?? string.Empty,
            template.TemplateKey,
            template.Name,
            template.TriggerType,
            template.Priority,
            template.Limit,
            template.ReleaseToWarehouse,
            template.ScheduleCron,
            template.RequestedShipDateFrom,
            template.RequestedShipDateTo,
            template.CarrierCode,
            template.CustomerId,
            template.MinimumPriority,
            template.SourceType,
            template.IsActive,
            template.Revision);

    private static WaveDto MapWave(Wave wave) =>
        new(
            wave.Id,
            wave.WarehouseId,
            wave.WaveNumber,
            wave.CreationKey,
            wave.TemplateId,
            wave.TemplateKey,
            wave.TriggerType,
            wave.Priority,
            wave.PlannedStartAtUtc,
            wave.PlannedReleaseAtUtc,
            wave.CapacityLimit,
            wave.Status,
            wave.LastProcessIdempotencyKey,
            wave.LastProcessedAtUtc,
            wave.LastError,
            wave.Revision,
            false,
            wave.Lines
                .OrderBy(line => line.LineNumber)
                .ThenBy(line => line.Id)
                .Select(line => new WaveLineDto(
                    line.Id,
                    line.SalesOrderId,
                    line.SalesOrderLineId,
                    line.LineNumber,
                    line.ItemId,
                    line.ItemSkuSnapshot,
                    line.RequestedQuantity,
                    line.AllocatedQuantity,
                    line.BackorderQuantity,
                    line.ReservationId,
                    line.WorkCount,
                    line.Status,
                    line.ErrorCode,
                    line.ErrorMessage,
                    line.Explanation,
                    line.ProcessedAtUtc,
                    line.Revision))
                .ToArray(),
            wave.History
                .OrderBy(history => history.StartedAtUtc)
                .ThenBy(history => history.Id)
                .Select(history => new WaveStepDto(
                    history.Id,
                    history.Step,
                    history.Attempt,
                    history.IdempotencyKey,
                    history.Status,
                    history.StartedAtUtc,
                    history.CompletedAtUtc,
                    history.ItemsExamined,
                    history.ItemsSucceeded,
                    history.ItemsFailed,
                    history.ErrorMessage,
                    history.DetailsJson))
                .ToArray(),
            wave.Lines.Count,
            wave.Lines.Count(line => line.Status is not (WaveLineStatus.Selected or WaveLineStatus.Removed or WaveLineStatus.Cancelled)),
            wave.Lines.Count(line => line.Status == WaveLineStatus.Failed),
            wave.Lines.Count(line => line.Status == WaveLineStatus.Shortage));

    private static string BuildWaveNumber(string warehouseCode, DateTime nowUtc)
    {
        var candidate = $"WAVE-{warehouseCode}-{nowUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        return candidate.Length <= 80 ? candidate : candidate[..80];
    }

    private static string BuildCriteriaJson(WaveCreateInput input) =>
        JsonSerializer.Serialize(new
        {
            input.WarehouseId,
            input.RequestedShipDateFrom,
            input.RequestedShipDateTo,
            CarrierCode = NormalizeOptional(input.CarrierCode, 50)?.ToUpperInvariant(),
            input.CustomerId,
            input.MinimumPriority,
            SourceType = NormalizeOptional(input.SourceType, 30)?.ToUpperInvariant(),
            input.Limit,
            input.ReleaseToWarehouse,
            input.TemplateId,
            TemplateKey = NormalizeOptional(input.TemplateKey, 80)?.ToUpperInvariant()
        });

    private static void ValidateSelectionDates(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && to.Value < from.Value)
        {
            throw new ArgumentException(
                "The requested ship-date end cannot be earlier than the start.",
                nameof(to));
        }
    }

    private static Result<T>? ValidateActor<T>(string actorUserId)
    {
        return string.IsNullOrWhiteSpace(actorUserId)
            ? Result.Failure<T>(WmsErrors.Validation(
                "wave.actor_required",
                "An authenticated actor is required."))
            : null;
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, nameof(value));

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static bool IsCronDue(string? expression, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        var fields = expression
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 5)
        {
            return false;
        }

        var minuteMatches = MatchesCronField(fields[0], nowUtc.Minute, 0, 59);
        var hourMatches = MatchesCronField(fields[1], nowUtc.Hour, 0, 23);
        var dayOfMonthMatches = MatchesCronField(fields[2], nowUtc.Day, 1, 31);
        var monthMatches = MatchesCronField(fields[3], nowUtc.Month, 1, 12);
        var dayOfWeek = nowUtc.DayOfWeek == DayOfWeek.Sunday
            ? 0
            : (int)nowUtc.DayOfWeek;
        var dayOfWeekMatches = MatchesCronField(fields[4], dayOfWeek, 0, 7, allowSundaySeven: true);
        if (!minuteMatches || !hourMatches || !monthMatches)
        {
            return false;
        }

        var dayOfMonthWildcard = IsCronWildcard(fields[2]);
        var dayOfWeekWildcard = IsCronWildcard(fields[4]);
        return dayOfMonthWildcard && dayOfWeekWildcard
            ? true
            : dayOfMonthWildcard
                ? dayOfWeekMatches
                : dayOfWeekWildcard
                    ? dayOfMonthMatches
                    : dayOfMonthMatches || dayOfWeekMatches;
    }

    private static bool MatchesCronField(
        string field,
        int value,
        int minimum,
        int maximum,
        bool allowSundaySeven = false)
    {
        foreach (var rawPart in field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart;
            var step = 1;
            var stepSeparator = part.IndexOf('/');
            if (stepSeparator >= 0)
            {
                if (!int.TryParse(part[(stepSeparator + 1)..], CultureInfo.InvariantCulture, out step) || step <= 0)
                {
                    return false;
                }

                part = part[..stepSeparator];
            }

            var start = minimum;
            var end = maximum;
            if (part is not "" and not "*")
            {
                var range = part.Split('-', StringSplitOptions.TrimEntries);
                if (range.Length == 1)
                {
                    if (!int.TryParse(range[0], CultureInfo.InvariantCulture, out start))
                    {
                        return false;
                    }

                    end = start;
                }
                else if (range.Length == 2 &&
                         int.TryParse(range[0], CultureInfo.InvariantCulture, out start) &&
                         int.TryParse(range[1], CultureInfo.InvariantCulture, out end))
                {
                }
                else
                {
                    return false;
                }
            }

            if (start < minimum || end > maximum || start > end)
            {
                if (!(allowSundaySeven && start == 7 && end == 7 && value == 0))
                {
                    return false;
                }
            }

            for (var candidate = start; candidate <= end; candidate += step)
            {
                if (candidate == value || (allowSundaySeven && candidate == 7 && value == 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCronWildcard(string field) =>
        field.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(part => part is "*" || part.StartsWith("*/", StringComparison.Ordinal));

    private sealed record SelectedDemand(
        SalesOrder Order,
        SalesOrderLine Line,
        decimal Quantity);
}
