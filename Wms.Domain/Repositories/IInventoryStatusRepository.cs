using Wms.Domain.Entities;

namespace Wms.Domain.Repositories;

public interface IInventoryStatusRepository : IRepository<InventoryStatus>
{
    Task<InventoryStatus?> GetByCodeAsync(
        string code,
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<InventoryStatus>> GetForWarehouseAsync(
        int? warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<InventoryStatusTransition?> GetTransitionAsync(
        int fromStatusId,
        int toStatusId,
        CancellationToken cancellationToken = default);

    Task<InventoryStatusTransition> AddTransitionAsync(
        InventoryStatusTransition transition,
        CancellationToken cancellationToken = default);

    Task<bool> IsInUseAsync(int statusId, CancellationToken cancellationToken = default);
}
