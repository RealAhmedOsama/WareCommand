using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.Items;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.ItemsRead)]
public sealed class ItemsController(
    IItemManagementService itemManagementService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        ItemType? type,
        ItemLifecycleStatus? lifecycleStatus,
        string? category,
        bool includeInactive = true,
        ItemSortField sortBy = ItemSortField.Sku,
        bool descending = false,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var request = new ItemListQuery(
            searchTerm,
            type,
            lifecycleStatus,
            category,
            includeInactive,
            sortBy,
            descending,
            page,
            pageSize);
        var result = await itemManagementService.ListAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(new ItemListViewModel
            {
                SearchTerm = searchTerm,
                Type = type,
                LifecycleStatus = lifecycleStatus,
                Category = category,
                IncludeInactive = includeInactive,
                SortBy = sortBy,
                Descending = descending,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 200)
            });
        }

        return View(new ItemListViewModel
        {
            Items = result.Value.Items,
            SearchTerm = searchTerm,
            Type = type,
            LifecycleStatus = lifecycleStatus,
            Category = category,
            IncludeInactive = includeInactive,
            SortBy = sortBy,
            Descending = descending,
            Page = result.Value.Page,
            PageSize = result.Value.PageSize,
            TotalCount = result.Value.TotalCount,
            TotalPages = result.Value.TotalPages
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public IActionResult Create() => View(new ItemFormViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        ItemFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !TryBuildRequests(model, out var commercial, out var measurements,
                out var tracking, out var storage, out var planning, out var barcodes, out var packagings))
        {
            return View(model);
        }

        var result = await itemManagementService.CreateAsync(
            new ItemCreateRequest(
                model.Sku,
                commercial,
                measurements,
                tracking,
                storage,
                planning,
                barcodes,
                packagings),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Created", result.Value.Sku);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(ItemFormViewModel.From(result.Value));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        ItemFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !TryBuildRequests(model, out var commercial, out var measurements,
                out var tracking, out var storage, out var planning, out var barcodes, out var packagings))
        {
            return View(model);
        }

        var result = await itemManagementService.UpdateAsync(
            new ItemUpdateRequest(
                model.Id,
                commercial,
                measurements,
                tracking,
                storage,
                planning,
                barcodes,
                packagings),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Updated", result.Value.Sku);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.SetActiveAsync(
            id,
            active,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize(active ? "Items.Activated" : "Items.Deactivated");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.DeleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Items.Deleted");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public async Task<IActionResult> Duplicate(int id, CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var source = result.Value;
        var sku = source.Sku + "-COPY";
        return View(new ItemDuplicateViewModel
        {
            SourceItemId = source.Id,
            Sku = sku.Length <= 50 ? sku : sku[..50],
            Name = source.Name + " (Copy)",
            SourceItem = source
        });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(
        ItemDuplicateViewModel model,
        CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.DuplicateAsync(
            new ItemDuplicateRequest(model.SourceItemId, model.Sku, model.Name, model.CopyPackagings),
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            await PopulateDuplicateSourceAsync(model, cancellationToken);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Duplicated", result.Value.Sku);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    public IActionResult Import() => View(new ItemImportViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.ItemsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        ItemImportViewModel model,
        CancellationToken cancellationToken = default)
    {
        var csv = await ReadImportCsvAsync(model, cancellationToken);
        if (csv is null)
        {
            ModelState.AddModelError(nameof(model.Csv), this.Localize("Items.ImportRequired"));
            return View(model);
        }

        var result = await itemManagementService.ImportAsync(
            csv,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize("Items.Imported", result.Value.ImportedCount);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        string? searchTerm,
        ItemType? type,
        ItemLifecycleStatus? lifecycleStatus,
        string? category,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        var result = await itemManagementService.ExportAsync(
            new ItemListQuery(searchTerm, type, lifecycleStatus, category, includeInactive, PageSize: 200),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Value)).ToArray();
        return File(bytes, "text/csv", "items.csv");
    }

    private bool TryBuildRequests(
        ItemFormViewModel model,
        out ItemCommercialRequest commercial,
        out ItemMeasurementRequest measurements,
        out ItemTrackingRequest tracking,
        out ItemStorageRequest storage,
        out ItemPlanningRequest planning,
        out IReadOnlyList<string> barcodes,
        out IReadOnlyList<ItemPackagingRequest> packagings)
    {
        commercial = default!;
        measurements = default!;
        tracking = default!;
        storage = default!;
        planning = default!;
        barcodes = [];
        packagings = [];
        try
        {
            commercial = new ItemCommercialRequest(
                model.Name,
                model.LocalizedName,
                model.Description,
                model.LocalizedDescription,
                model.Category,
                model.Brand,
                model.Type,
                model.LifecycleStatus,
                model.ImageReference,
                model.DocumentReference);
            measurements = new ItemMeasurementRequest(
                model.BaseUnit,
                model.PurchaseUnit,
                model.SalesUnit,
                model.NetWeightKg,
                model.LengthCm,
                model.WidthCm,
                model.HeightCm,
                model.VolumeCubicMeters,
                model.StandardCost,
                model.SalesPrice,
                model.CountryOfOrigin,
                model.CustomsCode);
            tracking = new ItemTrackingRequest(
                model.RequiresLot,
                model.RequiresSerial,
                model.RequiresExpiry,
                model.ShelfLifeDays,
                model.UseFefo,
                model.QualityInspectionRequired,
                model.IsHazardous,
                model.TemperatureControlled,
                model.SpecialHandlingRequired,
                model.MinimumTemperatureCelsius,
                model.MaximumTemperatureCelsius,
                model.AllowFractionalQuantity);
            storage = new ItemStorageRequest(model.StorageProfile, model.PutawayProfile, model.DefaultSupplierCode);
            planning = new ItemPlanningRequest(
                model.ReorderPolicy,
                model.MinimumStock,
                model.MaximumStock,
                model.SafetyStock,
                model.LeadTimeDays);
            barcodes = ParseBarcodes(model.BarcodesText);
            packagings = ParsePackagings(model.PackagingsText);
            return true;
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return false;
        }
    }

    private async Task PopulateDuplicateSourceAsync(
        ItemDuplicateViewModel model,
        CancellationToken cancellationToken)
    {
        var source = await itemManagementService.GetAsync(model.SourceItemId, cancellationToken);
        if (source.IsSuccess)
        {
            model.SourceItem = source.Value;
        }
    }

    private static string[] ParseBarcodes(string? text) =>
        (text ?? string.Empty)
        .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static List<ItemPackagingRequest> ParsePackagings(string? text)
    {
        var result = new List<ItemPackagingRequest>();
        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var fields = lines[index].Split('|', StringSplitOptions.None);
            if (fields.Length < 3)
            {
                throw new ArgumentException(
                    $"Packaging line {index + 1} must be CODE|UNIT|UNITS_PER_PACKAGE|BARCODE|WEIGHT_KG|LENGTH_CM|WIDTH_CM|HEIGHT_CM|DEFAULT|NAME|LOCALIZED_NAME|GTIN|PARENT_CODE|TYPE|PARTIAL_POLICY|DEFAULT_RECEIVING|DEFAULT_STORAGE|DEFAULT_PICKING|DEFAULT_SHIPPING|ACTIVE.");
            }

            result.Add(new ItemPackagingRequest(
                Required(fields[0], "packaging code"),
                Required(fields[1], "packaging unit"),
                ParseRequiredDecimal(fields[2], "units per package"),
                Optional(fields, 3),
                ParseOptionalDecimal(fields, 4, "gross weight"),
                ParseOptionalDecimal(fields, 5, "length"),
                ParseOptionalDecimal(fields, 6, "width"),
                ParseOptionalDecimal(fields, 7, "height"),
                ParseOptionalBool(fields, 8, false, "packaging default"),
                Optional(fields, 9),
                Optional(fields, 10),
                Optional(fields, 11),
                Optional(fields, 12),
                ParseOptionalEnum(fields, 13, PackagingType.Other, "packaging type"),
                ParseOptionalEnum(fields, 14, PackagingPartialPolicy.Reject, "partial package policy"),
                ParseOptionalBool(fields, 15, false, "default receiving"),
                ParseOptionalBool(fields, 16, false, "default storage"),
                ParseOptionalBool(fields, 17, false, "default picking"),
                ParseOptionalBool(fields, 18, false, "default shipping"),
                ParseOptionalBool(fields, 19, true, "packaging active")));
        }

        return result;
    }

    private static async Task<string?> ReadImportCsvAsync(
        ItemImportViewModel model,
        CancellationToken cancellationToken)
    {
        if (model.File is not null)
        {
            if (model.File.Length == 0 || model.File.Length > 2_000_000)
            {
                return null;
            }

            await using var stream = model.File.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        return string.IsNullOrWhiteSpace(model.Csv) ? null : model.Csv;
    }

    private static string Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string? Optional(string[] fields, int index) =>
        fields.Length > index && !string.IsNullOrWhiteSpace(fields[index]) ? fields[index].Trim() : null;

    private static decimal ParseRequiredDecimal(string value, string label) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"{label} must be a decimal number.");

    private static decimal? ParseOptionalDecimal(string[] fields, int index, string label) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : ParseRequiredDecimal(fields[index], label);

    private static bool ParseOptionalBool(
        string[] fields,
        int index,
        bool defaultValue,
        string label) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? defaultValue
            : bool.TryParse(fields[index], out var parsed)
                ? parsed
                : throw new ArgumentException($"The {label} flag must be true or false.");

    private static T ParseOptionalEnum<T>(
        string[] fields,
        int index,
        T defaultValue,
        string label)
        where T : struct, Enum =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? defaultValue
            : Enum.TryParse<T>(fields[index], true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw new ArgumentException($"The {label} value is not supported.");
}
