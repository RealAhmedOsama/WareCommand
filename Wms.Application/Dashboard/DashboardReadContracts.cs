using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.UseCases.Reports;

namespace Wms.Application.Dashboard;

public enum DashboardSectionStatus
{
    Available,
    Forbidden,
    Unavailable
}

public enum DashboardWarehouseScope
{
    SelectedWarehouse,
    AllAuthorizedWarehouses
}

/// <summary>
/// A dashboard section reports whether its source was available separately
/// from its value, so a source failure is never represented as zero data.
/// </summary>
public sealed record DashboardReadSection<T>(
    DashboardSectionStatus Status,
    T? Data,
    string? ErrorCode);

public sealed record DashboardCatalogMetricsDto(
    int TotalItems,
    int ActiveItems);

/// <summary>
/// One server-authorized, uncached dashboard read. A null WarehouseId means
/// all warehouses in the caller's authorized scope; a selected warehouse is
/// reauthorized by every underlying data source.
/// </summary>
public sealed record DashboardReadSnapshot(
    int? WarehouseId,
    DashboardWarehouseScope WarehouseScope,
    string TimeZoneId,
    DateOnly BusinessDate,
    DateTimeOffset GeneratedAtUtc,
    int RecentMovementPeriodDays,
    int RefreshIntervalSeconds,
    IReadOnlyList<WmsWarehouseOption> AvailableWarehouses,
    DashboardReadSection<DashboardCatalogMetricsDto> Catalog,
    DashboardReadSection<InventoryDashboardMetricsDto> Inventory,
    DashboardReadSection<IReadOnlyList<InventoryReplenishmentSignalDto>> LowStockSignals,
    DashboardReadSection<IReadOnlyList<MovementReportDto>> RecentMovements);

public interface IDashboardReadService
{
    Task<Result<DashboardReadSnapshot>> GetAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);
}
