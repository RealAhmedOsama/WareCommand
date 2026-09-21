using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/ownership/reports")]
[Authorize(Policy = WmsPermissions.InventoryOwnershipRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryOwnershipReportsController(
    IInventoryOwnershipReportService reportService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(InventoryOwnershipReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Query(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] InventoryOwnerKind? ownerKind,
        [FromQuery] int? inventoryOwnerId,
        [FromQuery] string? ownerCodeSnapshot,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await reportService.QueryAsync(
            new InventoryOwnershipReportQuery(
                warehouseId,
                itemId,
                ownerKind,
                inventoryOwnerId,
                ownerCodeSnapshot,
                fromUtc,
                toUtc,
                page,
                pageSize),
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
            ErrorType.Conflict or ErrorType.Concurrency => StatusCodes.Status409Conflict,
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
