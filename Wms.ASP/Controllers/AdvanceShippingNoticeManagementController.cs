using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Suppliers;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.AdvanceShippingNoticesRead)]
public sealed class AdvanceShippingNoticeManagementController(
    IAdvanceShippingNoticeService advanceShippingNoticeService,
    ISupplierManagementService supplierManagementService,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        int? warehouseId,
        int? supplierId,
        AdvanceShippingNoticeStatus? status,
        bool includeCancelled = true,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await advanceShippingNoticeService.ListAsync(
            new AdvanceShippingNoticeListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                IncludeCancelled: includeCancelled,
                Page: page,
                PageSize: pageSize),
            cancellationToken);
        var model = new AdvanceShippingNoticeListViewModel
        {
            Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
                WmsPermissions.AdvanceShippingNoticesRead,
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

        return View(new AdvanceShippingNoticeListViewModel
        {
            AdvanceShippingNotices = result.Value.AdvanceShippingNotices,
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
        var result = await advanceShippingNoticeService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default) =>
        View(await WithOptionsAsync(new AdvanceShippingNoticeFormViewModel(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        AdvanceShippingNoticeFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        AdvanceShippingNoticeInput input;
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

        var result = await advanceShippingNoticeService.CreateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("AdvanceShippingNotices.Created", result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await advanceShippingNoticeService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(await WithOptionsAsync(
            AdvanceShippingNoticeFormViewModel.From(result.Value),
            cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        AdvanceShippingNoticeFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        AdvanceShippingNoticeInput input;
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

        var result = await advanceShippingNoticeService.UpdateAsync(
            model.Id,
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("AdvanceShippingNotices.Updated", result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Submit(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.SubmitAsync(id, currentUser.RequireUserId(), cancellationToken),
            "AdvanceShippingNotices.Submitted");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkExpected(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.MarkExpectedAsync(id, currentUser.RequireUserId(), cancellationToken),
            "AdvanceShippingNotices.Expected");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Arrive(int id, int? dockLocationId, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.ArriveAsync(
                id,
                dockLocationId,
                currentUser.RequireUserId(),
                cancellationToken),
            "AdvanceShippingNotices.Arrived");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkException(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.MarkExceptionAsync(id, currentUser.RequireUserId(), cancellationToken),
            "AdvanceShippingNotices.Exception");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.CompleteAsync(id, currentUser.RequireUserId(), cancellationToken),
            "AdvanceShippingNotices.Completed");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => advanceShippingNoticeService.CancelAsync(id, currentUser.RequireUserId(), cancellationToken),
            "AdvanceShippingNotices.Cancelled");

    [HttpGet]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    public IActionResult Import() => View(new AdvanceShippingNoticeImportViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.AdvanceShippingNoticesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        AdvanceShippingNoticeImportViewModel model,
        CancellationToken cancellationToken = default)
    {
        var csv = await ReadCsvAsync(model, cancellationToken);
        if (csv is null)
        {
            ModelState.AddModelError(nameof(model.Csv), this.Localize("AdvanceShippingNotices.ImportRequired"));
            return View(model);
        }

        var result = await advanceShippingNoticeService.ImportAsync(
            csv,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize(
            "AdvanceShippingNotices.Imported",
            result.Value.ImportedCount,
            result.Value.Errors.Count);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        int? warehouseId,
        int? supplierId,
        AdvanceShippingNoticeStatus? status,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await advanceShippingNoticeService.ExportAsync(
            new AdvanceShippingNoticeListQuery(warehouseId, supplierId, status, searchTerm, PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "advance-shipping-notices.csv");
    }

    private async Task<IActionResult> RunLifecycleAsync(
        Func<Task<Result<AdvanceShippingNoticeDto>>> operation,
        string messageKey)
    {
        var result = await operation();
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = this.Localize(messageKey, result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    private async Task<AdvanceShippingNoticeFormViewModel> WithOptionsAsync(
        AdvanceShippingNoticeFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            cancellationToken);
        var suppliers = await supplierManagementService.ListAsync(
            new SupplierListQuery(Page: 1, PageSize: 200),
            cancellationToken);
        model.Suppliers = suppliers.IsSuccess
            ? suppliers.Value.Suppliers
                .Select(supplier => new AdvanceShippingNoticeSupplierOption(
                    supplier.Id,
                    supplier.Code,
                    supplier.LegalName,
                    supplier.IsActive))
                .ToArray()
            : [];
        return model;
    }

    private static async Task<string?> ReadCsvAsync(
        AdvanceShippingNoticeImportViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.File is not null)
        {
            if (model.File.Length == 0 || model.File.Length > 2_000_000)
            {
                return null;
            }

            await using var stream = model.File.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        return string.IsNullOrWhiteSpace(model.Csv) ? null : model.Csv;
    }
}
