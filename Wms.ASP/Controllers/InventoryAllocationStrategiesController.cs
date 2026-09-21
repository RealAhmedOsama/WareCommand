using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/allocation-strategies")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryAllocationStrategiesController(
    IInventoryAllocationStrategyService strategyService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryAllocationStrategyPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Policies(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] string? itemCategory,
        [FromQuery] string? demandType,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await strategyService.SearchAsync(
            new InventoryAllocationStrategyPolicyQuery(
                warehouseId,
                itemId,
                itemCategory,
                demandType,
                includeInactive),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] InventoryAllocationStrategyPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await strategyService.SaveAsync(
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
        [FromBody] InventoryAllocationStrategyPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await strategyService.SaveAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPost("simulate")]
    [ProducesResponseType(typeof(InventoryAllocationSimulationResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Simulate(
        [FromBody] InventoryAllocationSimulationInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await strategyService.SimulateAsync(
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
