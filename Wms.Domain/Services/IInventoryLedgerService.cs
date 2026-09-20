using Wms.Domain.Entities;
using Wms.Domain.Inventory;

namespace Wms.Domain.Services;

public interface IInventoryLedgerService
{
    Task<IReadOnlyList<InventoryTransaction>> RecordAsync(
        IReadOnlyCollection<InventoryLedgerEntryRequest> entries,
        CancellationToken cancellationToken = default);

    Task<InventoryBalance?> GetBalanceAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default);

    Task<InventoryReconciliationReport> ReconcileAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);
}
