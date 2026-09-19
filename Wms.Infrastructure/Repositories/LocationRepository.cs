// Wms.Infrastructure/Repositories/LocationRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class LocationRepository : Repository<Location>, ILocationRepository
{
    public LocationRepository(WmsDbContext context) : base(context)
    {
    }

    public async Task<Location?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.ToUpperInvariant();
        return await DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .FirstOrDefaultAsync(l => l.Code == normalizedCode, cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetByWarehouseIdAsync(int warehouseId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(l => l.ParentLocation)
            .Where(l => l.WarehouseId == warehouseId)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetChildLocationsAsync(int parentLocationId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .Where(l => l.ParentLocationId == parentLocationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> SearchAsync(string searchTerm,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(l => l.Warehouse)
            .Include(l => l.ParentLocation)
            .Where(l => l.Code.Contains(searchTerm) ||
                        l.Name.Contains(searchTerm) ||
                        l.Warehouse.Name.Contains(searchTerm))
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetReceivableLocationsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(l => l.Warehouse)
            .Where(l => l.IsReceivable && l.IsActive)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Location>> GetPickableLocationsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(l => l.Warehouse)
            .Where(l => l.IsPickable && l.IsActive)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.ToUpperInvariant();
        return await DbSet
            .AnyAsync(l => l.Code == normalizedCode, cancellationToken);
    }
}
