using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.Purchasing;
using Wms.Application.Suppliers;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.PurchaseOrdersRead)]
public sealed class PurchaseOrderManagementController(
    IPurchaseOrderService purchaseOrderService,
    ISupplierManagementService supplierManagementService,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        int? warehouseId,
        int? supplierId,
        PurchaseOrderStatus? status,
        bool includeCancelled = true,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await purchaseOrderService.ListAsync(
            new PurchaseOrderListQuery(
                warehouseId,
                supplierId,
                status,
                searchTerm,
                IncludeCancelled: includeCancelled,
                Page: page,
                PageSize: pageSize),
            cancellationToken);
        var model = new PurchaseOrderListViewModel
        {
            Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
                WmsPermissions.PurchaseOrdersRead,
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

        return View(new PurchaseOrderListViewModel
        {
            PurchaseOrders = result.Value.PurchaseOrders,
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
        var result = await purchaseOrderService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default) =>
        View(await WithOptionsAsync(new PurchaseOrderFormViewModel(), cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        PurchaseOrderFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        PurchaseOrderInput input;
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

        var result = await purchaseOrderService.CreateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithOptionsAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("PurchaseOrders.Created", result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await purchaseOrderService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(await WithOptionsAsync(
            PurchaseOrderFormViewModel.From(result.Value),
            cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        PurchaseOrderFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        PurchaseOrderInput input;
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

        var result = await purchaseOrderService.UpdateAsync(
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

        TempData["SuccessMessage"] = this.Localize("PurchaseOrders.Updated", result.Value.DocumentNumber);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Confirm(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => purchaseOrderService.ConfirmAsync(id, currentUser.RequireUserId(), cancellationToken),
            "PurchaseOrders.Confirmed");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => purchaseOrderService.CancelAsync(id, currentUser.RequireUserId(), cancellationToken),
            "PurchaseOrders.Cancelled");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Close(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => purchaseOrderService.CloseAsync(id, currentUser.RequireUserId(), cancellationToken),
            "PurchaseOrders.Closed");

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reopen(int id, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync(
            () => purchaseOrderService.ReopenAsync(id, currentUser.RequireUserId(), cancellationToken),
            "PurchaseOrders.Reopened");

    [HttpGet]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    public IActionResult Import() => View(new PurchaseOrderImportViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.PurchaseOrdersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        PurchaseOrderImportViewModel model,
        CancellationToken cancellationToken = default)
    {
        var csv = await ReadCsvAsync(model, cancellationToken);
        if (csv is null)
        {
            ModelState.AddModelError(nameof(model.Csv), this.Localize("PurchaseOrders.ImportRequired"));
            return View(model);
        }

        var result = await purchaseOrderService.ImportAsync(
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
            "PurchaseOrders.Imported",
            result.Value.ImportedCount,
            result.Value.Errors.Count);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        int? warehouseId,
        int? supplierId,
        PurchaseOrderStatus? status,
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await purchaseOrderService.ExportAsync(
            new PurchaseOrderListQuery(warehouseId, supplierId, status, searchTerm, PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "purchase-orders.csv");
    }

    private async Task<IActionResult> RunLifecycleAsync(
        Func<Task<Wms.Application.Common.Result<PurchaseOrderDto>>> operation,
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

    private async Task<PurchaseOrderFormViewModel> WithOptionsAsync(
        PurchaseOrderFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.PurchaseOrdersManage,
            cancellationToken);
        var suppliers = await supplierManagementService.ListAsync(
            new SupplierListQuery(Page: 1, PageSize: 200),
            cancellationToken);
        model.Suppliers = suppliers.IsSuccess
            ? suppliers.Value.Suppliers
                .Select(supplier => new PurchaseOrderSupplierOption(
                    supplier.Id,
                    supplier.Code,
                    supplier.LegalName,
                    supplier.IsActive))
                .ToArray()
            : [];
        return model;
    }

    private static async Task<string?> ReadCsvAsync(
        PurchaseOrderImportViewModel model,
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
