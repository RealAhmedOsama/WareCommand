using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Identity;
using Wms.Application.UseCases.Inventory;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.InventoryRead)]
public class InventoryController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IGetStockUseCase _getStockUseCase;
    private readonly IStockAdjustmentUseCase _stockAdjustmentUseCase;

    public InventoryController(
        IGetStockUseCase getStockUseCase,
        IStockAdjustmentUseCase stockAdjustmentUseCase,
        ICurrentUser currentUser)
    {
        _getStockUseCase = getStockUseCase;
        _stockAdjustmentUseCase = stockAdjustmentUseCase;
        _currentUser = currentUser;
    }

    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Index(
        string? searchTerm,
        bool showSummary = false,
        CancellationToken cancellationToken = default)
    {
        var model = new InventoryViewModel
        {
            SearchTerm = searchTerm,
            ShowSummary = showSummary
        };

        if (showSummary)
        {
            var summaryResult = await _getStockUseCase.GetStockSummaryAsync(cancellationToken);
            if (summaryResult.IsSuccess)
            {
                model.StockSummary = summaryResult.Value.OrderBy(s => s.ItemSku).ToList();
            }
        }
        else
        {
            var result = await _getStockUseCase.GetAllStockAsync(cancellationToken);
            if (result.IsSuccess)
            {
                var stockItems = result.Value.Where(s => s.AvailableQuantity > 0);

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    stockItems = stockItems.Where(s =>
                        s.ItemSku.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        s.ItemName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        s.LocationCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
                }

                model.StockItems = stockItems.ToList();
            }
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    public IActionResult Adjust(string itemSku, string locationCode, decimal currentQuantity)
    {
        var model = new StockAdjustmentViewModel
        {
            ItemSku = itemSku,
            LocationCode = locationCode,
            CurrentQuantity = currentQuantity,
            NewQuantity = currentQuantity
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    public async Task<IActionResult> Adjust(
        [Bind("ItemSku,LocationCode,NewQuantity,Reason")] StockAdjustmentViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new StockAdjustmentDto(
            model.ItemSku,
            model.LocationCode,
            model.NewQuantity,
            model.Reason
        );

        var result = await _stockAdjustmentUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Inventory.Adjusted", result.Value.MovementId);
        return RedirectToAction(nameof(Index));
    }
}
