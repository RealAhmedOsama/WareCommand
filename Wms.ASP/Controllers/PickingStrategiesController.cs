using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/outbound/picking-strategies")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class PickingStrategiesController(
    IPickingStrategyService pickingStrategyService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        [FromQuery] PickingStrategyKind? strategy = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.SearchPoliciesAsync(
            new PickingStrategyPolicyQuery(warehouseId, includeInactive, strategy),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] PickingStrategyPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await pickingStrategyService.SavePolicyAsync(
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
        [FromBody] PickingStrategyPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.SavePolicyAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("plans")]
    public async Task<IActionResult> Plans(
        [FromQuery] int? warehouseId,
        [FromQuery] int? waveId,
        [FromQuery] PickingStrategyKind? strategy,
        [FromQuery] PickingPlanStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.SearchAsync(
            new PickingPlanQuery(warehouseId, waveId, strategy, status, page, pageSize),
            cancellationToken));

    [HttpGet("plans/{planId:int}")]
    public async Task<IActionResult> GetPlan(
        int planId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.GetAsync(planId, cancellationToken));

    [HttpPost("plans/simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] PickingPlanCreateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.SimulateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlan(
        [FromBody] PickingPlanCreateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.CreatePlanAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans/{planId:int}/containers/{containerId:int}/scan")]
    [Authorize(Policy = WmsPermissions.PickingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ScanContainer(
        int planId,
        int containerId,
        [FromBody] PickingContainerScanInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.ScanContainerAsync(
            planId,
            containerId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans/{planId:int}/handoffs/{handoffId:int}/complete")]
    [Authorize(Policy = WmsPermissions.PickingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteHandoff(
        int planId,
        int handoffId,
        [FromBody] PickingHandoffInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.CompleteHandoffAsync(
            planId,
            handoffId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans/{planId:int}/refresh")]
    [Authorize(Policy = WmsPermissions.PickingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(
        int planId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.RefreshAsync(
            planId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans/{planId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int planId,
        [FromBody] PickingPlanCancellationInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await pickingStrategyService.CancelAsync(
            planId,
            input.IdempotencyKey,
            input.Reason,
            currentUser.RequireUserId(),
            cancellationToken));

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

public sealed record PickingPlanCancellationInput(string IdempotencyKey, string Reason);
