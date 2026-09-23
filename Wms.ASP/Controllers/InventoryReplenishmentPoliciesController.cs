using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/replenishment-policies")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryReplenishmentPoliciesController(
    IInventoryReplenishmentPolicyService policyService,
    IReplenishmentExecutionService replenishmentExecutionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryReplenishmentPolicyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] int? locationId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await policyService.SearchAsync(
            new InventoryReplenishmentPolicyQuery(
                warehouseId,
                itemId,
                locationId,
                includeInactive),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("signals")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryReplenishmentSignalDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Signals(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] int? locationId,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var result = await policyService.GetSignalsAsync(
            new InventoryReplenishmentSignalQuery(warehouseId, itemId, locationId, limit),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("generate")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(ReplenishmentGenerationResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate(
        [FromQuery] int? warehouseId,
        [FromQuery] int? policyId,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var result = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(warehouseId, policyId, limit),
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] InventoryReplenishmentPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await policyService.SaveAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{policyId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int policyId,
        [FromBody] InventoryReplenishmentPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await policyService.SaveAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
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
