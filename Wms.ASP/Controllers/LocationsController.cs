using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Locations;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.LocationsRead)]
public sealed class LocationsController(
    ILocationManagementService locationService,
    ICurrentUser currentUser,
    IWarehouseAccessService warehouseAccessService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        int? warehouseId,
        string? searchTerm,
        LocationType? type,
        bool includeInactive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.ListAsync(
            new LocationListQuery(warehouseId, searchTerm, type, includeInactive, page, pageSize),
            cancellationToken);
        var model = new LocationManagementViewModel
        {
            WarehouseId = warehouseId,
            SearchTerm = searchTerm,
            Type = type,
            IncludeInactive = includeInactive,
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 200),
            Warehouses = await GetWarehousesAsync(WmsPermissions.LocationsRead, cancellationToken)
        };

        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        model.Locations = result.Value.Items.ToList();
        model.TotalCount = result.Value.TotalCount;
        model.TotalPages = result.Value.TotalPages;
        model.Page = result.Value.Page;
        model.PageSize = result.Value.PageSize;
        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var model = new CreateLocationViewModel();
        await PopulateLocationOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Create(
        [Bind("Code,Name,WarehouseId,ParentLocationId,Type,Barcode,Priority,IsPickable,IsReceivable,IsCountable,AllowMixedItems,AllowMixedLots,MaxUnits,MaxWeightKg,MaxVolumeCubicMeters,MaxPallets,MaxLpns,StorageProfile,MinimumTemperatureCelsius,MaximumTemperatureCelsius,HazardClass,AccessRestriction,ConstraintAttributesJson")]
        CreateLocationViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await locationService.CreateAsync(
            ToCreateRequest(model),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Locations.Created", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Edit(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var model = ToEditModel(result.Value);
        await PopulateLocationOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Edit(
        [Bind("Id,Code,Name,WarehouseId,ParentLocationId,Type,Barcode,Priority,IsPickable,IsReceivable,IsCountable,AllowMixedItems,AllowMixedLots,MaxUnits,MaxWeightKg,MaxVolumeCubicMeters,MaxPallets,MaxLpns,StorageProfile,MinimumTemperatureCelsius,MaximumTemperatureCelsius,HazardClass,AccessRestriction,ConstraintAttributesJson")]
        EditLocationViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await locationService.UpdateAsync(
            ToUpdateRequest(model),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Locations.Updated", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Activate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.SetActiveAsync(
            id,
            active: true,
            currentUser.RequireUserId(),
            cancellationToken);
        return RedirectWithResult(result, id, "Locations.Activated");
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Deactivate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.SetActiveAsync(
            id,
            active: false,
            currentUser.RequireUserId(),
            cancellationToken);
        return RedirectWithResult(result, id, "Locations.Deactivated");
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.DeleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken);
        return RedirectWithResult(result, null, "Locations.Deleted");
    }

    [HttpGet]
    public async Task<IActionResult> Children(
        int warehouseId,
        int? parentLocationId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.GetChildrenAsync(
            warehouseId,
            parentLocationId,
            page,
            pageSize,
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index), new { warehouseId });
        }

        return View(new LocationChildrenViewModel
        {
            WarehouseId = warehouseId,
            ParentLocationId = parentLocationId,
            Locations = result.Value.ToList(),
            Page = Math.Max(1, page),
            PageSize = Math.Clamp(pageSize, 1, 200)
        });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Generate(CancellationToken cancellationToken = default)
    {
        var model = new GenerateLocationsViewModel();
        await PopulateLocationOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Generate(
        [Bind("WarehouseId,CodePrefix,NamePrefix,StartNumber,Count,NumberWidth,ParentLocationId,Type,IsPickable,IsReceivable,IsCountable,AllowMixedItems,AllowMixedLots,MaxUnits,StorageProfile")]
        GenerateLocationsViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await locationService.GenerateAsync(
            new BulkLocationGenerationRequest(
                model.WarehouseId,
                model.CodePrefix,
                model.NamePrefix,
                model.StartNumber,
                model.Count,
                model.NumberWidth,
                model.ParentLocationId,
                model.Type,
                model.IsPickable,
                model.IsReceivable,
                model.IsCountable,
                model.MaxUnits,
                model.StorageProfile,
                model.AllowMixedItems,
                model.AllowMixedLots),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            await PopulateLocationOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Locations.BulkGenerated", result.Value.Count);
        return RedirectToAction(nameof(Index), new { warehouseId = model.WarehouseId });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken = default)
    {
        var model = new ImportLocationsViewModel();
        await PopulateWarehouseOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Import(
        [Bind("WarehouseId,Csv,File")]
        ImportLocationsViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await PopulateWarehouseOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var csv = model.Csv;
        if (model.File is not null)
        {
            if (model.File.Length > 2_000_000)
            {
                ModelState.AddModelError(nameof(model.File), this.Localize("Locations.ImportTooLarge"));
                await PopulateWarehouseOptionsAsync(model, cancellationToken);
                return View(model);
            }

            await using var stream = model.File.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            csv = await reader.ReadToEndAsync(cancellationToken);
        }

        var result = await locationService.ImportAsync(
            model.WarehouseId,
            csv ?? string.Empty,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            await PopulateWarehouseOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Locations.Imported", result.Value.CreatedCount);
        return RedirectToAction(nameof(Index), new { warehouseId = model.WarehouseId });
    }

    [HttpGet]
    public async Task<IActionResult> Labels(
        int warehouseId,
        [FromQuery] int[] ids,
        CancellationToken cancellationToken = default)
    {
        var result = await locationService.GetLabelsAsync(warehouseId, ids, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index), new { warehouseId });
        }

        return View(new LocationLabelsViewModel
        {
            WarehouseId = warehouseId,
            LocationIds = ids,
            Labels = result.Value
        });
    }

    private RedirectToActionResult RedirectWithResult(Result result, int? locationId, string successKey)
    {
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize(successKey);
        }

        return locationId.HasValue
            ? RedirectToAction(nameof(Details), new { id = locationId.Value })
            : RedirectToAction(nameof(Index));
    }

    private async Task PopulateLocationOptionsAsync(
        CreateLocationViewModel model,
        CancellationToken cancellationToken)
    {
        await PopulateWarehouseOptionsAsync(model, cancellationToken);
        model.ParentLocations = await GetParentLocationsAsync(model.WarehouseId, cancellationToken);
    }

    private async Task PopulateLocationOptionsAsync(
        EditLocationViewModel model,
        CancellationToken cancellationToken)
    {
        await PopulateWarehouseOptionsAsync(model, cancellationToken);
        model.ParentLocations = (await GetParentLocationsAsync(model.WarehouseId, cancellationToken))
            .Where(location => location.Id != model.Id)
            .ToArray();
    }

    private async Task PopulateLocationOptionsAsync(
        GenerateLocationsViewModel model,
        CancellationToken cancellationToken)
    {
        await PopulateWarehouseOptionsAsync(model, cancellationToken);
        model.ParentLocations = await GetParentLocationsAsync(model.WarehouseId, cancellationToken);
    }

    private async Task PopulateWarehouseOptionsAsync(
        CreateLocationViewModel model,
        CancellationToken cancellationToken) =>
        model.Warehouses = await GetWarehousesAsync(WmsPermissions.LocationsManage, cancellationToken);

    private async Task PopulateWarehouseOptionsAsync(
        GenerateLocationsViewModel model,
        CancellationToken cancellationToken) =>
        model.Warehouses = await GetWarehousesAsync(WmsPermissions.LocationsManage, cancellationToken);

    private async Task PopulateWarehouseOptionsAsync(
        ImportLocationsViewModel model,
        CancellationToken cancellationToken) =>
        model.Warehouses = await GetWarehousesAsync(WmsPermissions.LocationsManage, cancellationToken);

    private async Task<IReadOnlyList<WmsWarehouseOption>> GetWarehousesAsync(
        string permission,
        CancellationToken cancellationToken) =>
        await warehouseAccessService.GetAccessibleWarehousesAsync(permission, cancellationToken);

    private async Task<IReadOnlyList<LocationDto>> GetParentLocationsAsync(
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId <= 0)
        {
            return [];
        }

        var result = await locationService.ListAsync(
            new LocationListQuery(
                warehouseId,
                IncludeInactive: false,
                Page: 1,
                PageSize: 200),
            cancellationToken);
        return result.IsSuccess ? result.Value.Items : [];
    }

    private static LocationCreateRequest ToCreateRequest(CreateLocationViewModel model) =>
        new(
            model.Code,
            model.Name,
            model.WarehouseId,
            model.ParentLocationId,
            model.Type,
            model.Barcode,
            model.Priority,
            model.IsPickable,
            model.IsReceivable,
            model.IsCountable,
            model.AllowMixedItems,
            model.AllowMixedLots,
            model.MaxUnits,
            model.MaxWeightKg,
            model.MaxVolumeCubicMeters,
            model.MaxPallets,
            model.MaxLpns,
            model.StorageProfile,
            model.MinimumTemperatureCelsius,
            model.MaximumTemperatureCelsius,
            model.HazardClass,
            model.AccessRestriction,
            model.ConstraintAttributesJson);

    private static LocationUpdateRequest ToUpdateRequest(EditLocationViewModel model) =>
        new(
            model.Id,
            model.Name,
            model.ParentLocationId,
            model.Type,
            model.Barcode,
            model.Priority,
            model.IsPickable,
            model.IsReceivable,
            model.IsCountable,
            model.AllowMixedItems,
            model.AllowMixedLots,
            model.MaxUnits,
            model.MaxWeightKg,
            model.MaxVolumeCubicMeters,
            model.MaxPallets,
            model.MaxLpns,
            model.StorageProfile,
            model.MinimumTemperatureCelsius,
            model.MaximumTemperatureCelsius,
            model.HazardClass,
            model.AccessRestriction,
            model.ConstraintAttributesJson);

    private static EditLocationViewModel ToEditModel(LocationDto location) =>
        new()
        {
            Id = location.Id,
            Code = location.Code,
            Name = location.Name,
            WarehouseId = location.WarehouseId,
            ParentLocationId = location.ParentLocationId,
            Type = location.Type,
            Barcode = location.Barcode,
            Priority = location.Priority,
            IsPickable = location.IsPickable,
            IsReceivable = location.IsReceivable,
            IsCountable = location.IsCountable,
            AllowMixedItems = location.AllowMixedItems,
            AllowMixedLots = location.AllowMixedLots,
            MaxUnits = location.MaxUnits,
            MaxWeightKg = location.MaxWeightKg,
            MaxVolumeCubicMeters = location.MaxVolumeCubicMeters,
            MaxPallets = location.MaxPallets,
            MaxLpns = location.MaxLpns,
            StorageProfile = location.StorageProfile,
            MinimumTemperatureCelsius = location.MinimumTemperatureCelsius,
            MaximumTemperatureCelsius = location.MaximumTemperatureCelsius,
            HazardClass = location.HazardClass,
            AccessRestriction = location.AccessRestriction,
            ConstraintAttributesJson = location.ConstraintAttributesJson
        };
}
