// Wms.Infrastructure/Repositories/StockRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class StockRepository : Repository<Stock>, IStockRepository
{
    public StockRepository(WmsDbContext context) : base(context)
    {
    }

    public override async Task<IEnumerable<Stock>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stock>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Stock>> GetByLocationIdAsync(int locationId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.LocationId == locationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Stock?> GetByItemAndLocationAsync(int itemId, int locationId, int? lotId = null,
        string? serialNumber = null, CancellationToken cancellationToken = default)
    {
        var query = DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId && s.LocationId == locationId);

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
        return await DbSet
            .Include(s => s.Item)
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.ItemId == itemId &&
                        s.QuantityAvailable.Value > s.QuantityReserved.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quantity> GetTotalQuantityAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var total = await DbSet
            .Where(s => s.ItemId == itemId)
            .SumAsync(s => s.QuantityAvailable.Value, cancellationToken);

        return new Quantity(total);
    }
}
