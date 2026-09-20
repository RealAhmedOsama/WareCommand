using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.UseCases.Inventory;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.InventoryRead)]
public class InventoryController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IInventoryInquiryService _inventoryInquiryService;
    private readonly IStockAdjustmentUseCase _stockAdjustmentUseCase;

    public InventoryController(
        IInventoryInquiryService inventoryInquiryService,
        IStockAdjustmentUseCase stockAdjustmentUseCase,
        ICurrentUser currentUser)
    {
        _inventoryInquiryService = inventoryInquiryService;
        _stockAdjustmentUseCase = stockAdjustmentUseCase;
        _currentUser = currentUser;
    }

    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    public async Task<IActionResult> Index(
        string? searchTerm,
        bool showSummary = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var model = new InventoryViewModel
        {
            SearchTerm = searchTerm,
            ShowSummary = showSummary
        };

        if (showSummary)
        {
            var summaryResult = await _inventoryInquiryService.SummarizeAsync(
                new InventoryInquiryQuery(SearchTerm: searchTerm),
                cancellationToken);
            if (summaryResult.IsSuccess)
            {
                model.StockSummary = summaryResult.Value.ToList();
            }
        }
        else
        {
            var result = await _inventoryInquiryService.QueryAsync(
                new InventoryInquiryQuery(
                    SearchTerm: searchTerm,
                    AvailableOnly: true,
                    Page: page,
                    PageSize: pageSize),
                cancellationToken);
            if (result.IsSuccess)
            {
                model.StockItems = result.Value.Items.ToList();
                model.StockPage = result.Value.Page;
                model.StockPageSize = result.Value.PageSize;
                model.StockTotalCount = result.Value.TotalCount;
                model.StockTotalPages = result.Value.TotalPages;
            }
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    public IActionResult Adjust(
        string itemSku,
        string locationCode,
        decimal currentQuantity,
        string? lotNumber = null)
    {
        var model = new StockAdjustmentViewModel
        {
            ItemSku = itemSku,
            LocationCode = locationCode,
            LotNumber = lotNumber,
            CurrentQuantity = currentQuantity,
            NewQuantity = currentQuantity
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    public async Task<IActionResult> Adjust(
        [Bind("ItemSku,LocationCode,LotNumber,LicensePlateId,NewQuantity,UnitOfMeasure,PackagingCode,Reason")] StockAdjustmentViewModel model,
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
            model.Reason,
            model.LotNumber,
            UnitOfMeasure: model.UnitOfMeasure,
            PackagingCode: model.PackagingCode,
            LicensePlateId: model.LicensePlateId
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
