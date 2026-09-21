using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/dispositions")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class InventoryDispositionsController(
    IInventoryDispositionService dispositionService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(
        [FromQuery] int? warehouseId,
        [FromQuery] int? itemId,
        [FromQuery] string? itemCategory,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.SearchPoliciesAsync(
            new InventoryDispositionPolicyQuery(warehouseId, itemId, itemCategory, includeInactive),
            cancellationToken));

    [HttpPost("policies")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] InventoryDispositionPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.SavePolicyAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPut("policies/{policyId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePolicy(
        int policyId,
        [FromBody] InventoryDispositionPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.SavePolicyAsync(
            policyId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("expiry-candidates")]
    public async Task<IActionResult> ExpiryCandidates(
        [FromQuery] int warehouseId,
        [FromQuery] DateTime? asOfUtc = null,
        [FromQuery] int limit = 500,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.GetExpiryCandidatesAsync(
            new InventoryExpiryCandidateQuery(warehouseId, asOfUtc, limit),
            cancellationToken));

    [HttpPost]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromBody] InventoryDispositionCreateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.CreateAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{dispositionId:int}/approve")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(
        int dispositionId,
        [FromBody] InventoryDispositionApprovalInput? input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.ApproveAsync(
            dispositionId,
            currentUser.RequireUserId(),
            input,
            cancellationToken));

    [HttpPost("{dispositionId:int}/execute")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(
        int dispositionId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.ExecuteAsync(
            dispositionId,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("recalls")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRecall(
        [FromBody] InventoryRecallCreateInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.CreateRecallAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("recalls/{recallCaseId:int}/close")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CloseRecall(
        int recallCaseId,
        [FromBody] InventoryRecallCloseInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.CloseRecallAsync(
            recallCaseId,
            currentUser.RequireUserId(),
            input,
            cancellationToken));

    [HttpGet("recalls/{recallCaseId:int}/trace")]
    public async Task<IActionResult> RecallTrace(
        int recallCaseId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.GetRecallTraceAsync(
            recallCaseId,
            cancellationToken));

    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(
        [FromQuery] int warehouseId,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await dispositionService.GetMetricsAsync(
            new InventoryDispositionMetricsQuery(warehouseId, fromUtc, toUtc),
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
