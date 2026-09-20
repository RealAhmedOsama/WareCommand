using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Warehouses;
using Wms.ASP.Extensions;
using Wms.ASP.Middleware;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize]
public sealed class WarehousesController(
    IWarehouseManagementService warehouseService,
    IWarehouseAccessService warehouseAccessService,
    IClock clock) : Controller
{
    [HttpGet]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Index(
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.ListAsync(includeInactive, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(new WarehouseManagementViewModel { IncludeInactive = includeInactive });
        }

        return View(new WarehouseManagementViewModel
        {
            IncludeInactive = includeInactive,
            Warehouses = result.Value
        });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public IActionResult Create() => View(new CreateWarehouseViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Create(
        [Bind("Code,Name,ArabicName,Address,ContactName,ContactPhone,ContactEmail,TimeZone,AllowNegativeStock,RequireLocationForAdjustment,BlockExpiredReceipt,ExpiryWarningDays,NextReceiptNumber,NextOrderNumber,NextWorkNumber,NextShipmentNumber,NextTransferNumber,NextCountNumber")]
        CreateWarehouseViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await warehouseService.CreateAsync(ToRequest(model), cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Warehouses.Created", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Edit(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(ToEditModel(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Edit(
        [Bind("Id,Name,ArabicName,Address,ContactName,ContactPhone,ContactEmail,TimeZone,AllowNegativeStock,RequireLocationForAdjustment,BlockExpiredReceipt,ExpiryWarningDays,NextReceiptNumber,NextOrderNumber,NextWorkNumber,NextShipmentNumber,NextTransferNumber,NextCountNumber")]
        EditWarehouseViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await warehouseService.UpdateAsync(ToRequest(model), cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Warehouses.Updated", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Configure(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var model = ConfigureWarehouseLocationsViewModel.From(result.Value);
        await PopulateLocationsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Configure(
        [Bind("WarehouseId,ReceivingLocationId,StagingLocationId,StorageLocationId,PackingLocationId,ShippingLocationId,QuarantineLocationId,DamagedLocationId,ReturnsLocationId,TransitLocationId")]
        ConfigureWarehouseLocationsViewModel model,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.ConfigureOperationalLocationsAsync(
            model.WarehouseId,
            model.ToSelections(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            var warehouseResult = await warehouseService.GetAsync(model.WarehouseId, cancellationToken);
            if (warehouseResult.IsSuccess)
            {
                model.Warehouse = warehouseResult.Value;
            }

            await PopulateLocationsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Warehouses.OperationalLocationsSaved");
        return RedirectToAction(nameof(Details), new { id = model.WarehouseId });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> EnableWorkflows(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.EnableWorkflowsAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Warehouses.WorkflowsEnabled");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Activate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.ActivateAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Warehouses.Activated");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.WarehouseManage)]
    public async Task<IActionResult> Deactivate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await warehouseService.DeactivateAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Warehouses.Deactivated");
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Select(
        int id,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.DashboardView,
            cancellationToken);
        if (!accessible.Any(warehouse => warehouse.Id == id))
        {
            TempData["ErrorMessage"] = this.Localize("Warehouses.SelectionDenied");
            return RedirectToLocal(returnUrl);
        }

        Response.Cookies.Append(
            WarehouseContextMiddleware.CookieName,
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new CookieOptions
            {
                Expires = clock.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
        return RedirectToLocal(returnUrl);
    }

    private async Task PopulateLocationsAsync(
        ConfigureWarehouseLocationsViewModel model,
        CancellationToken cancellationToken)
    {
        var result = await warehouseService.ListLocationOptionsAsync(
            model.WarehouseId,
            cancellationToken);
        if (result.IsSuccess)
        {
            model.Locations = result.Value
                .Select(location => new WarehouseLocationOptionViewModel(
                    location.Id,
                    location.Code,
                    location.Name,
                    location.IsActive))
                .ToArray();
        }
    }

    private static CreateWarehouseRequest ToRequest(CreateWarehouseViewModel model) =>
        new(
            model.Code,
            model.Name,
            model.ArabicName,
            model.Address,
            model.ContactName,
            model.ContactPhone,
            model.ContactEmail,
            model.TimeZone,
            model.AllowNegativeStock,
            model.RequireLocationForAdjustment,
            model.BlockExpiredReceipt,
            model.ExpiryWarningDays,
            model.NextReceiptNumber,
            model.NextOrderNumber,
            model.NextWorkNumber,
            model.NextShipmentNumber,
            model.NextTransferNumber,
            model.NextCountNumber);

    private static UpdateWarehouseRequest ToRequest(EditWarehouseViewModel model) =>
        new(
            model.Id,
            model.Name,
            model.ArabicName,
            model.Address,
            model.ContactName,
            model.ContactPhone,
            model.ContactEmail,
            model.TimeZone,
            model.AllowNegativeStock,
            model.RequireLocationForAdjustment,
            model.BlockExpiredReceipt,
            model.ExpiryWarningDays,
            model.NextReceiptNumber,
            model.NextOrderNumber,
            model.NextWorkNumber,
            model.NextShipmentNumber,
            model.NextTransferNumber,
            model.NextCountNumber);

    private static EditWarehouseViewModel ToEditModel(WarehouseDto warehouse) =>
        new()
        {
            Id = warehouse.Id,
            Code = warehouse.Code,
            Name = warehouse.Name,
            ArabicName = warehouse.ArabicName,
            Address = warehouse.Address,
            ContactName = warehouse.ContactName,
            ContactPhone = warehouse.ContactPhone,
            ContactEmail = warehouse.ContactEmail,
            TimeZone = warehouse.TimeZone,
            AllowNegativeStock = warehouse.AllowNegativeStock,
            RequireLocationForAdjustment = warehouse.RequireLocationForAdjustment,
            BlockExpiredReceipt = warehouse.BlockExpiredReceipt,
            ExpiryWarningDays = warehouse.ExpiryWarningDays,
            NextReceiptNumber = warehouse.NumberSequence.NextReceiptNumber,
            NextOrderNumber = warehouse.NumberSequence.NextOrderNumber,
            NextWorkNumber = warehouse.NumberSequence.NextWorkNumber,
            NextShipmentNumber = warehouse.NumberSequence.NextShipmentNumber,
            NextTransferNumber = warehouse.NumberSequence.NextTransferNumber,
            NextCountNumber = warehouse.NumberSequence.NextCountNumber
        };

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");
}
