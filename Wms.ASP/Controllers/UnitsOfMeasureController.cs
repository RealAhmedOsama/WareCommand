using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.Units;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.ItemsRead)]
public sealed class UnitsOfMeasureController(
    IUnitOfMeasureManagementService unitService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        UnitOfMeasureCategory? category,
        bool includeInactive = true,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await unitService.ListAsync(
            new UnitOfMeasureListQuery(searchTerm, category, includeInactive, page, pageSize),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(new UnitOfMeasureListViewModel
            {
                SearchTerm = searchTerm,
                Category = category,
                IncludeInactive = includeInactive,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 200)
            });
        }

        return View(new UnitOfMeasureListViewModel
        {
            Items = result.Value.Items,
            SearchTerm = searchTerm,
            Category = category,
            IncludeInactive = includeInactive,
            Page = result.Value.Page,
            PageSize = result.Value.PageSize,
            TotalCount = result.Value.TotalCount,
            TotalPages = result.Value.TotalPages
        });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public IActionResult Create() => View(new UnitOfMeasureFormViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        UnitOfMeasureFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await unitService.CreateAsync(
            model.ToRequest(),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Uom.Created", result.Value.Code);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await unitService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(UnitOfMeasureFormViewModel.From(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        UnitOfMeasureFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await unitService.UpdateAsync(
            id,
            model.ToRequest(),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Uom.Updated", result.Value.Code);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var result = await unitService.SetActiveAsync(
            id,
            active,
            currentUser.RequireUserId(),
            cancellationToken);
        TempData[result.IsSuccess ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess
            ? this.Localize(active ? "Uom.Activated" : "Uom.Deactivated")
            : this.LocalizeError(result.FirstError!);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Item(int id, CancellationToken cancellationToken = default)
    {
        var result = await unitService.GetItemAssignmentAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(ItemUnitAssignmentViewModel.From(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Item(
        ItemUnitAssignmentViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid ||
            !TryParseConversions(model.ConversionsText, out var conversions))
        {
            return View(model);
        }

        var result = await unitService.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                model.ItemId,
                model.BaseUnitOfMeasure,
                model.PurchaseUnitOfMeasure,
                model.SalesUnitOfMeasure,
                model.AllowFractionalQuantity,
                conversions),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            model.ExistingConversions = result.Value?.Conversions ?? [];
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Uom.ItemAssignmentSaved", result.Value.Sku);
        return RedirectToAction(nameof(Item), new { id = result.Value.ItemId });
    }

    private bool TryParseConversions(
        string? text,
        out IReadOnlyList<ItemUnitConversionRequest> conversions)
    {
        var parsed = new List<ItemUnitConversionRequest>();
        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var fields = lines[index].Split('|', StringSplitOptions.None);
            if (fields.Length < 4)
            {
                ModelState.AddModelError(
                    nameof(ItemUnitAssignmentViewModel.ConversionsText),
                    $"Conversion line {index + 1} must be FROM|TO|FACTOR|PRECISION|ROUNDING.");
                conversions = [];
                return false;
            }

            if (!decimal.TryParse(
                    fields[2],
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var factor))
            {
                ModelState.AddModelError(
                    nameof(ItemUnitAssignmentViewModel.ConversionsText),
                    $"Conversion line {index + 1} has an invalid factor.");
                conversions = [];
                return false;
            }

            if (!int.TryParse(
                    fields[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var precision))
            {
                ModelState.AddModelError(
                    nameof(ItemUnitAssignmentViewModel.ConversionsText),
                    $"Conversion line {index + 1} has an invalid precision.");
                conversions = [];
                return false;
            }

            var rounding = QuantityRoundingMode.Reject;
            if (fields.Length > 4 &&
                !string.IsNullOrWhiteSpace(fields[4]) &&
                !Enum.TryParse(fields[4], true, out rounding))
            {
                ModelState.AddModelError(
                    nameof(ItemUnitAssignmentViewModel.ConversionsText),
                    $"Conversion line {index + 1} has an invalid rounding mode.");
                conversions = [];
                return false;
            }

            parsed.Add(new ItemUnitConversionRequest(
                fields[0].Trim(),
                fields[1].Trim(),
                factor,
                precision,
                rounding));
        }

        conversions = parsed;
        return true;
    }
}
