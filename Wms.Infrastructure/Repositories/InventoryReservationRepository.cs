using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class InventoryReservationRepository(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService) : IInventoryReservationRepository
{
    public async Task<InventoryReservation?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(
            context.InventoryReservations
                .Include(reservation => reservation.Allocations)
                .Include(reservation => reservation.Events),
            scope);
        return await query.SingleOrDefaultAsync(
            reservation => reservation.Id == id,
            cancellationToken);
    }

    public async Task<InventoryReservation?> GetByDemandAsync(
        string demandKey,
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(
            context.InventoryReservations
                .Include(reservation => reservation.Allocations)
                .Include(reservation => reservation.Events),
            scope);
        return await query.SingleOrDefaultAsync(
            reservation => reservation.WarehouseId == warehouseId &&
                           reservation.DemandKey == demandKey,
            cancellationToken);
    }

    public async Task<IEnumerable<InventoryReservation>> GetExpiredAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(
            context.InventoryReservations
                .Include(reservation => reservation.Allocations)
                .Include(reservation => reservation.Events),
            scope);
        return await query
            .Where(reservation => reservation.ExpiresAtUtc.HasValue &&
                                 reservation.ExpiresAtUtc <= nowUtc &&
                                 reservation.Status != Domain.Enums.InventoryReservationStatus.Consumed &&
                                 reservation.Status != Domain.Enums.InventoryReservationStatus.Released &&
                                 reservation.Status != Domain.Enums.InventoryReservationStatus.Expired &&
                                 reservation.Status != Domain.Enums.InventoryReservationStatus.Cancelled)
            .OrderBy(reservation => reservation.ExpiresAtUtc)
            .ThenBy(reservation => reservation.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryReservation> AddAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken = default)
    {
        var entry = await context.InventoryReservations.AddAsync(reservation, cancellationToken);
        return entry.Entity;
    }

    public async Task<InventoryReservationAllocation> AddAllocationAsync(
        InventoryReservationAllocation allocation,
        CancellationToken cancellationToken = default)
    {
        var entry = await context.InventoryReservationAllocations
            .AddAsync(allocation, cancellationToken);
        return entry.Entity;
    }

    public async Task<InventoryReservationEvent> AddEventAsync(
        InventoryReservationEvent reservationEvent,
        CancellationToken cancellationToken = default)
    {
        var entry = await context.InventoryReservationEvents
            .AddAsync(reservationEvent, cancellationToken);
        return entry.Entity;
    }

    private static IQueryable<InventoryReservation> ApplyScope(
        IQueryable<InventoryReservation> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(reservation => scope.WarehouseIds.Contains(reservation.WarehouseId));
}
