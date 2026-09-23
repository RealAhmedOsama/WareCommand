using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/reconciliation")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryReconciliationController(
    IInventoryReconciliationService reconciliationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(InventoryReconciliationReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] bool deep = true,
        [FromQuery] int batchSize = 500,
        [FromQuery] int maxIssues = 2_000,
        CancellationToken cancellationToken = default)
    {
        var result = await reconciliationService.ReconcileAsync(
            new InventoryReconciliationQuery(
                warehouseId,
                itemId,
                fromUtc,
                toUtc,
                deep,
                batchSize,
                maxIssues),
            cancellationToken);
        return ToActionResult(result);
    }

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        var error = result.FirstError!;
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict or ErrorType.Concurrency or ErrorType.BusinessRule => StatusCodes.Status409Conflict,
            ErrorType.Dependency => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        return Problem(
            statusCode: statusCode,
            title: error.Type.ToString(),
            detail: error.Message,
            type: $"https://warecommand.local/problems/{error.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = error.Code,
                ["retryable"] = error.IsRetryable
            });
    }
}
