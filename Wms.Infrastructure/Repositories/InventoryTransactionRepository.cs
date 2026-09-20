using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class InventoryTransactionRepository : IInventoryTransactionRepository
{
    private readonly WmsDbContext _context;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public InventoryTransactionRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService)
    {
        _context = context;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<InventoryTransaction?> GetByIdempotencyAsync(
        string idempotencyKey,
        int entrySequence,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryTransactions.AsQueryable(), scope);
        return await query.SingleOrDefaultAsync(
            transaction => transaction.IdempotencyKey == idempotencyKey &&
                           transaction.EntrySequence == entrySequence,
            cancellationToken);
    }

    public async Task<IEnumerable<InventoryTransaction>> GetByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryTransactions.AsQueryable(), scope)
            .Where(transaction =>
                transaction.WarehouseId == key.WarehouseId &&
                transaction.LocationId == key.LocationId &&
                transaction.ItemId == key.ItemId &&
                transaction.LotId == key.LotId &&
                transaction.SerialNumberId == key.SerialNumberId &&
                transaction.SerialNumber == key.SerialNumber &&
                transaction.LicensePlateId == key.LicensePlateId &&
                transaction.InventoryStatusId == key.InventoryStatusId &&
                transaction.BaseUnitOfMeasure == key.BaseUnitOfMeasure);
        return await query
            .OrderBy(transaction => transaction.OccurredAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<InventoryTransaction>> GetByItemIdAsync(
        int itemId,
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryTransactions.AsQueryable(), scope)
            .Where(transaction => transaction.ItemId == itemId);
        if (warehouseId.HasValue)
        {
            query = query.Where(transaction => transaction.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderByDescending(transaction => transaction.OccurredAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<InventoryTransaction>> GetAllAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(_context.InventoryTransactions.AsQueryable(), scope);
        if (warehouseId.HasValue)
        {
            query = query.Where(transaction => transaction.WarehouseId == warehouseId.Value);
        }

        return await query
            .OrderBy(transaction => transaction.OccurredAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryTransaction> AddAsync(
        InventoryTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.InventoryTransactions.AddAsync(transaction, cancellationToken);
        return entry.Entity;
    }

    private static IQueryable<InventoryTransaction> ApplyScope(
        IQueryable<InventoryTransaction> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(transaction => scope.WarehouseIds.Contains(transaction.WarehouseId));
}
