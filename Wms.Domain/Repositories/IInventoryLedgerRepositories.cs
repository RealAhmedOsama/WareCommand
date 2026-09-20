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

public interface IInventoryCommandIdempotencyRepository
{
    Task<InventoryCommandIdempotency?> GetAsync(
        string callerScope,
        string commandKey,
        CancellationToken cancellationToken = default);

    Task<InventoryCommandIdempotency?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<InventoryCommandIdempotency> AddAsync(
        InventoryCommandIdempotency command,
        CancellationToken cancellationToken = default);

    Task<int> PruneAsync(
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default);
}

public interface IInventoryReservationRepository
{
    Task<InventoryReservation?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<InventoryReservation?> GetByDemandAsync(
        string demandKey,
        int warehouseId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryReservation>> GetExpiredAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<InventoryReservation> AddAsync(
        InventoryReservation reservation,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationAllocation> AddAllocationAsync(
        InventoryReservationAllocation allocation,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationEvent> AddEventAsync(
        InventoryReservationEvent reservationEvent,
        CancellationToken cancellationToken = default);
}
