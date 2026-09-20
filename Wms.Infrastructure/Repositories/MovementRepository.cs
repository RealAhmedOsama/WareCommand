// Wms.Infrastructure/Repositories/MovementRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class MovementRepository : Repository<Movement>, IMovementRepository
{
    private readonly IWarehouseAccessService _warehouseAccessService;

    public MovementRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService) : base(context)
    {
        _warehouseAccessService = warehouseAccessService;
    }

    public override async Task<Movement?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable());
        query = ApplyScope(query, scope);
        return await query.FirstOrDefaultAsync(movement => movement.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(m => m.ItemId == itemId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(m => m.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByLotIdAsync(
        int lotId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(movement => movement.LotId == lotId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(movement => movement.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByLocationIdAsync(int locationId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(m => m.FromLocationId == locationId || m.ToLocationId == locationId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(m => m.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByDateRangeAsync(
        DateTime fromInclusiveUtc,
        DateTime toExclusiveUtc,
        CancellationToken cancellationToken = default)
    {
        if (toExclusiveUtc <= fromInclusiveUtc)
        {
            throw new ArgumentException(
                "The exclusive movement range end must be after the start.",
                nameof(toExclusiveUtc));
        }

        fromInclusiveUtc = DateTime.SpecifyKind(fromInclusiveUtc, DateTimeKind.Utc);
        toExclusiveUtc = DateTime.SpecifyKind(toExclusiveUtc, DateTimeKind.Utc);
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(m => m.Timestamp >= fromInclusiveUtc && m.Timestamp < toExclusiveUtc)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(m => m.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByTypeAsync(MovementType type,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(m => m.Type == type)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(m => m.Timestamp).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByUserIdAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(m => m.UserId == userId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(m => m.Timestamp).ToListAsync(cancellationToken);
    }

    private static IQueryable<Movement> IncludeNavigations(IQueryable<Movement> query)
    {
        return query
            .Include(movement => movement.Item)
            .Include(movement => movement.FromLocation)
            .Include(movement => movement.ToLocation)
            .Include(movement => movement.Lot);
    }

    private static IQueryable<Movement> ApplyScope(
        IQueryable<Movement> query,
        WarehouseAccessScope scope)
    {
        return scope.HasGlobalAccess
            ? query
            : query.Where(movement =>
                (movement.FromLocation == null ||
                 scope.WarehouseIds.Contains(movement.FromLocation.WarehouseId)) &&
                (movement.ToLocation == null ||
                 scope.WarehouseIds.Contains(movement.ToLocation.WarehouseId)));
    }
}
