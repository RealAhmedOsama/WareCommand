using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Dashboard;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Items;
using Wms.Application.Reporting;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.UseCases.Reports;

namespace Wms.Infrastructure.Dashboard;

/// <summary>
/// Composes the dashboard from the application's authorized read models.
/// The service deliberately keeps no metric cache so each request observes
/// persisted changes from the inventory ledger and movement report queries.
/// </summary>
public sealed class DashboardReadService(
    IWarehouseAccessService warehouseAccessService,
    IItemManagementService itemManagementService,
    IInventoryInquiryService inventoryInquiryService,
    IInventoryReplenishmentPolicyService replenishmentPolicyService,
    IReportQueryService reportQueryService,
    IWmsSettingsService settingsService,
    IClock clock,
    ILogger<DashboardReadService> logger) : IDashboardReadService
{
    private const int MaximumRecentMovements = 200;

    public async Task<Result<DashboardReadSnapshot>> GetAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dashboardAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.DashboardView,
                warehouseId,
                cancellationToken);
            if (dashboardAuthorization.IsFailure)
            {
                return dashboardAuthorization.ToFailure<DashboardReadSnapshot>();
            }

            var accessibleWarehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
                WmsPermissions.DashboardView,
                cancellationToken);
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var effectiveWarehouseId = warehouseId;
            if (!effectiveWarehouseId.HasValue &&
                !scope.HasGlobalAccess &&
                accessibleWarehouses.Count == 1)
            {
                effectiveWarehouseId = accessibleWarehouses[0].Id;
            }

            var selectedWarehouse = effectiveWarehouseId.HasValue
                ? accessibleWarehouses.FirstOrDefault(warehouse =>
                    warehouse.Id == effectiveWarehouseId.Value)
                : null;
            var settingsResult = await settingsService.GetAsync(
                effectiveWarehouseId,
                cancellationToken);
            var settingsValues = settingsResult.IsSuccess
                ? settingsResult.Value.Values
                : WmsSettingsDefaults.Create();

            var requestedTimeZone = selectedWarehouse?.TimeZoneId ??
                                    settingsValues.Localization.TimeZone;
            var validTimeZone = WmsTimeZoneCatalog.TryNormalize(
                requestedTimeZone,
                out var timeZoneId);
            if (!validTimeZone)
            {
                timeZoneId = "UTC";
                logger.LogWarning(
                    "Dashboard time zone {TimeZoneId} is invalid; time-dependent sections will be unavailable",
                    requestedTimeZone);
            }

            var generatedAtUtc = clock.UtcNow.ToUniversalTime();
            var businessDate = WmsBusinessTime.GetBusinessDate(
                generatedAtUtc,
                timeZoneId);

            var itemCountsResult = await itemManagementService.GetDashboardCountsAsync(
                cancellationToken);
            var catalog = itemCountsResult.IsSuccess
                ? Available(new DashboardCatalogMetricsDto(
                    itemCountsResult.Value.TotalItems,
                    itemCountsResult.Value.ActiveItems))
                : Failed<DashboardCatalogMetricsDto>(itemCountsResult);

            DashboardReadSection<InventoryDashboardMetricsDto> inventory;
            DashboardReadSection<IReadOnlyList<InventoryReplenishmentSignalDto>> lowStockSignals;
            DashboardReadSection<IReadOnlyList<MovementReportDto>> recentMovements;

            if (settingsResult.IsFailure || !validTimeZone)
            {
                var errorCode = settingsResult.IsFailure
                    ? settingsResult.ErrorCode
                    : "dashboard.time_zone_invalid";
                inventory = Unavailable<InventoryDashboardMetricsDto>(errorCode);
                lowStockSignals = Unavailable<IReadOnlyList<InventoryReplenishmentSignalDto>>(errorCode);
                recentMovements = Unavailable<IReadOnlyList<MovementReportDto>>(errorCode);
            }
            else
            {
                var inventoryResult = await inventoryInquiryService.GetDashboardMetricsAsync(
                    new InventoryInquiryQuery(WarehouseId: effectiveWarehouseId),
                    businessDate,
                    settingsValues.Expiry.WarningDays,
                    cancellationToken);
                inventory = Section(inventoryResult);

                var signalResult = await replenishmentPolicyService.GetSignalsAsync(
                    new InventoryReplenishmentSignalQuery(
                        WarehouseId: effectiveWarehouseId,
                        Limit: settingsValues.Dashboard.LowStockAlertLimit),
                    cancellationToken);
                lowStockSignals = Section(signalResult);

                var movementLimit = Math.Clamp(
                    settingsValues.Dashboard.RecentMovementLimit,
                    1,
                    MaximumRecentMovements);
                var movementResult = await reportQueryService.QueryMovementLedgerAsync(
                    new MovementLedgerQuery(
                        FromDate: businessDate.AddDays(-settingsValues.Reports.DefaultPeriodDays),
                        ToDate: businessDate,
                        WarehouseId: effectiveWarehouseId,
                        Sort: MovementReportSort.Timestamp,
                        Descending: true,
                        Page: 1,
                        PageSize: movementLimit,
                        BusinessTimeZoneId: timeZoneId),
                    cancellationToken);
                recentMovements = movementResult.IsSuccess
                    ? Available(movementResult.Value.Items)
                    : Failed<IReadOnlyList<MovementReportDto>>(movementResult);
            }

            return Result.Success(new DashboardReadSnapshot(
                effectiveWarehouseId,
                effectiveWarehouseId.HasValue
                    ? DashboardWarehouseScope.SelectedWarehouse
                    : DashboardWarehouseScope.AllAuthorizedWarehouses,
                timeZoneId,
                businessDate,
                clock.UtcNow.ToUniversalTime(),
                settingsValues.Reports.DefaultPeriodDays,
                settingsValues.Dashboard.RefreshIntervalSeconds,
                accessibleWarehouses,
                catalog,
                inventory,
                lowStockSignals,
                recentMovements));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dashboard snapshot could not be assembled");
            return Result.Failure<DashboardReadSnapshot>(WmsErrors.FromException(
                exception,
                "dashboard.read_failed",
                "Dashboard data could not be loaded. Please try again."));
        }
    }

    private static DashboardReadSection<T> Section<T>(Result<T> result) =>
        result.IsSuccess
            ? Available(result.Value)
            : Failed<T>(result);

    private static DashboardReadSection<T> Failed<T>(Result result)
    {
        var errorCode = result.ErrorCode;
        var status = errorCode.StartsWith("authorization.", StringComparison.Ordinal)
            ? DashboardSectionStatus.Forbidden
            : DashboardSectionStatus.Unavailable;
        return new DashboardReadSection<T>(status, default, errorCode);
    }

    private static DashboardReadSection<T> Available<T>(T value) =>
        new(DashboardSectionStatus.Available, value, null);

    private static DashboardReadSection<T> Unavailable<T>(string errorCode) =>
        new(DashboardSectionStatus.Unavailable, default, errorCode);
}
