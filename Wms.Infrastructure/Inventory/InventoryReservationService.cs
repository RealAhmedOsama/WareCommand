using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Time;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Reservation and allocation engine built on top of the inventory ledger.
/// Allocation rows preserve the demand-to-dimension decision while the ledger
/// remains the only writer of materialized reserved/on-hand quantities.
/// </summary>
public sealed class InventoryReservationService(
    IUnitOfWork unitOfWork,
    WmsDbContext context,
    IInventoryLedgerService inventoryLedgerService,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    ILogger<InventoryReservationService> logger) : IInventoryReservationService
{
    public async Task<InventoryReservationResult> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateReservationRequest(request);
        var nowUtc = UtcNow();
        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "A reservation expiry must be in the future.");
        }

        await EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var demandKey = InventoryReservation.BuildDemandKey(
            request.DemandType,
            request.DemandId,
            request.DemandLine);
        var existing = await unitOfWork.InventoryReservations.GetByDemandAsync(
            demandKey,
            request.WarehouseId,
            cancellationToken);
        if (existing is not null)
        {
            EnsureSameDemand(existing, request);
            return ToResult(existing);
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

            var item = await context.Items
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.ItemId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Item '{request.ItemId}' was not found.");
            if (!item.IsActive)
            {
                throw new InvalidOperationException(
                    $"Item '{item.Sku}' is inactive and cannot be reserved.");
            }

            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == request.WarehouseId && candidate.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Warehouse '{request.WarehouseId}' was not found or is inactive.");

            var correlationId = NormalizeCorrelationId(request.CorrelationId);
            var actorUserId = NormalizeActor(request.ActorUserId);
            var reservation = new InventoryReservation(
                request.DemandType,
                request.DemandId,
                request.DemandLine,
                request.WarehouseId,
                request.ItemId,
                request.RequestedQuantity,
                request.Mode,
                request.Priority,
                NormalizeUtc(request.ExpiresAtUtc),
                actorUserId,
                correlationId,
                request.Reason,
                request.Selector);
            await unitOfWork.InventoryReservations.AddAsync(reservation, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await AddEventAsync(
                reservation,
                new InventoryReservationEvent(
                    reservation.Id,
                    InventoryReservationEventType.Created,
                    0m,
                    actorUserId,
                    nowUtc,
                    correlationId,
                    reason: request.Reason ?? "reservation created"),
                cancellationToken);

            var candidates = await GetEligibleBalancesAsync(
                request,
                item,
                warehouse,
                cancellationToken);
            var allocations = await AddAllocationsAsync(
                reservation,
                candidates,
                actorUserId,
                request.Reason,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (allocations.Count > 0)
            {
                var operationId = Guid.NewGuid().ToString("N");
                await inventoryLedgerService.RecordAsync(
                    allocations.Select((allocation, index) =>
                        CreateLedgerEntry(
                            allocation,
                            InventoryTransactionType.Reservation,
                            0m,
                            allocation.AllocatedQuantity,
                            reservation,
                            actorUserId,
                            correlationId,
                            nowUtc,
                            "reserve",
                            operationId,
                            index + 1)).ToArray(),
                    cancellationToken);

                foreach (var allocation in allocations)
                {
                    await AddEventAsync(
                        reservation,
                        new InventoryReservationEvent(
                            reservation.Id,
                            InventoryReservationEventType.AllocationAdded,
                            allocation.AllocatedQuantity,
                            actorUserId,
                            nowUtc,
                            correlationId,
                            allocation.Id,
                            request.Reason ?? "inventory allocated"),
                        cancellationToken);
                }
            }

            ApplyDerivedStatus(reservation);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;

            logger.LogInformation(
                "Inventory reservation {ReservationId} created for {DemandType}/{DemandId}; requested {RequestedQuantity}; active {ActiveQuantity}; status {Status}",
                reservation.Id,
                reservation.DemandType,
                reservation.DemandId,
                reservation.RequestedQuantity,
                GetActiveQuantity(reservation),
                reservation.Status);
            return ToResult(reservation);
        }
        catch
        {
            await RollbackIfNeededAsync(transactionStarted);
            throw;
        }
    }

    public async Task<InventoryReservationResult> ReleaseAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationRequest(request);
        return await MutateOpenReservationAsync(
            request,
            action: "release",
            ledgerType: InventoryTransactionType.Release,
            eventType: InventoryReservationEventType.Released,
            apply: (allocation, quantity, reason) => allocation.Release(quantity, reason),
            cancellationToken);
    }

    public async Task<InventoryReservationResult> ConsumeAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationRequest(request);
        return await MutateOpenReservationAsync(
            request,
            action: "consume",
            ledgerType: InventoryTransactionType.Pick,
            eventType: InventoryReservationEventType.Consumed,
            apply: (allocation, quantity, reason) => allocation.Consume(quantity, reason),
            cancellationToken);
    }

    public async Task<InventoryReservationResult> CancelAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationRequest(request, allowNullQuantity: true);
        var reservation = await LoadReservationAsync(request.ReservationId, cancellationToken);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"Reservation '{request.ReservationId}' was not found.");
        }

        await EnsureWarehouseAccessAsync(reservation.WarehouseId, cancellationToken);
        if (reservation.Status == InventoryReservationStatus.Cancelled)
        {
            return ToResult(reservation);
        }

        EnsureOpen(reservation);
        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var nowUtc = UtcNow();
            var actor = NormalizeActor(request.ActorUserId);
            var correlation = NormalizeCorrelationId(request.CorrelationId ?? reservation.CorrelationId);
            await ReleaseActiveAllocationsAsync(
                reservation,
                actor,
                correlation,
                request.Reason ?? "reservation cancelled",
                "cancel",
                InventoryTransactionType.Release,
                InventoryReservationEventType.Cancelled,
                cancel: true,
                expire: false,
                cancellationToken);
            reservation.SetStatus(
                InventoryReservationStatus.Cancelled,
                request.Reason ?? "reservation cancelled");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return ToResult(reservation);
        }
        catch
        {
            await RollbackIfNeededAsync(transactionStarted);
            throw;
        }
    }

    public async Task<InventoryReservationResult> ReallocateAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateMutationRequest(request, allowNullQuantity: true);
        var reservation = await LoadReservationAsync(request.ReservationId, cancellationToken);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"Reservation '{request.ReservationId}' was not found.");
        }

        await EnsureWarehouseAccessAsync(reservation.WarehouseId, cancellationToken);
        if (!reservation.CanReallocate)
        {
            throw new InvalidOperationException(
                $"Reservation '{reservation.Id}' cannot be reallocated while it is {reservation.Status}.");
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var nowUtc = UtcNow();
            var actor = NormalizeActor(request.ActorUserId);
            var correlation = NormalizeCorrelationId(request.CorrelationId ?? reservation.CorrelationId);
            await ReleaseActiveAllocationsAsync(
                reservation,
                actor,
                correlation,
                request.Reason ?? "reservation reallocated",
                "reallocate",
                InventoryTransactionType.Release,
                InventoryReservationEventType.Released,
                cancel: false,
                expire: false,
                cancellationToken);

            if (reservation.Status != InventoryReservationStatus.Released)
            {
                reservation.SetStatus(
                    InventoryReservationStatus.Released,
                    request.Reason ?? "reservation reallocated");
            }

            reservation.SetStatus(
                InventoryReservationStatus.Pending,
                request.Reason ?? "reservation reallocated");
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var item = await context.Items
                .SingleAsync(candidate => candidate.Id == reservation.ItemId, cancellationToken);
            var warehouse = await context.Warehouses
                .SingleAsync(candidate => candidate.Id == reservation.WarehouseId, cancellationToken);
            var reallocationRequest = new InventoryReservationRequest(
                reservation.DemandType,
                reservation.DemandId,
                reservation.DemandLine,
                reservation.WarehouseId,
                reservation.ItemId,
                reservation.RequestedQuantity,
                reservation.Mode,
                reservation.Priority,
                reservation.ExpiresAtUtc,
                Selector: reservation.GetSelector(),
                ActorUserId: actor,
                CorrelationId: correlation,
                Reason: request.Reason);
            var candidates = await GetEligibleBalancesAsync(
                reallocationRequest,
                item,
                warehouse,
                cancellationToken);
            var allocations = await AddAllocationsAsync(
                reservation,
                candidates,
                actor,
                request.Reason ?? "reservation reallocated",
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (allocations.Count > 0)
            {
                var operationId = Guid.NewGuid().ToString("N");
                await inventoryLedgerService.RecordAsync(
                    allocations.Select((allocation, index) =>
                        CreateLedgerEntry(
                            allocation,
                            InventoryTransactionType.Reservation,
                            0m,
                            allocation.AllocatedQuantity,
                            reservation,
                            actor,
                            correlation,
                            nowUtc,
                            "reallocate",
                            operationId,
                            index + 1)).ToArray(),
                    cancellationToken);
                foreach (var allocation in allocations)
                {
                    await AddEventAsync(
                        reservation,
                        new InventoryReservationEvent(
                            reservation.Id,
                            InventoryReservationEventType.AllocationAdded,
                            allocation.AllocatedQuantity,
                            actor,
                            nowUtc,
                            correlation,
                            allocation.Id,
                            request.Reason ?? "inventory reallocated"),
                        cancellationToken);
                }

                await AddEventAsync(
                    reservation,
                    new InventoryReservationEvent(
                        reservation.Id,
                        InventoryReservationEventType.Reallocated,
                        allocations.Sum(allocation => allocation.AllocatedQuantity),
                        actor,
                        nowUtc,
                        correlation,
                        reason: request.Reason ?? "reservation reallocated"),
                    cancellationToken);
            }

            ApplyDerivedStatus(reservation);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return ToResult(reservation);
        }
        catch
        {
            await RollbackIfNeededAsync(transactionStarted);
            throw;
        }
    }

    public async Task<IReadOnlyList<InventoryReservationResult>> ExpireAsync(
        DateTime? nowUtc = null,
        string actorUserId = "system",
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveNow = NormalizeUtc(nowUtc) ?? UtcNow();
        var reservations = (await unitOfWork.InventoryReservations.GetExpiredAsync(
            effectiveNow,
            cancellationToken)).ToArray();
        if (reservations.Length == 0)
        {
            return [];
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var actor = NormalizeActor(actorUserId);
            var results = new List<InventoryReservationResult>(reservations.Length);
            foreach (var reservation in reservations)
            {
                var correlation = NormalizeCorrelationId(correlationId ?? reservation.CorrelationId);
                await ReleaseActiveAllocationsAsync(
                    reservation,
                    actor,
                    correlation,
                    "reservation expired",
                    "expire",
                    InventoryTransactionType.Release,
                    InventoryReservationEventType.Expired,
                    cancel: false,
                    expire: true,
                    cancellationToken);
                reservation.SetStatus(
                    InventoryReservationStatus.Expired,
                    "reservation expired");
                results.Add(ToResult(reservation));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return results;
        }
        catch
        {
            await RollbackIfNeededAsync(transactionStarted);
            throw;
        }
    }

    public async Task<InventoryReservationResult?> GetAsync(
        int reservationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservationId);
        var reservation = await LoadReservationAsync(reservationId, cancellationToken);
        return reservation is null ? null : ToResult(reservation);
    }

    public async Task<InventoryReservationResult?> GetByDemandAsync(
        string demandType,
        string demandId,
        int? demandLine,
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        var demandKey = InventoryReservation.BuildDemandKey(demandType, demandId, demandLine);
        var reservation = await unitOfWork.InventoryReservations.GetByDemandAsync(
            demandKey,
            warehouseId,
            cancellationToken);
        return reservation is null ? null : ToResult(reservation);
    }

    private async Task<InventoryReservationResult> MutateOpenReservationAsync(
        InventoryReservationMutationRequest request,
        string action,
        InventoryTransactionType ledgerType,
        InventoryReservationEventType eventType,
        Action<InventoryReservationAllocation, decimal, string?> apply,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadReservationAsync(request.ReservationId, cancellationToken);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"Reservation '{request.ReservationId}' was not found.");
        }

        await EnsureWarehouseAccessAsync(reservation.WarehouseId, cancellationToken);
        EnsureOpen(reservation);
        var activeQuantity = GetActiveQuantity(reservation);
        var quantity = request.Quantity ?? activeQuantity;
        if (quantity == 0m && activeQuantity == 0m)
        {
            return ToResult(reservation);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (quantity > activeQuantity)
        {
            throw new InvalidOperationException(
                "The requested quantity exceeds the active reservation quantity.");
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var actor = NormalizeActor(request.ActorUserId);
            var correlation = NormalizeCorrelationId(request.CorrelationId ?? reservation.CorrelationId);
            var reason = request.Reason ?? $"reservation {action}";
            var operationId = Guid.NewGuid().ToString("N");
            var entries = new List<InventoryLedgerEntryRequest>();
            var events = new List<InventoryReservationEvent>();
            var remaining = quantity;
            var sequence = 1;
            foreach (var allocation in reservation.Allocations
                         .Where(allocation => allocation.IsOpen)
                         .OrderBy(allocation => allocation.Id))
            {
                if (remaining == 0m)
                {
                    break;
                }

                var appliedQuantity = Math.Min(allocation.RemainingQuantity, remaining);
                apply(allocation, appliedQuantity, reason);
                entries.Add(CreateLedgerEntry(
                    allocation,
                    ledgerType,
                    ledgerType == InventoryTransactionType.Pick
                        ? -appliedQuantity
                        : 0m,
                    -appliedQuantity,
                    reservation,
                    actor,
                    correlation,
                    UtcNow(),
                    action,
                    operationId,
                    sequence++));
                events.Add(new InventoryReservationEvent(
                    reservation.Id,
                    eventType,
                    appliedQuantity,
                    actor,
                    UtcNow(),
                    correlation,
                    allocation.Id,
                    reason));
                remaining -= appliedQuantity;
            }

            if (remaining != 0m)
            {
                throw new InvalidOperationException(
                    "The reservation changed while the mutation was being prepared.");
            }

            await inventoryLedgerService.RecordAsync(entries, cancellationToken);
            foreach (var reservationEvent in events)
            {
                await AddEventAsync(reservation, reservationEvent, cancellationToken);
            }

            ApplyDerivedStatus(reservation);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return ToResult(reservation);
        }
        catch
        {
            await RollbackIfNeededAsync(transactionStarted);
            throw;
        }
    }

    private async Task ReleaseActiveAllocationsAsync(
        InventoryReservation reservation,
        string actor,
        string correlation,
        string reason,
        string action,
        InventoryTransactionType ledgerType,
        InventoryReservationEventType eventType,
        bool cancel,
        bool expire,
        CancellationToken cancellationToken)
    {
        var entries = new List<InventoryLedgerEntryRequest>();
        var events = new List<InventoryReservationEvent>();
        var operationId = Guid.NewGuid().ToString("N");
        var sequence = 1;
        foreach (var allocation in reservation.Allocations
                     .Where(allocation => allocation.IsOpen)
                     .OrderBy(allocation => allocation.Id))
        {
            var quantity = allocation.RemainingQuantity;
            if (quantity <= 0m)
            {
                continue;
            }

            if (cancel)
            {
                allocation.Cancel(reason);
            }
            else if (expire)
            {
                allocation.Expire(reason);
            }
            else
            {
                allocation.Release(quantity, reason);
            }

            entries.Add(CreateLedgerEntry(
                allocation,
                ledgerType,
                0m,
                -quantity,
                reservation,
                actor,
                correlation,
                UtcNow(),
                action,
                operationId,
                sequence++));
            events.Add(new InventoryReservationEvent(
                reservation.Id,
                eventType,
                quantity,
                actor,
                UtcNow(),
                correlation,
                allocation.Id,
                reason));
        }

        if (entries.Count > 0)
        {
            await inventoryLedgerService.RecordAsync(entries, cancellationToken);
        }

        foreach (var reservationEvent in events)
        {
            await AddEventAsync(reservation, reservationEvent, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<InventoryBalance>> GetEligibleBalancesAsync(
        InventoryReservationRequest request,
        Item item,
        Warehouse warehouse,
        CancellationToken cancellationToken)
    {
        var selector = request.Selector;
        if (!string.IsNullOrWhiteSpace(selector?.BaseUnitOfMeasure) &&
            !string.Equals(
                selector.BaseUnitOfMeasure.Trim(),
                item.UnitOfMeasure,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Reservation unit '{selector.BaseUnitOfMeasure}' does not match item base unit '{item.UnitOfMeasure}'.");
        }

        var query = context.InventoryBalances
            .AsTracking()
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Location)
            .Include(balance => balance.Item)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Serial)
            .Include(balance => balance.LicensePlate)
            .Include(balance => balance.InventoryStatus)
            .Where(balance => balance.WarehouseId == request.WarehouseId &&
                              balance.ItemId == request.ItemId &&
                              balance.BaseUnitOfMeasure == item.UnitOfMeasure &&
                              balance.OnHandQuantity > balance.ReservedQuantity);

        if (selector?.LocationId is > 0)
        {
            query = query.Where(balance => balance.LocationId == selector.LocationId.Value);
        }

        if (selector?.LotId is > 0)
        {
            query = query.Where(balance => balance.LotId == selector.LotId.Value);
        }

        if (selector?.SerialNumberId is > 0)
        {
            query = query.Where(balance => balance.SerialNumberId == selector.SerialNumberId.Value);
        }

        if (!string.IsNullOrWhiteSpace(selector?.SerialNumber))
        {
            var serialNumber = selector.SerialNumber.Trim().ToUpperInvariant();
            query = query.Where(balance => balance.SerialNumber == serialNumber);
        }

        if (selector?.LicensePlateId is > 0)
        {
            query = query.Where(balance => balance.LicensePlateId == selector.LicensePlateId.Value);
        }

        if (selector?.InventoryStatusId is > 0)
        {
            query = query.Where(balance => balance.InventoryStatusId == selector.InventoryStatusId.Value);
        }

        var businessDate = WmsBusinessTime.GetBusinessDate(
            clock.UtcNow,
            warehouse.TimeZone);
        var candidates = await query.ToListAsync(cancellationToken);
        var eligible = candidates
            .Where(balance => IsEligible(balance, item, request.WarehouseId, businessDate))
            .ToArray();

        IEnumerable<InventoryBalance> ordered = item.UseFefo
            ? eligible
                .OrderBy(balance => balance.Lot?.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(balance => balance.Location.Priority)
                .ThenBy(balance => balance.CreatedAt)
                .ThenBy(balance => balance.Id)
            : eligible
                .OrderBy(balance => balance.Location.Priority)
                .ThenBy(balance => balance.CreatedAt)
                .ThenBy(balance => balance.Id);
        return ordered.ToArray();
    }

    private async Task<IReadOnlyList<InventoryReservationAllocation>> AddAllocationsAsync(
        InventoryReservation reservation,
        IReadOnlyList<InventoryBalance> candidates,
        string actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var remaining = reservation.RequestedQuantity;
        var allocations = new List<InventoryReservationAllocation>();
        foreach (var balance in candidates)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var quantity = Math.Min(balance.AvailableQuantity, remaining);
            if (quantity <= 0m)
            {
                continue;
            }

            var allocation = new InventoryReservationAllocation(
                reservation.Id,
                balance.WarehouseId,
                balance.LocationId,
                balance.ItemId,
                balance.LotId,
                balance.SerialNumberId,
                balance.SerialNumber,
                balance.LicensePlateId,
                balance.InventoryStatusId,
                balance.BaseUnitOfMeasure,
                quantity,
                reason ?? $"allocated by {actorUserId}");
            await unitOfWork.InventoryReservations.AddAllocationAsync(
                allocation,
                cancellationToken);
            if (!reservation.Allocations.Contains(allocation))
            {
                reservation.Allocations.Add(allocation);
            }
            allocations.Add(allocation);
            remaining -= quantity;
        }

        return allocations;
    }

    private async Task<InventoryReservation?> LoadReservationAsync(
        int reservationId,
        CancellationToken cancellationToken) =>
        await unitOfWork.InventoryReservations.GetByIdAsync(
            reservationId,
            cancellationToken);

    private async Task AddEventAsync(
        InventoryReservation reservation,
        InventoryReservationEvent reservationEvent,
        CancellationToken cancellationToken)
    {
        await unitOfWork.InventoryReservations.AddEventAsync(
            reservationEvent,
            cancellationToken);
        if (!reservation.Events.Contains(reservationEvent))
        {
            reservation.Events.Add(reservationEvent);
        }
    }

    private async Task EnsureWarehouseAccessAsync(
        int warehouseId,
        CancellationToken cancellationToken)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        if (!scope.HasGlobalAccess && !scope.WarehouseIds.Contains(warehouseId))
        {
            throw new UnauthorizedAccessException(
                $"The current user does not have access to warehouse '{warehouseId}'.");
        }
    }

    private async Task RollbackIfNeededAsync(bool transactionStarted)
    {
        if (!transactionStarted)
        {
            return;
        }

        try
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(
                rollbackException,
                "Inventory reservation transaction rollback failed");
        }
    }

    private static bool IsEligible(
        InventoryBalance balance,
        Item item,
        int warehouseId,
        DateOnly businessDate)
    {
        if (balance.Warehouse is null ||
            !balance.Warehouse.IsActive ||
            balance.Location is null ||
            balance.Location.WarehouseId != warehouseId ||
            !balance.Location.IsActive ||
            !balance.Location.IsPickable ||
            balance.InventoryStatus is null ||
            !balance.InventoryStatus.IsActive ||
            !balance.InventoryStatus.IsAvailable ||
            !balance.InventoryStatus.IsAllocatable)
        {
            return false;
        }

        if (item.RequiresLot && balance.Lot is null)
        {
            return false;
        }

        if (balance.Lot is not null && !balance.Lot.IsAllocationEligible(businessDate))
        {
            return false;
        }

        if (item.RequiresSerial && balance.Serial is null)
        {
            return false;
        }

        if (balance.Serial is not null &&
            (!balance.Serial.IsAllocationEligible ||
             balance.Serial.CurrentWarehouseId != warehouseId ||
             balance.Serial.CurrentLocationId != balance.LocationId))
        {
            return false;
        }

        if (balance.LicensePlate is not null &&
            (!balance.LicensePlate.IsActive ||
             balance.LicensePlate.Status is not (LicensePlateStatus.Open or LicensePlateStatus.Returned) ||
             balance.LicensePlate.WarehouseId != warehouseId ||
             balance.LicensePlate.CurrentLocationId != balance.LocationId))
        {
            return false;
        }

        return true;
    }

    private static InventoryLedgerEntryRequest CreateLedgerEntry(
        InventoryReservationAllocation allocation,
        InventoryTransactionType type,
        decimal quantityDelta,
        decimal reservedQuantityDelta,
        InventoryReservation reservation,
        string actorUserId,
        string correlationId,
        DateTime occurredAtUtc,
        string action,
        string operationId,
        int sequence) =>
        new(
            type,
            new InventoryBalanceKey(
                allocation.WarehouseId,
                allocation.LocationId,
                allocation.ItemId,
                allocation.LotId,
                allocation.SerialNumberId,
                allocation.SerialNumber,
                allocation.LicensePlateId,
                allocation.InventoryStatusId,
                allocation.BaseUnitOfMeasure),
            quantityDelta,
            reservedQuantityDelta,
            "InventoryReservation",
            reservation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            allocation.Id,
            reservation.StatusReason,
            actorUserId,
            occurredAtUtc,
            correlationId,
            $"reservation:{reservation.Id}:{action}:{operationId}:{allocation.Id}",
            $"reservation:{reservation.Id}:{action}:{operationId}",
            sequence);

    private static void ApplyDerivedStatus(InventoryReservation reservation)
    {
        var active = GetActiveQuantity(reservation);
        var consumed = reservation.Allocations.Sum(allocation => allocation.ConsumedQuantity);
        var allocated = reservation.Allocations.Sum(allocation => allocation.AllocatedQuantity);
        InventoryReservationStatus status;
        if (consumed >= reservation.RequestedQuantity)
        {
            status = InventoryReservationStatus.Consumed;
        }
        else if (active == 0m && allocated > 0m)
        {
            status = InventoryReservationStatus.Released;
        }
        else if (consumed > 0m)
        {
            status = InventoryReservationStatus.PartiallyConsumed;
        }
        else if (active >= reservation.RequestedQuantity)
        {
            status = InventoryReservationStatus.Reserved;
        }
        else if (active > 0m)
        {
            status = InventoryReservationStatus.PartiallyReserved;
        }
        else
        {
            status = InventoryReservationStatus.Pending;
        }

        reservation.SetStatus(status, reservation.StatusReason);
    }

    private static decimal GetActiveQuantity(InventoryReservation reservation) =>
        reservation.Allocations.Sum(allocation => allocation.RemainingQuantity);

    private static InventoryReservationResult ToResult(InventoryReservation reservation)
    {
        var allocated = reservation.Allocations.Sum(allocation => allocation.AllocatedQuantity);
        var consumed = reservation.Allocations.Sum(allocation => allocation.ConsumedQuantity);
        var released = reservation.Allocations.Sum(allocation => allocation.ReleasedQuantity);
        var active = reservation.Allocations.Sum(allocation => allocation.RemainingQuantity);
        var backorder = Math.Max(0m, reservation.RequestedQuantity - consumed - active);
        return new InventoryReservationResult(
            reservation.Id,
            reservation.DemandType,
            reservation.DemandId,
            reservation.DemandLine,
            reservation.WarehouseId,
            reservation.ItemId,
            reservation.RequestedQuantity,
            allocated,
            consumed,
            released,
            active,
            backorder,
            reservation.Mode,
            reservation.Status,
            reservation.ExpiresAtUtc,
            reservation.Allocations
                .OrderBy(allocation => allocation.Id)
                .Select(allocation => new InventoryReservationAllocationResult(
                    allocation.Id,
                    allocation.WarehouseId,
                    allocation.LocationId,
                    allocation.ItemId,
                    allocation.LotId,
                    allocation.SerialNumberId,
                    allocation.SerialNumber,
                    allocation.LicensePlateId,
                    allocation.InventoryStatusId,
                    allocation.BaseUnitOfMeasure,
                    allocation.AllocatedQuantity,
                    allocation.ConsumedQuantity,
                    allocation.ReleasedQuantity,
                    allocation.RemainingQuantity,
                    allocation.Status,
                    allocation.Reason))
                .ToArray(),
            reservation.Events
                .OrderBy(reservationEvent => reservationEvent.OccurredAtUtc)
                .ThenBy(reservationEvent => reservationEvent.Id)
                .Select(reservationEvent => new InventoryReservationEventResult(
                    reservationEvent.Id,
                    reservationEvent.AllocationId,
                    reservationEvent.Type,
                    reservationEvent.Quantity,
                    reservationEvent.ActorUserId,
                    reservationEvent.OccurredAtUtc,
                    reservationEvent.CorrelationId,
                    reservationEvent.Reason))
                .ToArray());
    }

    private static void EnsureSameDemand(
        InventoryReservation existing,
        InventoryReservationRequest request)
    {
        if (existing.ItemId != request.ItemId ||
            existing.RequestedQuantity != request.RequestedQuantity ||
            existing.Mode != request.Mode)
        {
            throw new InvalidOperationException(
                $"Demand '{existing.DemandKey}' already has a reservation with a different contract.");
        }
    }

    private static void EnsureOpen(InventoryReservation reservation)
    {
        if (!reservation.IsOpen)
        {
            throw new InvalidOperationException(
                $"Reservation '{reservation.Id}' is not open; current status is {reservation.Status}.");
        }
    }

    private static void ValidateReservationRequest(InventoryReservationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.WarehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.RequestedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Priority);
        if (request.DemandLine is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (!Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Selector?.BaseUnitOfMeasure is not null &&
            string.IsNullOrWhiteSpace(request.Selector.BaseUnitOfMeasure))
        {
            throw new ArgumentException(
                "The selector unit of measure cannot be empty.",
                nameof(request));
        }
    }

    private static void ValidateMutationRequest(
        InventoryReservationMutationRequest request,
        bool allowNullQuantity = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.ReservationId);
        if (!allowNullQuantity && request.Quantity is null)
        {
            return;
        }

        if (request.Quantity is <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private DateTime UtcNow() =>
        DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : null;

    private static string NormalizeActor(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();

    private static string NormalizeCorrelationId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Guid.NewGuid().ToString("N")
            : value.Trim();
}
