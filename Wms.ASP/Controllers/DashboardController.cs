using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Reports;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.DashboardView)]
public class DashboardController : Controller
{
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IGetStockUseCase _getStockUseCase;
    private readonly ILogger<DashboardController> _logger;
    private readonly IMovementReportUseCase _movementReportUseCase;
    private readonly IWmsSettingsService _settingsService;

    public DashboardController(
        IGetStockUseCase getStockUseCase,
        IGetItemsUseCase getItemsUseCase,
        IMovementReportUseCase movementReportUseCase,
        ILogger<DashboardController> logger,
        IWmsSettingsService settingsService)
    {
        _getStockUseCase = getStockUseCase;
        _getItemsUseCase = getItemsUseCase;
        _movementReportUseCase = movementReportUseCase;
        _logger = logger;
        _settingsService = settingsService;
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

        // Stock Summary
        var stockSummaryResult = await _getStockUseCase.GetStockSummaryAsync(cancellationToken);
        if (stockSummaryResult.IsSuccess)
        {
            var summary = stockSummaryResult.Value.ToList();
            model.TotalSKUs = summary.Count;
            model.TotalStockValue = summary.Sum(s => s.TotalQuantity);
        }
        else
        {
            _logger.LogWarning("Failed to load stock summary: {ErrorCode}", stockSummaryResult.ErrorCode);
        }

        // Stock Locations
        var allStockResult = await _getStockUseCase.GetAllStockAsync(cancellationToken);
        if (allStockResult.IsSuccess)
        {
            model.StockLocations = allStockResult.Value.Select(s => s.LocationId).Distinct().Count();

            // Low stock alerts
            model.LowStockItems = allStockResult.Value
                .Where(s => s.AvailableQuantity < dashboardSettings.LowStockThreshold)
                .OrderBy(s => s.AvailableQuantity)
                .Take(dashboardSettings.LowStockAlertLimit)
                .ToList();
        }
        else
        {
            _logger.LogWarning("Failed to load stock data: {ErrorCode}", allStockResult.ErrorCode);
        }

        // Recent movements
        var utcToday = DateTime.UtcNow.Date;
        var request = new MovementReportRequest(
            utcToday.AddDays(-settingsValues.Reports.DefaultPeriodDays),
            DateTime.UtcNow
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

        model.LastRefresh = DateTime.UtcNow;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> RefreshData()
    {
        return Json(new { success = true, message = "Dashboard refreshed", timestamp = DateTime.UtcNow });
    }
}
