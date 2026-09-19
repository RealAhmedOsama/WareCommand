using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Locations;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize]
public class LocationsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ICreateLocationUseCase _createLocationUseCase;
    private readonly IGetLocationsUseCase _getLocationsUseCase;
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(
        IGetLocationsUseCase getLocationsUseCase,
        ICreateLocationUseCase createLocationUseCase,
        ICurrentUser currentUser,
        ILogger<LocationsController> logger)
    {
        _getLocationsUseCase = getLocationsUseCase;
        _createLocationUseCase = createLocationUseCase;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? searchTerm)
    {
        try
        {
            var result = await _getLocationsUseCase.ExecuteAsync();

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading locations");
            TempData["ErrorMessage"] = "Error loading locations. Please try again.";
            return View(new LocationManagementViewModel());
        }
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateLocationViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateLocationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var request = new CreateLocationDto(
                model.Code,
                model.Name,
                model.WarehouseId,
                null, // ParentLocationId
                model.IsPickable,
                model.IsReceivable,
                model.Capacity
            );

            var result = await _createLocationUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                TempData["ErrorMessage"] = result.Error;
                return View(model);
            }

            TempData["SuccessMessage"] = $"Location '{model.Name}' created successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location");
            TempData["ErrorMessage"] = "Error creating location. Please try again.";
            return View(model);
        }
    }
}
