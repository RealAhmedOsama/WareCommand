// Wms.Infrastructure/Repositories/StockRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class StockRepository : Repository<Stock>, IStockRepository
{
    private readonly IWarehouseAccessService _warehouseAccessService;

    public StockRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService) : base(context)
    {
        _warehouseAccessService = warehouseAccessService;
    }

    public override async Task<Stock?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(stock => stock.Item)
            .Include(stock => stock.Location)
            .Include(stock => stock.Lot)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.FirstOrDefaultAsync(stock => stock.Id == id, cancellationToken);
    }

    public override async Task<IEnumerable<Stock>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stock>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stock>> GetByLocationIdAsync(int locationId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.LocationId == locationId)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Stock?> GetByItemAndLocationAsync(int itemId, int locationId, int? lotId = null,
        string? serialNumber = null, CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId && s.LocationId == locationId)
            .AsQueryable();
        query = ApplyScope(query, scope);

        if (lotId.HasValue)
            query = query.Where(s => s.LotId == lotId.Value);
        else
            query = query.Where(s => s.LotId == null);

        if (!string.IsNullOrWhiteSpace(serialNumber))
            query = query.Where(s => s.SerialNumber == serialNumber);
        else
            query = query.Where(s => s.SerialNumber == null);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stock>> GetAvailableStockAsync(int itemId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId &&
                        s.QuantityAvailable.Value > s.QuantityReserved.Value)
            .AsQueryable();
        query = ApplyScope(query, scope);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Quantity> GetTotalQuantityAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(DbSet.AsQueryable(), scope);
        var total = await query
            .Where(s => s.ItemId == itemId)
            .SumAsync(s => s.QuantityAvailable.Value, cancellationToken);

        return new Quantity(total);
    }

    private static IQueryable<Stock> ApplyScope(
        IQueryable<Stock> query,
        WarehouseAccessScope scope)
    {
        return scope.HasGlobalAccess
            ? query
            : query.Where(stock => scope.WarehouseIds.Contains(stock.Location.WarehouseId));
    }
}
