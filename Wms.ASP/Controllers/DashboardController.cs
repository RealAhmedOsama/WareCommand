using Microsoft.AspNetCore.Mvc;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Reports;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

public class DashboardController : Controller
{
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IGetStockUseCase _getStockUseCase;
    private readonly ILogger<DashboardController> _logger;
    private readonly IMovementReportUseCase _movementReportUseCase;

    public DashboardController(
        IGetStockUseCase getStockUseCase,
        IGetItemsUseCase getItemsUseCase,
        IMovementReportUseCase movementReportUseCase,
        ILogger<DashboardController> logger)
    {
        _getStockUseCase = getStockUseCase;
        _getItemsUseCase = getItemsUseCase;
        _movementReportUseCase = movementReportUseCase;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var model = new DashboardViewModel();

        try
        {
            // Load KPI data
            var itemsResult = await _getItemsUseCase.ExecuteAsync();
            if (itemsResult.IsSuccess)
            {
                var items = itemsResult.Value.ToList();
                model.TotalItems = items.Count;
                model.ActiveItems = items.Count(i => i.IsActive);
            }
            else
            {
                _logger.LogWarning("Failed to load items: {Error}", itemsResult.Error);
            }

            // Stock Summary
            var stockSummaryResult = await _getStockUseCase.GetStockSummaryAsync();
            if (stockSummaryResult.IsSuccess)
            {
                var summary = stockSummaryResult.Value.ToList();
                model.TotalSKUs = summary.Count;
                model.TotalStockValue = summary.Sum(s => s.TotalQuantity);
            }
            else
            {
                _logger.LogWarning("Failed to load stock summary: {Error}", stockSummaryResult.Error);
            }

            // Stock Locations
            var allStockResult = await _getStockUseCase.GetAllStockAsync();
            if (allStockResult.IsSuccess)
            {
                model.StockLocations = allStockResult.Value.Select(s => s.LocationId).Distinct().Count();

                // Low stock alerts
                model.LowStockItems = allStockResult.Value
                    .Where(s => s.AvailableQuantity < 10)
                    .OrderBy(s => s.AvailableQuantity)
                    .Take(10)
                    .ToList();
            }
            else
            {
                _logger.LogWarning("Failed to load stock data: {Error}", allStockResult.Error);
            }

            // Recent movements
            var utcToday = DateTime.UtcNow.Date;
            var request = new MovementReportRequest(
                utcToday.AddDays(-7),
                DateTime.UtcNow
            );

            var movementsResult = await _movementReportUseCase.ExecuteAsync(request);
            if (movementsResult.IsSuccess)
            {
                model.RecentMovements = movementsResult.Value
                    .OrderByDescending(m => m.Timestamp)
                    .Take(10)
                    .ToList();
            }
            else
            {
                _logger.LogWarning("Failed to load recent movements: {Error}", movementsResult.Error);
            }

            model.LastRefresh = DateTime.UtcNow;
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard data");
            TempData["ErrorMessage"] = "Error loading dashboard data. Please try again.";
            model.LastRefresh = DateTime.UtcNow;
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> RefreshData()
    {
        return Json(new { success = true, message = "Dashboard refreshed", timestamp = DateTime.UtcNow });
    }
}
