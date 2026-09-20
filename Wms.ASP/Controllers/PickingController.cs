using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Picking;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.PickingExecute)]
public class PickingController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PickingController> _logger;
    private readonly IPickOrderUseCase _pickOrderUseCase;

    public PickingController(
        IPickOrderUseCase pickOrderUseCase,
        ICurrentUser currentUser,
        ILogger<PickingController> logger)
    {
        _pickOrderUseCase = pickOrderUseCase;
        _currentUser = currentUser;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View(new PickingViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Pick(PickingViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        try
        {
            var request = new PickItemDto(
                model.ItemSku,
                model.LocationCode,
                model.Quantity,
                model.OrderNumber,
                model.LotNumber,
                model.SerialNumber,
                model.Notes
            );

            var result = await _pickOrderUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                TempData["ErrorMessage"] = result.Error;
                return View("Index", model);
            }

            TempData["SuccessMessage"] = $"Pick completed successfully! Movement ID: {result.Value.MovementId}";
            return View("Index", new PickingViewModel()); // Clear form for next entry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing pick");
            TempData["ErrorMessage"] = "Error performing pick. Please try again.";
            return View("Index", model);
        }
    }
}
