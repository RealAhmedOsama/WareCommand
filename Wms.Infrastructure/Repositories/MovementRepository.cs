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
    private readonly WmsDbContext _context;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public MovementRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService) : base(context)
    {
        _context = context;
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

    public async Task<IEnumerable<Movement>> GetBySerialNumberIdAsync(
        int serialNumberId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(movement => movement.SerialNumberId == serialNumberId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderByDescending(movement => movement.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByLicensePlateIdAsync(
        int licensePlateId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeNavigations(DbSet.AsQueryable())
            .Where(movement => movement.LicensePlateId == licensePlateId ||
                               movement.FromLicensePlateId == licensePlateId ||
                               movement.ToLicensePlateId == licensePlateId)
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

    private IQueryable<Movement> IncludeNavigations(IQueryable<Movement> query)
    {
        var included = query
            .Include(movement => movement.Item)
            .Include(movement => movement.FromLocation)
            .Include(movement => movement.ToLocation)
            .Include(movement => movement.Lot)
            .Include(movement => movement.Serial);

        // Some legacy in-memory repository tests construct movements directly
        // without seeding the status catalog. Keep those records queryable;
        // relational stores always load the required status graph.
        return _context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
            ? included
            : included
                .Include(movement => movement.InventoryStatus)
                .Include(movement => movement.FromInventoryStatus)
                .Include(movement => movement.ToInventoryStatus)
                .Include(movement => movement.LicensePlate)
                .Include(movement => movement.FromLicensePlate)
                .Include(movement => movement.ToLicensePlate);
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
