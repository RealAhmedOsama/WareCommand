using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Administration;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.ASP.Extensions;
using Wms.ASP.Models;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.AccessManage)]
public sealed class AdministrationController(
    IAdministrationService administrationService,
    ILogger<AdministrationController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        int? warehouseId = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var catalog = await administrationService.GetCatalogAsync(cancellationToken);
        if (catalog.IsFailure)
        {
            logger.LogWarning("Administration catalog could not load: {Error}", catalog.Error);
            TempData["ErrorMessage"] = this.LocalizeError(catalog.FirstError!);
            return Forbid();
        }

        var readiness = await administrationService.GetReadinessAsync(warehouseId, cancellationToken);
        if (readiness.IsFailure)
        {
            logger.LogWarning("Administration readiness could not load: {Error}", readiness.Error);
            TempData["ErrorMessage"] = this.LocalizeError(readiness.FirstError!);
        }

        var history = await administrationService.GetHistoryAsync(
            new AdministrationHistoryQuery(
                WarehouseId: warehouseId,
                Page: Math.Max(page, 1),
                PageSize: 25),
            cancellationToken);

        return View(new AdministrationIndexViewModel
        {
            WarehouseId = warehouseId,
            Modules = catalog.Value,
            Readiness = readiness.IsSuccess ? readiness.Value : null,
            History = history.IsSuccess ? history.Value : null,
            HistoryError = history.IsFailure
                ? this.LocalizeError(history.FirstError!)
                : null
        });
    }
}
