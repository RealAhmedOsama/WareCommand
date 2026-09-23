using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Dashboard;
using Wms.Application.Identity;
using Wms.ASP.Models;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[Authorize(Policy = WmsPermissions.DashboardView)]
public sealed class DashboardController(
    IDashboardReadService dashboardReadService,
    ILogger<DashboardController> logger) : Controller
{
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await dashboardReadService.GetAsync(warehouseId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Dashboard page could not be loaded: {ErrorCode}", result.ErrorCode);
            return SnapshotFailure(result, isRefresh: false);
        }

        return View(DashboardViewModel.From(result.Value));
    }

    [HttpGet]
    [EnableRateLimiting(WmsRateLimitPolicies.Report)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshData(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await dashboardReadService.GetAsync(warehouseId, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Dashboard refresh failed: {ErrorCode}", result.ErrorCode);
            return SnapshotFailure(result, isRefresh: true);
        }

        return Json(result.Value);
    }

    private IActionResult SnapshotFailure(Result result, bool isRefresh)
    {
        if (result.ErrorCode.StartsWith("authorization.", StringComparison.Ordinal))
        {
            return isRefresh ? StatusCode(StatusCodes.Status403Forbidden) : Forbid();
        }

        if (result.ErrorCode is "warehouse.not_found" or "settings.warehouse_not_found")
        {
            return NotFound();
        }

        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Dashboard data unavailable",
            detail: "Dashboard data could not be loaded. Please try again.");
    }
}
