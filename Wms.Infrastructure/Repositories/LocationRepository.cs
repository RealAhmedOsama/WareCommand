// Wms.Infrastructure/Repositories/LocationRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class LocationRepository : Repository<Location>, ILocationRepository
{
    private readonly IWarehouseAccessService _warehouseAccessService;

    public LocationRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService) : base(context)
    {
        _warehouseAccessService = warehouseAccessService;
    }

    public override async Task<Location?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet.AsQueryable();
        query = ApplyScope(query, scope);
        return await query.FirstOrDefaultAsync(location => location.Id == id, cancellationToken);
    }

    public override async Task<IEnumerable<Location>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(location => location.Warehouse)
            .Include(location => location.ParentLocation)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderBy(location => location.Code).ToListAsync(cancellationToken);
    }

    public async Task<Location?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.ToUpperInvariant();
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.FirstOrDefaultAsync(l => l.Code == normalizedCode, cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetByWarehouseIdAsync(int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.ParentLocation)
            .Where(l => l.WarehouseId == warehouseId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderBy(l => l.Code).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetChildLocationsAsync(int parentLocationId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .Where(l => l.ParentLocationId == parentLocationId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> SearchAsync(string searchTerm,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .Where(l => l.Code.Contains(searchTerm) ||
                        l.Name.Contains(searchTerm) ||
                        l.Warehouse.Name.Contains(searchTerm))
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderBy(l => l.Code).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetReceivableLocationsAsync(CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.Warehouse)
            .Where(l => l.IsReceivable && l.IsActive)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderBy(l => l.Code).ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetPickableLocationsAsync(CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(l => l.Warehouse)
            .Where(l => l.IsPickable && l.IsActive)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.OrderBy(l => l.Code).ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.ToUpperInvariant();
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(DbSet.AsQueryable(), scope);
        return await query.AnyAsync(l => l.Code == normalizedCode, cancellationToken);
    }

    private static IQueryable<Location> ApplyScope(
        IQueryable<Location> query,
        WarehouseAccessScope scope)
    {
        return scope.HasGlobalAccess
            ? query
            : query.Where(location => scope.WarehouseIds.Contains(location.WarehouseId));
    }
}
