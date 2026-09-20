using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Identity;
using Wms.Application.Suppliers;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.SuppliersRead)]
public sealed class SupplierMasterController(
    ISupplierManagementService supplierManagementService,
    IWarehouseAccessService warehouseAccessService,
    ICurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? searchTerm,
        bool includeInactive = true,
        int? preferredWarehouseId = null,
        SupplierSortField sortBy = SupplierSortField.Code,
        bool descending = false,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ListAsync(
            new SupplierListQuery(
                searchTerm,
                includeInactive,
                preferredWarehouseId,
                sortBy,
                descending,
                page,
                pageSize),
            cancellationToken);
        var warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.SuppliersRead,
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(new SupplierListViewModel
            {
                Warehouses = warehouses,
                SearchTerm = searchTerm,
                IncludeInactive = includeInactive,
                PreferredWarehouseId = preferredWarehouseId,
                SortBy = sortBy,
                Descending = descending,
                Page = Math.Max(1, page),
                PageSize = Math.Clamp(pageSize, 1, 200)
            });
        }

        return View(new SupplierListViewModel
        {
            Suppliers = result.Value.Suppliers,
            Warehouses = warehouses,
            SearchTerm = searchTerm,
            IncludeInactive = includeInactive,
            PreferredWarehouseId = preferredWarehouseId,
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
        var result = await supplierManagementService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(result.Value);
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        return View(await WithWarehousesAsync(new SupplierFormViewModel(), cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        SupplierFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !TryBuildInput(model, out var input))
        {
            return View(await WithWarehousesAsync(model, cancellationToken));
        }

        var result = await supplierManagementService.CreateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithWarehousesAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Suppliers.Created", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.GetAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        return View(await WithWarehousesAsync(
            SupplierFormViewModel.From(result.Value),
            cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        SupplierFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || !TryBuildInput(model, out var input))
        {
            return View(await WithWarehousesAsync(model, cancellationToken));
        }

        var result = await supplierManagementService.UpdateAsync(
            model.Id,
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(await WithWarehousesAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Suppliers.Updated", result.Value.Code);
        return RedirectToAction(nameof(Details), new { id = result.Value.Id });
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(
        int id,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.SetActiveAsync(
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
            TempData["SuccessMessage"] = this.Localize(active ? "Suppliers.Activated" : "Suppliers.Deactivated");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.DeleteAsync(
            id,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
        }
        else
        {
            TempData["SuccessMessage"] = this.Localize("Suppliers.Deleted");
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    public IActionResult Import() => View(new SupplierImportViewModel());

    [HttpPost]
    [Authorize(Policy = WmsPermissions.SuppliersManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(
        SupplierImportViewModel model,
        CancellationToken cancellationToken = default)
    {
        var csv = await ReadImportCsvAsync(model, cancellationToken);
        if (csv is null)
        {
            ModelState.AddModelError(nameof(model.Csv), this.Localize("Suppliers.ImportRequired"));
            return View(model);
        }

        var result = await supplierManagementService.ImportAsync(
            csv,
            currentUser.RequireUserId(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return View(model);
        }

        TempData["SuccessMessage"] = this.Localize(
            "Suppliers.Imported",
            result.Value.ImportedSupplierCount,
            result.Value.ImportedReferenceCount);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export(
        string? searchTerm,
        bool includeInactive = true,
        int? preferredWarehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierManagementService.ExportAsync(
            new SupplierListQuery(searchTerm, includeInactive, preferredWarehouseId),
            cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();
        return File(bytes, "text/csv", "suppliers.csv");
    }

    private async Task<SupplierFormViewModel> WithWarehousesAsync(
        SupplierFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Warehouses = await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.SuppliersManage,
            cancellationToken);
        return model;
    }

    private bool TryBuildInput(SupplierFormViewModel model, out SupplierInput input)
    {
        input = default!;
        try
        {
            var references = ParseItemReferences(model.ItemReferencesText);
            input = new SupplierInput(
                model.Code,
                model.LegalName,
                model.LocalizedName,
                model.TaxRegistrationNumber,
                model.ExternalErpIdentifier,
                model.AddressLine1,
                model.AddressLine2,
                model.City,
                model.StateOrProvince,
                model.PostalCode,
                model.CountryCode,
                model.ContactName,
                model.ContactEmail,
                model.ContactPhone,
                new SupplierReceivingDefaultsInput(
                    model.PreferredWarehouseId,
                    model.PreferredDockLocationId,
                    model.DefaultLeadTimeDays,
                    model.OverDeliveryTolerancePercent,
                    model.UnderDeliveryTolerancePercent,
                    model.RequiresLot,
                    model.RequiresExpiry,
                    model.QualityProfile,
                    model.LabelRule,
                    model.DefaultCurrencyCode),
                model.Notes,
                model.IsActive,
                references);
            return true;
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(model.ItemReferencesText), exception.Message);
            return false;
        }
    }

    private static List<SupplierItemReferenceInput> ParseItemReferences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var references = new List<SupplierItemReferenceInput>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            var fields = lines[index].Split('|');
            if (fields.Length < 3)
            {
                throw new ArgumentException(
                    $"Item reference line {index + 1} must be ITEM_ID|VENDOR_SKU|VENDOR_BARCODE|PACKAGING_ID|VENDOR_PACKAGING|UNITS_PER_PACKAGE|MINIMUM_ORDER_QUANTITY|LEAD_TIME_DAYS|ACTIVE.");
            }

            references.Add(new SupplierItemReferenceInput(
                null,
                ParseRequiredInt(fields[0], "item id", index),
                Required(fields[1], "vendor SKU"),
                Optional(fields, 2),
                ParseOptionalInt(fields, 3, "packaging id", index),
                Optional(fields, 4),
                ParseOptionalDecimal(fields, 5, "units per package", index),
                ParseDecimal(fields, 6, 1, "minimum order quantity", index),
                ParseOptionalInt(fields, 7, "lead time", index),
                ParseOptionalBool(fields, 8, true, "active", index)));
        }

        return references;
    }

    private static async Task<string?> ReadImportCsvAsync(
        SupplierImportViewModel model,
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
        fields.Length > index && !string.IsNullOrWhiteSpace(fields[index])
            ? fields[index].Trim()
            : null;

    private static int ParseRequiredInt(string value, string label, int row) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Item reference line {row + 1}: {label} must be an integer.");

    private static int? ParseOptionalInt(string[] fields, int index, string label, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : ParseRequiredInt(fields[index], label, row);

    private static decimal? ParseOptionalDecimal(string[] fields, int index, string label, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? null
            : ParseDecimal(fields, index, null, label, row);

    private static decimal ParseDecimal(string[] fields, int index, decimal? defaultValue, string label, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? defaultValue ?? throw new ArgumentException($"{label} is required.")
            : decimal.TryParse(fields[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"Item reference line {row + 1}: {label} must be a decimal number.");

    private static bool ParseOptionalBool(string[] fields, int index, bool defaultValue, string label, int row) =>
        fields.Length <= index || string.IsNullOrWhiteSpace(fields[index])
            ? defaultValue
            : bool.TryParse(fields[index], out var parsed)
                ? parsed
                : throw new ArgumentException($"Item reference line {row + 1}: {label} must be true or false.");
}
