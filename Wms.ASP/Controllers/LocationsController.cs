using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Locations;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.LocationsRead)]
public class LocationsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ICreateLocationUseCase _createLocationUseCase;
    private readonly IGetLocationsUseCase _getLocationsUseCase;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public LocationsController(
        IGetLocationsUseCase getLocationsUseCase,
        ICreateLocationUseCase createLocationUseCase,
        ICurrentUser currentUser,
        IWarehouseAccessService warehouseAccessService)
    {
        _getLocationsUseCase = getLocationsUseCase;
        _createLocationUseCase = createLocationUseCase;
        _currentUser = currentUser;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<IActionResult> Index(
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await _getLocationsUseCase.ExecuteAsync(
            searchTerm,
            cancellationToken);

        var model = new LocationManagementViewModel
        {
            SearchTerm = searchTerm
        };

        if (result.IsSuccess)
        {
            var locations = result.Value.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                locations = locations.Where(l =>
                    l.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    l.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            model.Locations = locations.ToList();
        }
        else
        {
            TempData["ErrorMessage"] = result.Error;
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var model = new CreateLocationViewModel();
        await PopulateWarehouseOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.LocationsManage)]
    public async Task<IActionResult> Create(
        [Bind("Code,Name,WarehouseId,IsPickable,IsReceivable,Capacity")]
        CreateLocationViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            await PopulateWarehouseOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var request = new CreateLocationDto(
            model.Code,
            model.Name,
            model.WarehouseId,
            null, // ParentLocationId
            model.IsPickable,
            model.IsReceivable,
            model.Capacity
        );

        var result = await _createLocationUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = result.Error;
            await PopulateWarehouseOptionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = $"Location '{model.Name}' created successfully!";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateWarehouseOptionsAsync(
        CreateLocationViewModel model,
        CancellationToken cancellationToken)
    {
        model.Warehouses = await _warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.LocationsManage,
            cancellationToken);
    }
}
