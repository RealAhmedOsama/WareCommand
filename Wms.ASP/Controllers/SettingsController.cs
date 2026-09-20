using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.ASP.Extensions;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.SettingsManage)]
public sealed class SettingsController(
    IWmsSettingsService settingsService,
    IWarehouseAccessService warehouseAccessService,
    ILogger<SettingsController> logger) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 8
    };

    [HttpGet]
    public async Task<IActionResult> Index(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var page = await BuildPageAsync(warehouseId, cancellationToken);
        return View(page);
    }

    [HttpPost]
    public async Task<IActionResult> SaveGlobal(
        [Bind("CompanyName,CompanyCode,DefaultWarehouseId,DefaultReceivingLocationCode,DefaultShippingLocationCode,ReceivingPrefix,NextReceivingNumber,ShippingPrefix,NextShippingNumber,AdjustmentPrefix,NextAdjustmentNumber,LowStockThreshold,LowStockAlertLimit,RecentMovementLimit,RefreshIntervalSeconds,AllowNegativeStock,RequireLocationForAdjustment,MaximumAdjustmentQuantity,ExpiryWarningDays,BlockExpiredReceipt,ScannerTimeoutMilliseconds,MinimumBarcodeLength,MaximumBarcodeLength,EnableAudioFeedback,LabelTemplateName,LabelPaperSize,IncludeCompanyNameOnLabels,DefaultReportPeriodDays,MaximumReportRows,Locale,TimeZone,CurrencyCode,IntegrationsEnabled,IntegrationEndpointUrl,IntegrationTimeoutSeconds")]
        GlobalSettingsFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildGlobalPageAsync(model, cancellationToken));
        }

        var result = await settingsService.SaveGlobalAsync(model.ToValues(), cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            return View(nameof(Index), await BuildGlobalPageAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Settings.GlobalSaved");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SaveOverride(
        [Bind("WarehouseId,DefaultReceivingLocationCode,DefaultShippingLocationCode,LowStockThreshold,LowStockAlertLimit,RecentMovementLimit,RefreshIntervalSeconds,ExpiryWarningDays,BlockExpiredReceipt,ScannerTimeoutMilliseconds,MinimumBarcodeLength,MaximumBarcodeLength,EnableAudioFeedback,DefaultReportPeriodDays,MaximumReportRows,Locale,TimeZone")]
        WarehouseSettingsOverrideFormViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildOverridePageAsync(model, cancellationToken));
        }

        var result = await settingsService.SaveWarehouseOverrideAsync(
            model.WarehouseId,
            model.ToValues(),
            cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            return View(nameof(Index), await BuildOverridePageAsync(model, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Settings.OverrideSaved");
        return RedirectToAction(nameof(Index), new { warehouseId = model.WarehouseId });
    }

    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken = default)
    {
        var result = await settingsService.ExportAsync(cancellationToken);
        if (result.IsFailure)
        {
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return RedirectToAction(nameof(Index));
        }

        var json = JsonSerializer.Serialize(result.Value, JsonOptions);
        return File(
            System.Text.Encoding.UTF8.GetBytes(json),
            "application/json",
            "warecommand-settings.json");
    }

    [HttpPost]
    [EnableRateLimiting(WmsRateLimitPolicies.Import)]
    public async Task<IActionResult> Import(
        [Bind("ImportJson")] SettingsImportViewModel model,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildPageAsync(null, cancellationToken));
        }

        WmsSettingsExportDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<WmsSettingsExportDocument>(model.ImportJson, JsonOptions);
        }
        catch (JsonException)
        {
            ModelState.AddModelError(nameof(model.ImportJson), this.Localize("Settings.InvalidJson"));
            return View(nameof(Index), await BuildPageAsync(null, cancellationToken));
        }

        if (document is null)
        {
            ModelState.AddModelError(nameof(model.ImportJson), this.Localize("Settings.EmptyImport"));
            return View(nameof(Index), await BuildPageAsync(null, cancellationToken));
        }

        var result = await settingsService.ImportAsync(document, cancellationToken);
        if (result.IsFailure)
        {
            this.AddToModelState(result);
            return View(nameof(Index), await BuildPageAsync(null, cancellationToken));
        }

        TempData["SuccessMessage"] = this.Localize("Settings.Imported");
        return RedirectToAction(nameof(Index));
    }

    private async Task<SettingsIndexViewModel> BuildPageAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId.HasValue)
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.SettingsManage,
                warehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                TempData["ErrorMessage"] = this.LocalizeError(authorization.FirstError!);
                return new SettingsIndexViewModel
                {
                    WarehouseId = warehouseId,
                    Warehouses = await GetWarehousesAsync(cancellationToken)
                };
            }
        }

        var result = await settingsService.GetAsync(warehouseId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Settings page could not load: {ErrorCode}", result.ErrorCode);
            TempData["ErrorMessage"] = this.LocalizeError(result.FirstError!);
            return new SettingsIndexViewModel
            {
                WarehouseId = warehouseId,
                Warehouses = await GetWarehousesAsync(cancellationToken)
            };
        }

        var snapshot = result.Value;
        return new SettingsIndexViewModel
        {
            WarehouseId = warehouseId,
            ScopeName = warehouseId.HasValue
                ? $"Warehouse override ({warehouseId.Value})"
                : "Global settings",
            Global = GlobalSettingsFormViewModel.From(snapshot.Values),
            Override = WarehouseSettingsOverrideFormViewModel.From(
                warehouseId ?? 0,
                snapshot.Overrides ?? new WmsWarehouseSettingsOverrides()),
            EffectiveValues = snapshot.Values,
            Warehouses = await GetWarehousesAsync(cancellationToken)
        };
    }

    private async Task<SettingsIndexViewModel> BuildGlobalPageAsync(
        GlobalSettingsFormViewModel model,
        CancellationToken cancellationToken)
    {
        var page = await BuildPageAsync(null, cancellationToken);
        page.Global = model;
        return page;
    }

    private async Task<SettingsIndexViewModel> BuildOverridePageAsync(
        WarehouseSettingsOverrideFormViewModel model,
        CancellationToken cancellationToken)
    {
        var page = await BuildPageAsync(model.WarehouseId, cancellationToken);
        page.Override = model;
        return page;
    }

    private async Task<IReadOnlyList<WmsWarehouseOption>> GetWarehousesAsync(
        CancellationToken cancellationToken) =>
        await warehouseAccessService.GetAccessibleWarehousesAsync(
            WmsPermissions.SettingsManage,
            cancellationToken);
}
