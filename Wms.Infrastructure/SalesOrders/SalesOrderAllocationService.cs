using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.SalesOrders;

/// <summary>
/// Coordinates sales-order demand, reservation decisions, and pick-work
/// release. Reservations remain the source of truth for availability while
/// order lines retain a traceable demand summary.
/// </summary>
public sealed class SalesOrderAllocationService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IInventoryReservationService reservationService,
    IWarehouseWorkService warehouseWorkService,
    IAuditWriter auditWriter,
    ILogger<SalesOrderAllocationService> logger) : ISalesOrderAllocationService
{
    private const int MaximumInventoryContentionAttempts = 5;

    public async Task<Result<SalesOrderAllocationResultDto>> GetAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAuthorizedOrderAsync(
            salesOrderId,
            cancellationToken,
            WmsPermissions.InventoryRead);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        var reservations = await LoadReservationResultsAsync(
            loaded.Value,
            cancellationToken);
        return Result.Success(await BuildResultAsync(
            loaded.Value,
            reservations,
            isSimulation: false,
            simulations: null,
            cancellationToken));
    }

    public async Task<Result<SalesOrderAllocationResultDto>> AllocateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Simulation)
        {
            return await SimulateAsync(
                salesOrderId,
                command with { Simulation = false },
                userId,
                cancellationToken);
        }

        return await AllocateCoreAsync(
            salesOrderId,
            command,
            userId,
            cancellationToken);
    }

    public async Task<Result<SalesOrderAllocationResultDto>> SimulateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await LoadAuthorizedOrderAsync(salesOrderId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        var order = loaded.Value;
        var validation = ValidateLineSelection(command.LineIds);
        if (validation is not null)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(validation);
        }

        var lines = SelectLines(order, command.LineIds);
        if (lines.IsFailure)
        {
            return lines.ToFailure<SalesOrderAllocationResultDto>();
        }

        try
        {
            EnsureOrderCanAllocate(order);
            var simulations = new Dictionary<int, InventoryReservationSimulationResult>();
            foreach (var line in lines.Value)
            {
                var quantity = GetDemandQuantity(line);
                if (quantity <= 0m)
                {
                    continue;
                }

                var simulation = await reservationService.SimulateAsync(
                    CreateReservationRequest(
                        order,
                        line,
                        quantity,
                        command,
                        userId),
                    cancellationToken);
                simulations[line.Id] = simulation;
            }

            return Result.Success(await BuildSimulationResultAsync(
                order,
                simulations,
                cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Validation(
                "allocation.simulation_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.simulation_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sales-order allocation simulation failed for {SalesOrderId}",
                salesOrderId);
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.FromException(
                exception,
                "allocation.simulation_failed",
                "Allocation simulation could not be completed."));
        }
    }

    public Task<Result<SalesOrderAllocationResultDto>> ReleaseAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default) =>
        ReleaseCoreAsync(salesOrderId, command, userId, cancellationToken);

    public async Task<Result<SalesOrderAllocationResultDto>> ReallocateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await LoadAuthorizedOrderAsync(salesOrderId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        var work = await LoadOrderWorkAsync(loaded.Value, cancellationToken);
        if (work.Any(value => value.Status != WarehouseWorkStatus.Cancelled))
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Conflict(
                "allocation.work_already_released",
                "Released pick work must be unreleased before the order can be replanned."));
        }

        return await AllocateCoreAsync(
            salesOrderId,
            command with { Replan = true },
            userId,
            cancellationToken);
    }

    public Task<Result<SalesOrderAllocationResultDto>> UnreleaseAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default) =>
        CancelAllocationCoreAsync(
            salesOrderId,
            command,
            userId,
            cancelReservations: false,
            cancellationToken);

    public Task<Result<SalesOrderAllocationResultDto>> CancelAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default) =>
        CancelAllocationCoreAsync(
            salesOrderId,
            command,
            userId,
            cancelReservations: true,
            cancellationToken);

    public async Task<Result<SalesOrderAllocationBatchResultDto>> AllocateBatchAsync(
        SalesOrderAllocationBatchCommand command,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SalesOrderIds is null || command.SalesOrderIds.Count == 0)
        {
            return Result.Failure<SalesOrderAllocationBatchResultDto>(WmsErrors.Validation(
                "allocation.batch_orders_required",
                "At least one sales order is required for a batch allocation."));
        }

        if (command.SalesOrderIds.Any(id => id <= 0) ||
            command.SalesOrderIds.Distinct().Count() != command.SalesOrderIds.Count)
        {
            return Result.Failure<SalesOrderAllocationBatchResultDto>(WmsErrors.Validation(
                "allocation.batch_orders_invalid",
                "Batch sales-order IDs must be positive and unique."));
        }

        var keyValidation = ValidateIdempotencyKey(command.IdempotencyKey);
        if (keyValidation is not null)
        {
            return Result.Failure<SalesOrderAllocationBatchResultDto>(keyValidation);
        }

        var orders = await context.SalesOrders
            .AsNoTracking()
            .Where(order => command.SalesOrderIds.Contains(order.Id))
            .OrderByDescending(order => order.Priority)
            .ThenBy(order => order.RequestedShipDate ?? DateOnly.MaxValue)
            .ThenBy(order => order.Id)
            .Select(order => new { order.Id, order.DocumentNumber })
            .ToListAsync(cancellationToken);
        var items = new List<SalesOrderAllocationBatchItemDto>(command.SalesOrderIds.Count);
        foreach (var requestedId in command.SalesOrderIds)
        {
            var order = orders.SingleOrDefault(value => value.Id == requestedId);
            if (order is null)
            {
                items.Add(new SalesOrderAllocationBatchItemDto(
                    requestedId,
                    null,
                    null,
                    "sales_order.not_found",
                    "The sales order was not found."));
                continue;
            }

            var result = await AllocateAsync(
                requestedId,
                new SalesOrderAllocationCommand(
                    ReleaseToWarehouse: command.ReleaseToWarehouse,
                    Replan: command.Replan,
                    IdempotencyKey: $"{command.IdempotencyKey.Trim()}:{requestedId.ToString(CultureInfo.InvariantCulture)}",
                    Reason: command.Reason),
                userId,
                cancellationToken);
            items.Add(result.IsSuccess
                ? new SalesOrderAllocationBatchItemDto(
                    requestedId,
                    order.DocumentNumber,
                    result.Value,
                    null,
                    null)
                : new SalesOrderAllocationBatchItemDto(
                    requestedId,
                    order.DocumentNumber,
                    null,
                    result.ErrorCode,
                    result.Error));
        }

        var successful = items
            .Where(item => item.Result is not null)
            .Select(item => item.Result!)
            .ToArray();
        return Result.Success(new SalesOrderAllocationBatchResultDto(
            items,
            items.Count,
            successful.Count(result => result.IsFullyAllocated),
            successful.Count(result => !result.IsFullyAllocated),
            items.Count(item => item.Result is null)));
    }

    private async Task<Result<SalesOrderAllocationResultDto>> AllocateCoreAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumInventoryContentionAttempts; attempt++)
        {
            try
            {
                return await AllocateCoreAttemptAsync(
                    salesOrderId,
                    command,
                    userId,
                    cancellationToken);
            }
            catch (RetryableInventoryContentionException exception)
            {
                context.ChangeTracker.Clear();
                if (attempt == MaximumInventoryContentionAttempts)
                {
                    logger.LogWarning(
                        exception.InnerException,
                        "Sales-order allocation {SalesOrderId} exhausted {AttemptCount} retries after inventory balance contention",
                        salesOrderId,
                        attempt);
                    return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Concurrency(
                        "allocation.concurrency_conflict",
                        "Inventory changed repeatedly during allocation. Retry the allocation."));
                }

                var backoffMilliseconds = Math.Min(40, 5 * (1 << (attempt - 1))) + Random.Shared.Next(0, 11);
                await Task.Delay(TimeSpan.FromMilliseconds(backoffMilliseconds), cancellationToken);
            }
        }

        throw new InvalidOperationException("The allocation retry loop ended unexpectedly.");
    }

    private async Task<Result<SalesOrderAllocationResultDto>> AllocateCoreAttemptAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken)
    {
        var keyValidation = ValidateIdempotencyKey(command.IdempotencyKey);
        if (keyValidation is not null)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(keyValidation);
        }

        var lineValidation = ValidateLineSelection(command.LineIds);
        if (lineValidation is not null)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(lineValidation);
        }

        var loaded = await LoadAuthorizedOrderAsync(salesOrderId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        var order = loaded.Value;
        var lines = SelectLines(order, command.LineIds);
        if (lines.IsFailure)
        {
            return lines.ToFailure<SalesOrderAllocationResultDto>();
        }

        try
        {
            EnsureOrderCanAllocate(order);
            if (command.Replan)
            {
                var existingWork = await LoadOrderWorkAsync(order, cancellationToken);
                if (existingWork.Any(value => value.Status != WarehouseWorkStatus.Cancelled))
                {
                    return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Conflict(
                        "allocation.work_already_released",
                        "Released pick work must be unreleased before the order can be replanned."));
                }
            }

            var existingReservations = await LoadReservationEntitiesAsync(
                order,
                cancellationToken);
            var crossDockCommittedByLine = await LoadCrossDockCommittedQuantitiesAsync(
                order,
                cancellationToken);
            foreach (var line in lines.Value)
            {
                var quantity = existingReservations.TryGetValue(line.Id, out var existingReservation)
                    ? existingReservation.RequestedQuantity
                    : Math.Min(
                        line.BackorderBaseQuantity,
                        Math.Max(
                            0m,
                            GetDemandQuantity(line) - crossDockCommittedByLine.GetValueOrDefault(line.Id)));
                if (quantity <= 0m)
                {
                    continue;
                }

                var request = CreateReservationRequest(
                    order,
                    line,
                    quantity,
                    command,
                    userId);
                try
                {
                    if (command.Replan && existingReservations.TryGetValue(line.Id, out existingReservation))
                    {
                        await reservationService.ReallocateAsync(
                            new InventoryReservationMutationRequest(
                                existingReservation.Id,
                                null,
                                userId,
                                request.CorrelationId,
                                command.Reason ?? "sales order allocation replanned"),
                            cancellationToken);
                    }
                    else
                    {
                        await reservationService.ReserveAsync(
                            request,
                            cancellationToken);
                    }
                }
                catch (ConcurrencyConflictException exception) when (
                    exception.ResourceType == nameof(InventoryBalance))
                {
                    throw new RetryableInventoryContentionException(exception);
                }
                catch (DbUpdateConcurrencyException exception) when (
                    exception.Entries.Any(entry => entry.Entity is InventoryBalance))
                {
                    throw new RetryableInventoryContentionException(exception);
                }

            }

            var allReservations = await LoadReservationResultsAsync(order, cancellationToken);
            foreach (var line in order.Lines)
            {
                if (allReservations.TryGetValue(line.Id, out var reservation))
                {
                    ReconcileLineAllocation(
                        line,
                        reservation,
                        crossDockCommittedByLine.GetValueOrDefault(line.Id));
                }
            }

            if (order.Status != SalesOrderStatus.Allocating &&
                !(order.Status == SalesOrderStatus.Released && !command.Replan))
            {
                order.SetAllocationStatus(SalesOrderStatus.Allocating);
            }

            await context.SaveChangesAsync(cancellationToken);
            if (command.ReleaseToWarehouse)
            {
                var release = await ReleaseLoadedAsync(
                    order,
                    command,
                    userId,
                    allReservations,
                    cancellationToken);
                if (release.IsFailure)
                {
                    return release;
                }

                return release;
            }

            if (order.Status != SalesOrderStatus.Released)
            {
                SetAllocationSummaryStatus(order);
            }
            await auditWriter.RecordAsync(
                CreateAllocationAudit(
                    order,
                    "allocate",
                    allReservations.Values.Sum(value => value.AllocatedQuantity),
                    allReservations.Values.Sum(value => value.BackorderQuantity),
                    userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(await BuildResultAsync(
                order,
                allReservations,
                isSimulation: false,
                simulations: null,
                cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RetryableInventoryContentionException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Concurrency(
                "allocation.concurrency_conflict",
                "Inventory or order data changed while allocation was running. Retry the allocation."));
        }
        catch (ConcurrencyConflictException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Concurrency(
                exception.Code,
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Validation(
                "allocation.invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sales-order allocation failed for {SalesOrderId}",
                salesOrderId);
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.FromException(
                exception,
                "allocation.failed",
                "Sales-order allocation could not be completed."));
        }
    }

    private async Task<Result<SalesOrderAllocationResultDto>> ReleaseCoreAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken)
    {
        var keyValidation = ValidateIdempotencyKey(command.IdempotencyKey);
        if (keyValidation is not null)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(keyValidation);
        }

        var loaded = await LoadAuthorizedOrderAsync(salesOrderId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        try
        {
            EnsureOrderCanAllocate(loaded.Value);
            var reservations = await LoadReservationResultsAsync(
                loaded.Value,
                cancellationToken);
            return await ReleaseLoadedAsync(
                loaded.Value,
                command,
                userId,
                reservations,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Validation(
                "allocation.release_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.release_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sales-order release failed for {SalesOrderId}",
                salesOrderId);
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.FromException(
                exception,
                "allocation.release_failed",
                "Allocated demand could not be released to warehouse work."));
        }
    }

    private async Task<Result<SalesOrderAllocationResultDto>> ReleaseLoadedAsync(
        SalesOrder order,
        SalesOrderAllocationCommand command,
        string userId,
        Dictionary<int, InventoryReservationResult> reservations,
        CancellationToken cancellationToken)
    {
        if (reservations.Count == 0)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.nothing_to_release",
                "The sales order has no reservation-backed demand to release."));
        }

        var selectedLineIds = NormalizeLineIds(command.LineIds, order);
        var selectedLines = order.Lines
            .Where(line => selectedLineIds is null || selectedLineIds.Contains(line.Id))
            .ToArray();
        var selectedReservations = reservations
            .Where(pair => selectedLineIds is null || selectedLineIds.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (selectedReservations.Count == 0)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.nothing_to_release",
                "The selected sales-order lines have no reservation-backed demand to release."));
        }

        var backorder = selectedLines.Sum(line => line.BackorderBaseQuantity);
        if (backorder > 0m && !order.AllowPartialShipmentSnapshot)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.partial_not_allowed",
                "The order has a backorder and does not allow partial shipment."));
        }

        foreach (var line in selectedLines)
        {
            if (!selectedReservations.TryGetValue(line.Id, out var reservation))
            {
                continue;
            }

            foreach (var allocation in reservation.Allocations.Where(value => value.RemainingQuantity > 0m))
            {
                var workResult = await warehouseWorkService.CreateForAllocationAsync(
                    new WarehouseWorkInput(
                        CreationKey: BuildWorkCreationKey(
                            order,
                            line,
                            reservation,
                            allocation),
                        Type: WarehouseWorkType.Pick,
                        WarehouseId: order.WarehouseId,
                        SourceEntityType: "SalesOrderLine",
                        SourceEntityId: line.Id.ToString(CultureInfo.InvariantCulture),
                        Priority: order.Priority,
                        SourceLineReference: $"{order.DocumentNumber}:{line.LineNumber}",
                        QueueCode: "PICK",
                        DueAtUtc: ToDueAtUtc(order.RequestedShipDate),
                        Notes: command.Reason ?? "Released from sales-order allocation",
                        MakeAvailable: true,
                        Lines:
                        [
                            new WarehouseWorkLineInput(
                                Sequence: 1,
                                WarehouseId: order.WarehouseId,
                                ItemId: line.ItemId,
                                PlannedQuantity: allocation.RemainingQuantity,
                                BaseUnitOfMeasure: allocation.BaseUnitOfMeasure,
                                SourceLocationId: allocation.LocationId,
                                LotId: allocation.LotId,
                                SerialNumberId: allocation.SerialNumberId,
                                SerialNumber: allocation.SerialNumber,
                                LicensePlateId: allocation.LicensePlateId,
                                InventoryStatusId: allocation.InventoryStatusId,
                                SourceReference: $"reservation:{reservation.ReservationId}:allocation:{allocation.AllocationId}",
                                ReservationId: reservation.ReservationId,
                                ReservationAllocationId: allocation.AllocationId)
                        ],
                        AllocationStrategyPolicyId: reservation.AllocationStrategyPolicyId,
                        AllocationStrategyKey: reservation.AllocationStrategyKey,
                        AllocationStrategy: reservation.AllocationStrategy,
                        AllocationStrategyRevision: reservation.AllocationStrategyRevision),
                    userId,
                    cancellationToken);
                if (workResult.IsFailure)
                {
                    return workResult.ToFailure<SalesOrderAllocationResultDto>();
                }
            }
        }

        var allDemandLineIds = order.Lines
            .Where(line => GetDemandQuantity(line) > 0m)
            .Select(line => line.Id)
            .ToHashSet();
        if (selectedLineIds is null || allDemandLineIds.IsSubsetOf(selectedLineIds))
        {
            if (order.Status != SalesOrderStatus.Released)
            {
                order.SetAllocationStatus(SalesOrderStatus.Released);
            }
        }
        else if (order.Status != SalesOrderStatus.PartiallyAllocated)
        {
            order.SetAllocationStatus(SalesOrderStatus.PartiallyAllocated);
        }

        await auditWriter.RecordAsync(
            CreateAllocationAudit(
                order,
                "release",
                selectedReservations.Values.Sum(value => value.AllocatedQuantity),
                selectedReservations.Values.Sum(value => value.BackorderQuantity),
                userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        var refreshed = await LoadReservationResultsAsync(order, cancellationToken);
        return Result.Success(await BuildResultAsync(
            order,
            refreshed,
            isSimulation: false,
            simulations: null,
            cancellationToken));
    }

    private async Task<Result<SalesOrderAllocationResultDto>> CancelAllocationCoreAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        bool cancelReservations,
        CancellationToken cancellationToken)
    {
        var keyValidation = ValidateIdempotencyKey(command.IdempotencyKey);
        if (keyValidation is not null)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(keyValidation);
        }

        var loaded = await LoadAuthorizedOrderAsync(salesOrderId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<SalesOrderAllocationResultDto>();
        }

        try
        {
            var order = loaded.Value;
            var reservations = await LoadReservationResultsAsync(order, cancellationToken);
            var crossDockCommittedByLine = await LoadCrossDockCommittedQuantitiesAsync(
                order,
                cancellationToken);
            var selectedLineIds = NormalizeLineIds(command.LineIds, order);
            var selectedReservations = reservations
                .Where(pair => selectedLineIds is null || selectedLineIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (selectedReservations.Count == 0)
            {
                return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                    "allocation.nothing_to_cancel",
                    "The selected sales-order lines have no reservation-backed demand."));
            }
            if (selectedReservations.Values.Any(value => value.ConsumedQuantity > 0m))
            {
                return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Conflict(
                    "allocation.execution_started",
                    "Allocation cannot be cancelled after picking has consumed reserved inventory."));
            }

            var work = await LoadOrderWorkAsync(order, cancellationToken);
            if (selectedLineIds is not null)
            {
                work = work
                    .Where(value =>
                        int.TryParse(value.SourceEntityId, out var lineId) &&
                        selectedLineIds.Contains(lineId))
                    .ToArray();
            }
            if (work.Any(value => value.Status is WarehouseWorkStatus.InProgress or
                WarehouseWorkStatus.Paused or
                WarehouseWorkStatus.Exception or
                WarehouseWorkStatus.Completed))
            {
                return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Conflict(
                    "allocation.work_execution_started",
                    "Pick work must be stopped before allocation can be unreleased or cancelled."));
            }

            if (work.Any(value => value.SourceLineReference?.StartsWith(
                    "CROSSDOCK:",
                    StringComparison.OrdinalIgnoreCase) == true))
            {
                return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Conflict(
                    "allocation.crossdock_work_managed_separately",
                    "Cross-dock pick work must be managed through its cross-dock plan."));
            }

            foreach (var workItem in work.Where(value => !value.IsTerminal))
            {
                var workResult = await warehouseWorkService.CancelForAllocationAsync(
                    workItem.Id,
                    new WarehouseWorkCommandInput(
                        $"{command.IdempotencyKey.Trim()}:work:{workItem.Id.ToString(CultureInfo.InvariantCulture)}",
                        command.Reason ?? "Sales-order allocation was cancelled."),
                    userId,
                    cancellationToken);
                if (workResult.IsFailure)
                {
                    return workResult.ToFailure<SalesOrderAllocationResultDto>();
                }
            }

            foreach (var reservation in selectedReservations.Values)
            {
                if (reservation.ActiveQuantity <= 0m)
                {
                    continue;
                }

                var mutation = new InventoryReservationMutationRequest(
                    reservation.ReservationId,
                    null,
                    userId,
                    $"{command.IdempotencyKey.Trim()}:reservation:{reservation.ReservationId.ToString(CultureInfo.InvariantCulture)}",
                    command.Reason ?? (cancelReservations
                        ? "Sales-order allocation cancelled."
                        : "Sales-order allocation unreleased."));
                var result = cancelReservations
                    ? await reservationService.CancelAsync(mutation, cancellationToken)
                    : await reservationService.ReleaseAsync(mutation, cancellationToken);
            }

            var refreshed = await LoadReservationResultsAsync(order, cancellationToken);
            foreach (var line in order.Lines.Where(line =>
                         selectedLineIds is null || selectedLineIds.Contains(line.Id)))
            {
                if (refreshed.TryGetValue(line.Id, out var reservation))
                {
                    ReconcileLineAllocation(
                        line,
                        reservation,
                        crossDockCommittedByLine.GetValueOrDefault(line.Id));
                }
            }

            var allDemandLineIds = order.Lines
                .Where(line => GetDemandQuantity(line) > 0m)
                .Select(line => line.Id)
                .ToHashSet();
            if ((selectedLineIds is null || allDemandLineIds.IsSubsetOf(selectedLineIds)) &&
                order.Status is SalesOrderStatus.Allocating or
                SalesOrderStatus.PartiallyAllocated or
                SalesOrderStatus.Released or
                SalesOrderStatus.Exception)
            {
                order.RestoreConfirmedAfterAllocationCancellation();
            }
            else if (order.Status is SalesOrderStatus.Allocating or
                     SalesOrderStatus.Released or
                     SalesOrderStatus.Exception)
            {
                order.SetAllocationStatus(SalesOrderStatus.PartiallyAllocated);
            }

            await auditWriter.RecordAsync(
                CreateAllocationAudit(
                    order,
                    cancelReservations ? "cancel" : "unrelease",
                    selectedLineIds is null
                        ? refreshed.Values.Sum(value => value.AllocatedQuantity)
                        : refreshed.Where(pair => selectedLineIds.Contains(pair.Key))
                            .Sum(pair => pair.Value.AllocatedQuantity),
                    selectedLineIds is null
                        ? refreshed.Values.Sum(value => value.BackorderQuantity)
                        : refreshed.Where(pair => selectedLineIds.Contains(pair.Key))
                            .Sum(pair => pair.Value.BackorderQuantity),
                    userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(await BuildResultAsync(
                order,
                refreshed,
                isSimulation: false,
                simulations: null,
                cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Concurrency(
                "allocation.concurrency_conflict",
                "Inventory, work, or order data changed while allocation was being cancelled."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.Validation(
                "allocation.cancellation_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                "allocation.cancellation_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Sales-order allocation cancellation failed for {SalesOrderId}",
                salesOrderId);
            return Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.FromException(
                exception,
                "allocation.cancellation_failed",
                "Sales-order allocation cancellation could not be completed."));
        }
    }

    private async Task<Result<SalesOrder>> LoadAuthorizedOrderAsync(
        int salesOrderId,
        CancellationToken cancellationToken,
        string permission = WmsPermissions.AllocationManage)
    {
        if (salesOrderId <= 0)
        {
            return Result.Failure<SalesOrder>(WmsErrors.Validation(
                "sales_order.id_invalid",
                "A valid sales-order ID is required."));
        }

        var order = await context.SalesOrders
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == salesOrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<SalesOrder>(WmsErrors.NotFound(
                "sales_order.not_found",
                "The sales order was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            order.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<SalesOrder>()
            : Result.Success(order);
    }

    private async Task<Dictionary<int, InventoryReservation>> LoadReservationEntitiesAsync(
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        var demandId = order.Id.ToString(CultureInfo.InvariantCulture);
        var reservations = await context.InventoryReservations
            .Where(value => value.WarehouseId == order.WarehouseId &&
                           value.DemandType == "SalesOrderLine" &&
                           value.DemandId == demandId &&
                           value.DemandLine.HasValue)
            .ToListAsync(cancellationToken);
        return reservations
            .Where(value => value.DemandLine.HasValue)
            .ToDictionary(value => value.DemandLine!.Value);
    }

    private async Task<Dictionary<int, InventoryReservationResult>> LoadReservationResultsAsync(
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, InventoryReservationResult>();
        var demandId = order.Id.ToString(CultureInfo.InvariantCulture);
        foreach (var line in order.Lines)
        {
            var reservation = await reservationService.GetByDemandAsync(
                "SalesOrderLine",
                demandId,
                line.Id,
                order.WarehouseId,
                cancellationToken);
            if (reservation is not null)
            {
                results[line.Id] = reservation;
            }
        }

        return results;
    }

    private async Task<Dictionary<int, decimal>> LoadCrossDockCommittedQuantitiesAsync(
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        var orderLineIds = order.Lines.Select(line => line.Id).ToArray();
        if (orderLineIds.Length == 0)
        {
            return [];
        }

        var planLines = await context.CrossDockPlanLines
            .AsNoTracking()
            .Where(line => orderLineIds.Contains(line.SalesOrderLineId))
            .Select(line => new
            {
                line.CrossDockPlanId,
                line.Sequence,
                line.SalesOrderLineId
            })
            .ToArrayAsync(cancellationToken);
        if (planLines.Length == 0)
        {
            return [];
        }

        var planIds = planLines
            .Select(line => line.CrossDockPlanId.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reservations = await context.InventoryReservations
            .AsNoTracking()
            .Where(reservation => reservation.DemandType == "CrossDockPlanLine" &&
                                  planIds.Contains(reservation.DemandId) &&
                                  reservation.DemandLine.HasValue)
            .Select(reservation => new
            {
                reservation.Id,
                reservation.DemandId,
                DemandLine = reservation.DemandLine!.Value
            })
            .ToArrayAsync(cancellationToken);
        if (reservations.Length == 0)
        {
            return [];
        }

        var reservationIds = reservations.Select(reservation => reservation.Id).ToArray();
        var committedByReservation = await context.InventoryReservationAllocations
            .AsNoTracking()
            .Where(allocation => reservationIds.Contains(allocation.ReservationId))
            .GroupBy(allocation => allocation.ReservationId)
            .Select(group => new
            {
                ReservationId = group.Key,
                Quantity = group.Sum(allocation => allocation.AllocatedQuantity - allocation.ReleasedQuantity)
            })
            .ToDictionaryAsync(value => value.ReservationId, value => value.Quantity, cancellationToken);
        var salesOrderLineByDemand = planLines.ToDictionary(
            line => (
                DemandId: line.CrossDockPlanId.ToString(CultureInfo.InvariantCulture),
                DemandLine: line.Sequence),
            line => line.SalesOrderLineId);
        var result = new Dictionary<int, decimal>();
        foreach (var reservation in reservations)
        {
            if (!salesOrderLineByDemand.TryGetValue(
                    (reservation.DemandId, reservation.DemandLine),
                    out var lineId))
            {
                continue;
            }

            result[lineId] = result.GetValueOrDefault(lineId) +
                             committedByReservation.GetValueOrDefault(reservation.Id);
        }

        return result;
    }

    private async Task<IReadOnlyList<WarehouseWorkEntity>> LoadOrderWorkAsync(
        SalesOrder order,
        CancellationToken cancellationToken)
    {
        var lineIds = order.Lines
            .Select(line => line.Id.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return await context.WarehouseWorks
            .Include(value => value.Lines)
            .Where(value => value.WarehouseId == order.WarehouseId &&
                           value.Type == WarehouseWorkType.Pick &&
                           value.SourceEntityType == "SalesOrderLine" &&
                           lineIds.Contains(value.SourceEntityId))
            .OrderBy(value => value.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<SalesOrderAllocationResultDto> BuildResultAsync(
        SalesOrder order,
        IReadOnlyDictionary<int, InventoryReservationResult> reservations,
        bool isSimulation,
        IReadOnlyDictionary<int, InventoryReservationSimulationResult>? simulations,
        CancellationToken cancellationToken)
    {
        var work = await LoadOrderWorkAsync(order, cancellationToken);
        var lines = order.Lines
            .OrderBy(line => line.LineNumber)
            .Select(line => MapLine(line, reservations, simulations))
            .ToArray();
        return new SalesOrderAllocationResultDto(
            order.Id,
            order.DocumentNumber,
            order.WarehouseId,
            order.Status,
            isSimulation,
            lines.All(line => line.BackorderBaseQuantity == 0m),
            lines.Sum(line => line.OrderedBaseQuantity),
            lines.Sum(line => line.AllocatedBaseQuantity),
            lines.Sum(line => line.BackorderBaseQuantity),
            lines,
            work.Select(MapWork).ToArray(),
            isSimulation
                ? BuildSimulationExplanation(lines)
                : BuildAllocationExplanation(order, lines));
    }

    private async Task<SalesOrderAllocationResultDto> BuildSimulationResultAsync(
        SalesOrder order,
        IReadOnlyDictionary<int, InventoryReservationSimulationResult> simulations,
        CancellationToken cancellationToken) =>
        await BuildResultAsync(
            order,
            new Dictionary<int, InventoryReservationResult>(),
            isSimulation: true,
            simulations,
            cancellationToken);

    private static SalesOrderAllocationLineDto MapLine(
        SalesOrderLine line,
        IReadOnlyDictionary<int, InventoryReservationResult> reservations,
        IReadOnlyDictionary<int, InventoryReservationSimulationResult>? simulations = null)
    {
        if (simulations is not null && simulations.TryGetValue(line.Id, out var simulation))
        {
            var selected = simulation.Candidates
                .Where(candidate => candidate.Selected)
                .Select(candidate => new SalesOrderAllocationSelectionDto(
                    0,
                    candidate.LocationId,
                    candidate.LotId,
                    candidate.SerialNumberId,
                    candidate.SerialNumber,
                    candidate.LicensePlateId,
                    candidate.InventoryStatusId,
                    candidate.ProposedQuantity,
                    candidate.Decision,
                    candidate.Reason))
                .ToArray();
            return new SalesOrderAllocationLineDto(
                line.Id,
                line.LineNumber,
                line.ItemId,
                line.ItemSkuSnapshot,
                line.OrderedBaseQuantity,
                simulation.ProposedAllocatedQuantity,
                simulation.BackorderQuantity,
                line.CancelledBaseQuantity,
                null,
                simulation.ProjectedStatus.ToString(),
                simulation.ProposedAllocatedQuantity,
                0m,
                simulation.Explanation,
                selected);
        }

        if (simulations is not null)
        {
            return new SalesOrderAllocationLineDto(
                line.Id,
                line.LineNumber,
                line.ItemId,
                line.ItemSkuSnapshot,
                line.OrderedBaseQuantity,
                line.AllocatedBaseQuantity,
                line.BackorderBaseQuantity,
                line.CancelledBaseQuantity,
                null,
                null,
                0m,
                0m,
                "This line was not included in the simulation.",
                []);
        }

        if (!reservations.TryGetValue(line.Id, out var reservation))
        {
            return new SalesOrderAllocationLineDto(
                line.Id,
                line.LineNumber,
                line.ItemId,
                line.ItemSkuSnapshot,
                line.OrderedBaseQuantity,
                line.AllocatedBaseQuantity,
                line.BackorderBaseQuantity,
                line.CancelledBaseQuantity,
                null,
                null,
                0m,
                0m,
                "No reservation exists for this demand line.",
                []);
        }

        var selections = reservation.Allocations
            .Select(allocation => new SalesOrderAllocationSelectionDto(
                allocation.AllocationId,
                allocation.LocationId,
                allocation.LotId,
                allocation.SerialNumberId,
                allocation.SerialNumber,
                allocation.LicensePlateId,
                allocation.InventoryStatusId,
                allocation.RemainingQuantity,
                allocation.Status.ToString(),
                allocation.Reason))
            .ToArray();
        var explanation = reservation.BackorderQuantity > 0m
            ? "Eligible inventory was insufficient; the remaining demand is backordered."
            : reservation.Allocations.Count == 0
                ? "No eligible allocatable inventory matched the configured rules."
                : "Reservation allocations are traceable to the selected inventory dimensions.";
        return new SalesOrderAllocationLineDto(
            line.Id,
            line.LineNumber,
            line.ItemId,
            line.ItemSkuSnapshot,
            line.OrderedBaseQuantity,
            line.AllocatedBaseQuantity,
            line.BackorderBaseQuantity,
            line.CancelledBaseQuantity,
            reservation.ReservationId,
            reservation.Status.ToString(),
            reservation.ActiveQuantity,
            reservation.ConsumedQuantity,
            explanation,
            selections);
    }

    private static string BuildSimulationExplanation(
        IReadOnlyList<SalesOrderAllocationLineDto> lines) =>
        lines.Sum(line => line.BackorderBaseQuantity) == 0m
            ? "Simulation found eligible inventory for all selected demand. No reservation or work was created."
            : "Simulation found a shortage; the unallocated quantity is shown as backorder without changing inventory.";

    private static string BuildAllocationExplanation(
        SalesOrder order,
        IReadOnlyList<SalesOrderAllocationLineDto> lines) =>
        lines.Sum(line => line.BackorderBaseQuantity) > 0m
            ? order.AllowPartialShipmentSnapshot
                ? "Allocation completed with reservation-backed partial demand and backorder quantities."
                : "Allocation completed with a shortage; release remains blocked because partial shipment is disabled."
            : "All order demand is reservation-backed and traceable to warehouse inventory.";

    private static SalesOrderAllocationWorkDto MapWork(WarehouseWorkEntity work)
    {
        var lineId = int.TryParse(work.SourceEntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        return new SalesOrderAllocationWorkDto(
            work.Id,
            work.WorkNumber,
            lineId,
            work.Lines.Sum(line => line.PlannedQuantity),
            work.Status.ToString(),
            work.CreationKey);
    }

    private static void ReconcileLineAllocation(
        SalesOrderLine line,
        InventoryReservationResult reservation,
        decimal crossDockCommittedQuantity = 0m)
    {
        var target = crossDockCommittedQuantity + reservation.ActiveQuantity + reservation.ConsumedQuantity;
        if (target > line.AllocatedBaseQuantity)
        {
            line.RecordAllocation(target - line.AllocatedBaseQuantity);
        }
        else if (target < line.AllocatedBaseQuantity)
        {
            line.ReleaseAllocation(line.AllocatedBaseQuantity - target);
        }
    }

    private static void SetAllocationSummaryStatus(SalesOrder order)
    {
        var hasBackorder = order.Lines.Any(line => line.BackorderBaseQuantity > 0m);
        order.SetAllocationStatus(
            hasBackorder
                ? SalesOrderStatus.PartiallyAllocated
                : SalesOrderStatus.Allocating);
    }

    private static InventoryReservationRequest CreateReservationRequest(
        SalesOrder order,
        SalesOrderLine line,
        decimal quantity,
        SalesOrderAllocationCommand command,
        string userId) =>
        new(
            "SalesOrderLine",
            order.Id.ToString(CultureInfo.InvariantCulture),
            line.Id,
            order.WarehouseId,
            line.ItemId,
            quantity,
            InventoryReservationMode.Hard,
            order.Priority,
            ActorUserId: userId,
            CorrelationId: BuildCorrelationId(order, line, command.IdempotencyKey),
            Reason: command.Reason ?? $"Sales order {order.DocumentNumber} allocation");

    private static string BuildWorkCreationKey(
        SalesOrder order,
        SalesOrderLine line,
        InventoryReservationResult reservation,
        InventoryReservationAllocationResult allocation) =>
        $"sales-order:{order.Id.ToString(CultureInfo.InvariantCulture)}" +
        $":line:{line.Id.ToString(CultureInfo.InvariantCulture)}" +
        $":reservation:{reservation.ReservationId.ToString(CultureInfo.InvariantCulture)}" +
        $":allocation:{allocation.AllocationId.ToString(CultureInfo.InvariantCulture)}";

    private static DateTime? ToDueAtUtc(DateOnly? requestedShipDate) =>
        requestedShipDate.HasValue
            ? DateTime.SpecifyKind(
                requestedShipDate.Value.ToDateTime(TimeOnly.MaxValue),
                DateTimeKind.Utc)
            : null;

    private static decimal GetDemandQuantity(SalesOrderLine line) =>
        Math.Max(0m, line.OrderedBaseQuantity - line.CancelledBaseQuantity);

    private static string BuildCorrelationId(
        SalesOrder order,
        SalesOrderLine line,
        string idempotencyKey)
    {
        var raw = $"allocation:{order.Id}:{line.Id}:{idempotencyKey.Trim()}";
        return raw.Length <= 100 ? raw : raw[..100];
    }

    private static void EnsureOrderCanAllocate(SalesOrder order)
    {
        if (order.Status is SalesOrderStatus.Draft or
            SalesOrderStatus.Held or
            SalesOrderStatus.Cancelled or
            SalesOrderStatus.Closed or
            SalesOrderStatus.Shipped)
        {
            throw new InvalidOperationException(
                $"A sales order in {order.Status} cannot be allocated.");
        }
    }

    private static Result<IReadOnlyList<SalesOrderLine>> SelectLines(
        SalesOrder order,
        IReadOnlyList<int>? lineIds)
    {
        var lines = lineIds is null
            ? order.Lines.OrderBy(line => line.LineNumber).ToArray()
            : order.Lines
                .Where(line => lineIds.Contains(line.Id))
                .OrderBy(line => line.LineNumber)
                .ToArray();
        if (lines.Length == 0)
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                "allocation.lines_not_found",
                "No selected sales-order lines were found."));
        }

        if (lineIds is not null && lines.Length != lineIds.Count)
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                "allocation.lines_not_found",
                "One or more selected lines do not belong to the sales order."));
        }

        return Result.Success<IReadOnlyList<SalesOrderLine>>(lines);
    }

    private static ResultError? ValidateLineSelection(
        IReadOnlyList<int>? lineIds)
    {
        if (lineIds is not null &&
            (lineIds.Any(id => id <= 0) || lineIds.Distinct().Count() != lineIds.Count))
        {
            return WmsErrors.Validation(
                "allocation.lines_invalid",
                "Selected sales-order line IDs must be positive and unique.");
        }

        return null;
    }

    private static HashSet<int>? NormalizeLineIds(
        IReadOnlyList<int>? lineIds,
        SalesOrder order)
    {
        if (lineIds is null)
        {
            return null;
        }

        if (lineIds.Count == 0 ||
            lineIds.Any(id => id <= 0) ||
            lineIds.Distinct().Count() != lineIds.Count)
        {
            throw new ArgumentException(
                "Selected sales-order line IDs must be positive and unique.",
                nameof(lineIds));
        }

        var selected = lineIds.ToHashSet();
        if (selected.Except(order.Lines.Select(line => line.Id)).Any())
        {
            throw new ArgumentException(
                "One or more selected lines do not belong to the sales order.",
                nameof(lineIds));
        }

        return selected;
    }

    private static ResultError? ValidateIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 250)
        {
            return WmsErrors.Validation(
                "allocation.idempotency_required",
                "Every allocation mutation requires a non-empty idempotency key of at most 250 characters.");
        }

        return null;
    }

    private static AuditRecord CreateAllocationAudit(
        SalesOrder order,
        string operation,
        decimal allocatedQuantity,
        decimal backorderQuantity,
        string userId) =>
        new(
            WmsAuditActions.AllocationChanged,
            WmsAuditEntityTypes.Allocation,
            order.DocumentNumber,
            order.WarehouseId,
            After: new Dictionary<string, object?>
            {
                ["operation"] = operation,
                ["status"] = order.Status.ToString(),
                ["allocatedQuantity"] = allocatedQuantity,
                ["backorderQuantity"] = backorderQuantity,
                ["orderRevision"] = order.Revision
            },
            ActorUserId: userId);

    private sealed class RetryableInventoryContentionException(Exception innerException)
        : Exception("Inventory changed while the allocation was being reserved.", innerException);
}
