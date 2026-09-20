using Wms.Domain.Entities;
using Wms.Domain.Inventory;

namespace Wms.Domain.Repositories;

public interface IInventoryBalanceRepository
{
    Task<InventoryBalance?> GetByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default);

    Task<Stock?> GetLegacyStockByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryBalance>> GetByItemIdAsync(
        int itemId,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryBalance>> GetAllAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<InventoryBalance> AddAsync(
        InventoryBalance balance,
        CancellationToken cancellationToken = default);
}

public interface IInventoryTransactionRepository
{
    Task<InventoryTransaction?> GetByIdempotencyAsync(
        string idempotencyKey,
        int entrySequence,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryTransaction>> GetByDimensionAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryTransaction>> GetByItemIdAsync(
        int itemId,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryTransaction>> GetAllAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<InventoryTransaction> AddAsync(
        InventoryTransaction transaction,
        CancellationToken cancellationToken = default);
}
