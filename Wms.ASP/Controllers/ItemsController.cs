using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Items;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.ItemsRead)]
public class ItemsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ICreateItemUseCase _createItemUseCase;
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly IUpdateItemUseCase _updateItemUseCase;

    public ItemsController(
        IGetItemsUseCase getItemsUseCase,
        ICreateItemUseCase createItemUseCase,
        IUpdateItemUseCase updateItemUseCase,
        ICurrentUser currentUser)
    {
        _getItemsUseCase = getItemsUseCase;
        _createItemUseCase = createItemUseCase;
        _updateItemUseCase = updateItemUseCase;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(
        string? searchTerm,
        CancellationToken cancellationToken = default)
    {
        var result = await _getItemsUseCase.ExecuteAsync(searchTerm, cancellationToken);

        var model = new ItemManagementViewModel
        {
            SearchTerm = searchTerm
        };

        if (result.IsSuccess)
        {
            model.Items = result.Value.ToList();
        }
        else
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }

        return View(model);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public IActionResult Create()
    {
        return View(new CreateItemViewModel());
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Create(
        [Bind("Sku,Name,Description,UnitOfMeasure,RequiresLot,RequiresSerial,Barcode")]
        CreateItemViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var barcodes = !string.IsNullOrWhiteSpace(model.Barcode)
            ? new List<string> { model.Barcode }
            : new List<string>();

        var request = new CreateItemDto(
            model.Sku,
            model.Name,
            model.Description ?? string.Empty,
            model.UnitOfMeasure ?? "EA",
            model.RequiresLot,
            model.RequiresSerial,
            0, // ShelfLifeDays
            barcodes
        );

        var result = await _createItemUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Created", model.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await _getItemsUseCase.ExecuteAsync(cancellationToken: cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var item = result.Value.FirstOrDefault(i => i.Id == id);
        if (item == null)
        {
            TempData["ErrorMessage"] = this.Localize("Error.NotFound");
            return RedirectToAction(nameof(Index));
        }

        var model = new EditItemViewModel
        {
            Id = item.Id,
            Sku = item.Sku,
            Name = item.Name,
            Description = item.Description,
            UnitOfMeasure = item.UnitOfMeasure,
            RequiresLot = item.RequiresLot,
            RequiresSerial = item.RequiresSerial,
            Barcode = item.Barcodes.FirstOrDefault()
        };

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Edit(
        [Bind("Id,Name,Description,Barcode")] EditItemViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var barcodes = !string.IsNullOrWhiteSpace(model.Barcode)
            ? new List<string> { model.Barcode }
            : new List<string>();

        var request = new UpdateItemDto(
            model.Id,
            model.Name,
            model.Description ?? string.Empty,
            0, // ShelfLifeDays - not implemented in the view model yet
            barcodes
        );

        var result = await _updateItemUseCase.ExecuteAsync(
            request,
            _currentUser.RequireUserId(),
            cancellationToken);

        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Updated", model.Name);
        return RedirectToAction(nameof(Index));
    }
}
