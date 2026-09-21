using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Reports;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.DashboardView)]
public class DashboardController : Controller
{
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IInventoryInquiryService _inventoryInquiryService;
    private readonly IInventoryReplenishmentPolicyService _replenishmentPolicyService;
    private readonly IClock _clock;
    private readonly ILogger<DashboardController> _logger;
    private readonly IMovementReportUseCase _movementReportUseCase;
    private readonly IWmsSettingsService _settingsService;

    public DashboardController(
        IInventoryInquiryService inventoryInquiryService,
        IInventoryReplenishmentPolicyService replenishmentPolicyService,
        IGetItemsUseCase getItemsUseCase,
        IMovementReportUseCase movementReportUseCase,
        ILogger<DashboardController> logger,
        IWmsSettingsService settingsService,
        IClock clock)
    {
        _inventoryInquiryService = inventoryInquiryService;
        _replenishmentPolicyService = replenishmentPolicyService;
        _getItemsUseCase = getItemsUseCase;
        _movementReportUseCase = movementReportUseCase;
        _logger = logger;
        _settingsService = settingsService;
        _clock = clock;
    }

    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var model = new DashboardViewModel();
        var settingsResult = await _settingsService.GetAsync(cancellationToken: cancellationToken);
        var settingsValues = settingsResult.IsSuccess
            ? settingsResult.Value.Values
            : WmsSettingsDefaults.Create();
        var dashboardSettings = settingsValues.Dashboard;
        if (settingsResult.IsFailure)
        {
            _logger.LogWarning(
                "Dashboard settings could not be loaded: {ErrorCode}",
                settingsResult.ErrorCode);
        }

        model.LowStockThreshold = dashboardSettings.LowStockThreshold;
        model.LowStockAlertLimit = dashboardSettings.LowStockAlertLimit;
        model.RecentMovementPeriodDays = settingsValues.Reports.DefaultPeriodDays;
        model.DashboardRefreshIntervalSeconds = dashboardSettings.RefreshIntervalSeconds;
        model.DisplayTimeZone = settingsValues.Localization.TimeZone;

        var nowUtc = _clock.UtcNow;
        var businessToday = WmsBusinessTime.GetBusinessDate(
            nowUtc,
            settingsValues.Localization.TimeZone);

        // Load KPI data
        var itemsResult = await _getItemsUseCase.ExecuteAsync(cancellationToken: cancellationToken);
        if (itemsResult.IsSuccess)
        {
            var items = itemsResult.Value.ToList();
            model.TotalItems = items.Count;
            model.ActiveItems = items.Count(i => i.IsActive);
        }
        else
        {
            _logger.LogWarning("Failed to load items: {ErrorCode}", itemsResult.ErrorCode);
        }

        // Inventory KPIs are aggregated by the canonical inquiry read model.
        // The query remains warehouse-scoped by the service and never loads all
        // stock rows into the controller process.
        var dashboardMetricsResult = await _inventoryInquiryService.GetDashboardMetricsAsync(
            new InventoryInquiryQuery(),
            businessToday,
            settingsValues.Expiry.WarningDays,
            cancellationToken);
        if (dashboardMetricsResult.IsSuccess)
        {
            var metrics = dashboardMetricsResult.Value;
            model.TotalSKUs = metrics.StockKeepingUnits;
            model.TotalOnHandUnits = metrics.OnHandQuantity;
            model.ReservedUnits = metrics.ReservedQuantity;
            model.AvailableUnits = metrics.AvailableQuantity;
            model.HeldUnits = metrics.HeldQuantity;
            model.DamagedUnits = metrics.DamagedQuantity;
            model.ExpiredUnits = metrics.ExpiredQuantity;
            model.ExpiringUnits = metrics.ExpiringQuantity;
            model.StockLocations = metrics.StockLocations;
        }
        else
        {
            _logger.LogWarning(
                "Failed to load operational inventory metrics: {ErrorCode}",
                dashboardMetricsResult.ErrorCode);
        }

        // Replenishment signals are policy-driven; the legacy global threshold is
        // retained only for settings compatibility and is not used for decisions.
        var signalsResult = await _replenishmentPolicyService.GetSignalsAsync(
            new InventoryReplenishmentSignalQuery(Limit: dashboardSettings.LowStockAlertLimit),
            cancellationToken);
        if (signalsResult.IsSuccess)
        {
            model.LowStockSignals = signalsResult.Value.ToList();
        }
        else
        {
            _logger.LogWarning(
                "Failed to load replenishment signals: {ErrorCode}",
                signalsResult.ErrorCode);
        }

        // Recent movements
        var request = new MovementReportRequest(
            businessToday.AddDays(-settingsValues.Reports.DefaultPeriodDays)
                .ToDateTime(TimeOnly.MinValue),
            businessToday.ToDateTime(TimeOnly.MinValue)
        );

        var movementsResult = await _movementReportUseCase.ExecuteAsync(request, cancellationToken);
        if (movementsResult.IsSuccess)
        {
            model.RecentMovements = movementsResult.Value
                .OrderByDescending(m => m.Timestamp)
                .Take(dashboardSettings.RecentMovementLimit)
                .ToList();
        }
        else
        {
            _logger.LogWarning("Failed to load recent movements: {ErrorCode}", movementsResult.ErrorCode);
        }

        model.LastRefresh = WmsBusinessTime.ToLocalDateTime(
            nowUtc.UtcDateTime,
            settingsValues.Localization.TimeZone);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> RefreshData()
    {
        return Json(new { success = true, message = "Dashboard refreshed", timestamp = _clock.UtcNow });
    }
}
