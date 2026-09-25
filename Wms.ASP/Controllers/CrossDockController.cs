using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inbound/cross-dock")]
[Authorize(Policy = WmsPermissions.WorkRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class CrossDockController(
    ICrossDockService crossDockService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(
        [FromQuery] int warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.ListPoliciesAsync(
            warehouseId,
            includeInactive,
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CrossDockPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await crossDockService.SavePolicyAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("policies/{policyId:int}")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePolicy(
        int policyId,
        [FromBody] CrossDockPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.SavePolicyAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(
        [FromBody] CrossDockSimulationInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.SimulateAsync(input, cancellationToken));

    [HttpPost("plans")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlan(
        [FromBody] CrossDockPlanCreateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.CreatePlanAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("plans/{planId:int}")]
    public async Task<IActionResult> GetPlan(
        int planId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.GetAsync(planId, cancellationToken));

    [HttpPost("plans/{planId:int}/execute")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExecutePlan(
        int planId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.ExecutePlanAsync(
            planId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("plans/{planId:int}/cancel")]
    [Authorize(Policy = WmsPermissions.AllocationManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPlan(
        int planId,
        [FromBody] CrossDockCancellationInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await crossDockService.CancelAsync(
            planId,
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

public sealed record CrossDockCancellationInput(string Reason);
