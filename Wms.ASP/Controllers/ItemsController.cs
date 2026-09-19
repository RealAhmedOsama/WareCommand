using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.UseCases.Items;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize]
public class ItemsController : Controller
{
    private readonly ICurrentUser _currentUser;
    private readonly ICreateItemUseCase _createItemUseCase;
    private readonly IGetItemsUseCase _getItemsUseCase;
    private readonly ILogger<ItemsController> _logger;
    private readonly IUpdateItemUseCase _updateItemUseCase;

    public ItemsController(
        IGetItemsUseCase getItemsUseCase,
        ICreateItemUseCase createItemUseCase,
        IUpdateItemUseCase updateItemUseCase,
        ICurrentUser currentUser,
        ILogger<ItemsController> logger)
    {
        _getItemsUseCase = getItemsUseCase;
        _createItemUseCase = createItemUseCase;
        _updateItemUseCase = updateItemUseCase;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? searchTerm)
    {
        try
        {
            var result = await _getItemsUseCase.ExecuteAsync(searchTerm);

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
                TempData["ErrorMessage"] = result.Error;
            }

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading items");
            TempData["ErrorMessage"] = "Error loading items. Please try again.";
            return View(new ItemManagementViewModel());
        }
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateItemViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateItemViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
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

            var result = await _createItemUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                TempData["ErrorMessage"] = result.Error;
                return View(model);
            }

            TempData["SuccessMessage"] = $"Item '{model.Name}' created successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating item");
            TempData["ErrorMessage"] = "Error creating item. Please try again.";
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var result = await _getItemsUseCase.ExecuteAsync();

            if (result.IsFailure)
            {
                TempData["ErrorMessage"] = result.Error;
                return RedirectToAction(nameof(Index));
            }

            var item = result.Value.FirstOrDefault(i => i.Id == id);
            if (item == null)
            {
                TempData["ErrorMessage"] = "Item not found.";
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading item for edit");
            TempData["ErrorMessage"] = "Error loading item. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Edit(EditItemViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
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

            var result = await _updateItemUseCase.ExecuteAsync(request, _currentUser.RequireUserId());

            if (result.IsFailure)
            {
                TempData["ErrorMessage"] = result.Error;
                return View(model);
            }

            TempData["SuccessMessage"] = $"Item '{model.Name}' updated successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item");
            TempData["ErrorMessage"] = "Error updating item. Please try again.";
            return View(model);
        }
    }
}
