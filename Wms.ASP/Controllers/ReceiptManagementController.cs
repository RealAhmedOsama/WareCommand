using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.Receiving;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.ReceiptsRead)]
public sealed class ReceiptManagementController(
    IReceiptService receiptService,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        int? warehouseId,
        int? supplierId,
        ReceiptStatus? status,
        bool includeCancelled = true,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await receiptService.ListAsync(
            new ReceiptListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                IncludeCancelled: includeCancelled,
                Page: page,
                PageSize: pageSize),
            cancellationToken);
        var model = new ReceiptListViewModel
        {
            Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
                WmsPermissions.ReceiptsRead,
                cancellationToken),
            SearchTerm = searchTerm,
            WarehouseId = warehouseId,
            SupplierId = supplierId,
            Status = status,
            IncludeCancelled = includeCancelled,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 200)
        };
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        return View(new ReceiptListViewModel
        {
            Receipts = result.Value.Receipts,
            Warehouses = model.Warehouses,
            SearchTerm = model.SearchTerm,
            WarehouseId = model.WarehouseId,
            SupplierId = model.SupplierId,
            Status = model.Status,
            IncludeCancelled = model.IncludeCancelled,
            Page = result.Value.Page,
            PageSize = result.Value.PageSize,
            TotalCount = result.Value.TotalCount,
            TotalPages = result.Value.TotalPages
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var result = await receiptService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default) =>
        View(await WithOptionsAsync(new ReceiptFormViewModel(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ReceiptFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        ReceiptInput input;
        try
        {
            input = model.ToInput();
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(model.LinesText), exception.Message);
            input = default!;
        }

        if (!ModelState.IsValid)
        {
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        var result = await receiptService.CreateAsync(input, currentUser.RequireUserId(), cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Receipts.Created", result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken = default)
    {
        var result = await receiptService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken = default) =>
        await LifecycleAsync(id, () => receiptService.CompleteAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        await LifecycleAsync(id, () => receiptService.CancelAsync(id, currentUser.RequireUserId(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(int id, string reason, CancellationToken cancellationToken = default) =>
        await LifecycleAsync(id, () => receiptService.ReverseAsync(id, currentUser.RequireUserId(), reason, cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ReceiptsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(int id, string? reason, string? notes, CancellationToken cancellationToken = default)
    {
        var result = await receiptService.CorrectAsync(
            id,
            new ReceiptCorrectionInput(reason, notes),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["SuccessMessage"] = this.Localize("Receipts.Corrected", result.Value.Correction.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Correction.Id });
    }

    private async Task<IActionResult> LifecycleAsync(
        int id,
        Func<Task<Wms.Application.Common.Result<Wms.Application.Receiving.ReceiptDto>>> action)
    {
        var result = await action();
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Receipts.Updated", result.Value.DocumentNumber);
        }

        return RedirectToAction(nameof(Details), new { id = result.IsSuccess ? result.Value.Id : id });
    }

    private async Task<ReceiptFormViewModel> WithOptionsAsync(
        ReceiptFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.ReceiptsManage,
            cancellationToken);
        return model;
    }
}
