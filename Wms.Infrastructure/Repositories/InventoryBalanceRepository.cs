using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class InventoryBalanceRepository : IInventoryBalanceRepository
{
    private readonly WmsDbContext _context;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public InventoryBalanceRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService)
    {
        _context = context;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<InventoryBalance?> GetByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryBalances.AsQueryable(), scope);
        return await query.SingleOrDefaultAsync(
            balance => balance.WarehouseId == key.WarehouseId &&
                       balance.LocationId == key.LocationId &&
                       balance.ItemId == key.ItemId &&
                       balance.LotId == key.LotId &&
                       balance.SerialNumberId == key.SerialNumberId &&
                       balance.SerialNumber == key.SerialNumber &&
                       balance.LicensePlateId == key.LicensePlateId &&
                       balance.InventoryStatusId == key.InventoryStatusId &&
                       balance.BaseUnitOfMeasure == key.BaseUnitOfMeasure,
            cancellationToken);
    }

    public async Task<Stock?> GetLegacyStockByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = _context.Stock
            .Include(stock => stock.Item)
            .Include(stock => stock.Location)
            .Where(stock => stock.Location.WarehouseId == key.WarehouseId &&
                            stock.ItemId == key.ItemId &&
                            stock.LocationId == key.LocationId &&
                            stock.LotId == key.LotId &&
                            stock.InventoryStatusId == key.InventoryStatusId &&
                            stock.LicensePlateId == key.LicensePlateId &&
                            stock.Item.UnitOfMeasure == key.BaseUnitOfMeasure)
            .Where(stock => key.SerialNumberId.HasValue
                ? stock.SerialNumberId == key.SerialNumberId ||
                  (stock.SerialNumberId == null && stock.SerialNumber == key.SerialNumber)
                : stock.SerialNumberId == null && stock.SerialNumber == key.SerialNumber)
            .AsQueryable();
        query = scope.HasGlobalAccess
            ? query
            : query.Where(stock => scope.WarehouseIds.Contains(stock.Location.WarehouseId));
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<InventoryBalance>> GetByItemIdAsync(
        int itemId,
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryBalances.AsQueryable(), scope)
            .Where(balance => balance.ItemId == itemId);
        if (warehouseId.HasValue)
        {
            query = query.Where(balance => balance.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderBy(balance => balance.WarehouseId)
            .ThenBy(balance => balance.LocationId)
            .ThenBy(balance => balance.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<InventoryBalance>> GetAllAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryBalances.AsQueryable(), scope);
        if (warehouseId.HasValue)
        {
            query = query.Where(balance => balance.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderBy(balance => balance.WarehouseId)
            .ThenBy(balance => balance.LocationId)
            .ThenBy(balance => balance.ItemId)
            .ThenBy(balance => balance.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryBalance> AddAsync(
        InventoryBalance balance,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.InventoryBalances.AddAsync(balance, cancellationToken);
        return entry.Entity;
    }

    private static IQueryable<InventoryBalance> ApplyScope(
        IQueryable<InventoryBalance> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(balance => scope.WarehouseIds.Contains(balance.WarehouseId));
}
