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
[Route("api/inventory/slotting")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class SlottingController(
    ISlottingService slottingService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    [ProducesResponseType(typeof(IReadOnlyList<SlottingPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Policies(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await slottingService.SearchPoliciesAsync(
            new SlottingPolicyQuery(warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] SlottingPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await slottingService.SavePolicyAsync(
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
        [FromBody] SlottingPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await slottingService.SavePolicyAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("analyze")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(SlottingAnalysisResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Analyze(
        [FromBody] SlottingAnalysisQuery query,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await slottingService.AnalyzeAsync(
            query,
            currentUser.RequireUserId(),
            cancellationToken: cancellationToken));

    [HttpPost("simulate")]
    [ProducesResponseType(typeof(SlottingAnalysisResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Simulate(
        [FromBody] SlottingAnalysisQuery query,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await slottingService.AnalyzeAsync(
            query with { DryRun = true },
            currentUser.RequireUserId(),
            cancellationToken: cancellationToken));

    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(IReadOnlyList<SlottingRecommendationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Recommendations(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] SlottingRecommendationStatus? status,
        [FromQuery] bool includeHistorical = true,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await slottingService.SearchRecommendationsAsync(
            new SlottingRecommendationQuery(warehouseId, itemId, status, includeHistorical, limit),
            cancellationToken));

    [HttpPost("recommendations/{recommendationId:int}/approve")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        int recommendationId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await slottingService.ApproveAsync(
            recommendationId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("recommendations/{recommendationId:int}/reject")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int recommendationId,
        [FromBody] SlottingRejectionInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await slottingService.RejectAsync(
            recommendationId,
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
