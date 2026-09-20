using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Picking;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.PickingExecute)]
public class PickingController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IPickOrderUseCase _pickOrderUseCase;

    public PickingController(
        IPickOrderUseCase pickOrderUseCase,
        ICurrentUser currentUser)
    {
        _pickOrderUseCase = pickOrderUseCase;
        _currentUser = currentUser;
    }

    public IActionResult Index()
    {
        return View(new PickingViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pick(
        [Bind("ItemSku,LocationCode,Quantity,OrderNumber,LotNumber,SerialNumber,Notes")]
        PickingViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var request = new PickItemDto(
            model.ItemSku,
            model.LocationCode,
            model.Quantity,
            model.OrderNumber,
            model.LotNumber,
            model.SerialNumber,
            model.Notes
        );

        var result = await _pickOrderUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = result.Error;
            return View("Index", model);
        }

        TempData["SuccessMessage"] = $"Pick completed successfully! Movement ID: {result.Value.MovementId}";
        return View("Index", new PickingViewModel()); // Clear form for next entry
    }
}
