using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/classifications")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryClassificationsController(
    IInventoryClassificationService classificationService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryClassificationPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Policies(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await classificationService.SearchPoliciesAsync(
            new InventoryClassificationPolicyQuery(warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] InventoryClassificationPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await classificationService.SavePolicyAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("policies/{policyId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePolicy(
        int policyId,
        [FromBody] InventoryClassificationPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await classificationService.SavePolicyAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryClassificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] Wms.Domain.Enums.InventoryClassificationClass? classification,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await classificationService.SearchAsync(
            new InventoryClassificationQuery(warehouseId, itemId, classification, limit),
            cancellationToken));

    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryClassificationHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> History(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await classificationService.SearchHistoryAsync(
            new InventoryClassificationHistoryQuery(warehouseId, itemId, limit),
            cancellationToken));

    [HttpPost("recalculate")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(InventoryClassificationRecalculationResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Recalculate(
        [FromQuery] int? warehouseId,
        [FromQuery] int? policyId,
        [FromQuery] DateTime? asOfUtc,
        [FromQuery] int limit = 500,
        [FromQuery] bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await classificationService.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                warehouseId,
                policyId,
                asOfUtc,
                limit,
                dryRun),
            currentUser.RequireUserId(),
            cancellationToken: cancellationToken));

    [HttpPut("overrides")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Override(
        [FromBody] InventoryClassificationOverrideInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await classificationService.OverrideAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));
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
