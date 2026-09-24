using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/inventory/cycle-counts")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class CycleCountsController(
    ICycleCountService cycleCountService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<CycleCountPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Plans(
        [FromQuery] int? warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.SearchPlansAsync(
            new CycleCountPlanQuery(warehouseId, includeInactive),
            cancellationToken));

    [HttpPost("plans")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlan(
        [FromBody] CycleCountPlanInput input,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return ToActionResult(await cycleCountService.SavePlanAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));
    }

    [HttpPut("plans/{planId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePlan(
        int planId,
        [FromBody] CycleCountPlanInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.SavePlanAsync(
            planId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("generate")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(CycleCountGenerationResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate(
        [FromQuery] int? warehouseId,
        [FromQuery] int? planId,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.GenerateAsync(
            new CycleCountGenerationQuery(warehouseId, planId, limit),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("tasks/{taskId:int}")]
    [ProducesResponseType(typeof(CycleCountTaskDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTask(
        int taskId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.GetTaskAsync(taskId, cancellationToken));

    [HttpPost("tasks/{taskId:int}/start")]
    [Authorize(Policy = WmsPermissions.CountingExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTask(
        int taskId,
        [FromBody] CycleCountTaskStartInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.StartTaskAsync(
            taskId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("tasks/{taskId:int}/approve")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveTask(
        int taskId,
        [FromBody] CycleCountTaskApprovalInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await cycleCountService.ApproveTaskAsync(
            taskId,
            input,
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
