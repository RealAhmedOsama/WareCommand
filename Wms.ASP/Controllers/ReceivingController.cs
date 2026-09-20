using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.UseCases.Receiving;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize]
public class ReceivingController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly IPutawayUseCase _putawayUseCase;
    private readonly IReceiveItemUseCase _receiveItemUseCase;

    public ReceivingController(
        IReceiveItemUseCase receiveItemUseCase,
        IPutawayUseCase putawayUseCase,
        ICurrentUser currentUser)
    {
        _receiveItemUseCase = receiveItemUseCase;
        _putawayUseCase = putawayUseCase;
        _currentUser = currentUser;
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ReceivingExecute)]
    public IActionResult Receive()
    {
        return View(new ReceivingViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = WmsPermissions.ReceivingExecute)]
    public async Task<IActionResult> Receive(
        [Bind("ItemSku,LocationCode,Quantity,UnitOfMeasure,PackagingCode,LotNumber,SerialNumber,ReferenceNumber,Notes")]
        ReceivingViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new ReceiveItemDto(
            model.ItemSku,
            model.LocationCode,
            model.Quantity,
            model.LotNumber,
            model.SerialNumber,
            model.ReferenceNumber,
            model.Notes,
            UnitOfMeasure: model.UnitOfMeasure,
            PackagingCode: model.PackagingCode
        );

        var result = await _receiveItemUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Receiving.Completed", result.Value.MovementId);
        return View(new ReceivingViewModel()); // Clear form for next entry
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.PutawayExecute)]
    public IActionResult Putaway()
    {
        return View(new PutawayViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = WmsPermissions.PutawayExecute)]
    public async Task<IActionResult> Putaway(
        [Bind("ItemSku,FromLocationCode,ToLocationCode,Quantity,UnitOfMeasure,PackagingCode,LotNumber,SerialNumber,Notes")]
        PutawayViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var request = new PutawayDto(
            model.ItemSku,
            model.FromLocationCode,
            model.ToLocationCode,
            model.Quantity,
            model.LotNumber,
            model.SerialNumber,
            model.Notes,
            UnitOfMeasure: model.UnitOfMeasure,
            PackagingCode: model.PackagingCode
        );

        var result = await _putawayUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Putaway.Completed", result.Value.MovementId);
        return View(new PutawayViewModel()); // Clear form for next entry
    }
}
