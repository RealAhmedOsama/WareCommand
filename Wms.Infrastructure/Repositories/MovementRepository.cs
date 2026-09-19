// Wms.Infrastructure/Repositories/MovementRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class MovementRepository : Repository<Movement>, IMovementRepository
{
    public MovementRepository(WmsDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Movement>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .Include(m => m.Lot)
            .Where(m => m.ItemId == itemId)
            .OrderByDescending(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByLocationIdAsync(int locationId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .Include(m => m.Lot)
            .Where(m => m.FromLocationId == locationId || m.ToLocationId == locationId)
            .OrderByDescending(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByDateRangeAsync(DateTime from, DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .Include(m => m.Lot)
            .Where(m => m.Timestamp >= from && m.Timestamp <= toDate)
            .OrderByDescending(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByTypeAsync(MovementType type,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .Include(m => m.Lot)
            .Where(m => m.Type == type)
            .OrderByDescending(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Movement>> GetByUserIdAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(m => m.Item)
            .Include(m => m.FromLocation)
            .Include(m => m.ToLocation)
            .Include(m => m.Lot)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Timestamp)
            .ToListAsync(cancellationToken);
    }
}
