using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class InventoryStatusRepository(WmsDbContext context)
    : Repository<InventoryStatus>(context), IInventoryStatusRepository
{
    private readonly WmsDbContext _context = context;

    public override async Task<InventoryStatus?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(status => status.Warehouse)
            .FirstOrDefaultAsync(status => status.Id == id, cancellationToken);
    }

    public async Task<InventoryStatus?> GetByCodeAsync(
        string code,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var query = DbSet.Where(status => status.Code == normalized);
        if (warehouseId.HasValue)
        {
            query = query.Where(status =>
                status.WarehouseId == warehouseId.Value || status.WarehouseId == null);
        }
        else
        {
            query = query.Where(status => status.WarehouseId == null);
        }

        return await query
            .Include(status => status.Warehouse)
            .OrderByDescending(status => status.WarehouseId.HasValue)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<InventoryStatus>> GetForWarehouseAsync(
        int? warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.AsQueryable();
        if (warehouseId.HasValue)
        {
            query = query.Where(status =>
                status.WarehouseId == warehouseId.Value || status.WarehouseId == null);
        }
        else
        {
            query = query.Where(status => status.WarehouseId == null);
        }

        if (!includeInactive)
        {
            query = query.Where(status => status.IsActive);
        }

        return await query
            .Include(status => status.Warehouse)
            .OrderBy(status => status.Code)
            .ThenBy(status => status.WarehouseId)
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryStatusTransition?> GetTransitionAsync(
        int fromStatusId,
        int toStatusId,
        CancellationToken cancellationToken = default)
    {
        return await _context.InventoryStatusTransitions
            .Include(transition => transition.FromInventoryStatus)
            .Include(transition => transition.ToInventoryStatus)
            .FirstOrDefaultAsync(
                transition => transition.FromInventoryStatusId == fromStatusId &&
                              transition.ToInventoryStatusId == toStatusId,
                cancellationToken);
    }

    public async Task<InventoryStatusTransition> AddTransitionAsync(
        InventoryStatusTransition transition,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.InventoryStatusTransitions.AddAsync(transition, cancellationToken);
        return entry.Entity;
    }

    public async Task<bool> IsInUseAsync(int statusId, CancellationToken cancellationToken = default)
    {
        return await _context.Stock.AnyAsync(
                   stock => stock.InventoryStatusId == statusId,
                   cancellationToken) ||
               await _context.Movements.AnyAsync(
                   movement => movement.InventoryStatusId == statusId ||
                               movement.FromInventoryStatusId == statusId ||
                               movement.ToInventoryStatusId == statusId,
                   cancellationToken);
    }
}
