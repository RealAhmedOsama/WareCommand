using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Workforce;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/workforce")]
[Authorize(Policy = WmsPermissions.WorkRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class WorkforceController(
    IWorkforceService workforceService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("profiles")]
    public async Task<IActionResult> Profiles(
        [FromQuery] int warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.ListWorkerProfilesAsync(
            warehouseId,
            includeInactive,
            cancellationToken));

    [HttpPut("profiles/{userId}")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProfile(
        string userId,
        [FromBody] WorkerProfileInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveWorkerProfileAsync(
            userId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("queues")]
    public async Task<IActionResult> Queues(
        [FromQuery] int warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.ListQueuesAsync(
            warehouseId,
            includeInactive,
            cancellationToken));

    [HttpPost("queues")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateQueue(
        [FromBody] WorkQueueInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveQueueAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPut("queues/{queueId:int}")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQueue(
        int queueId,
        [FromBody] WorkQueueInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveQueueAsync(
            queueId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("suggestions")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    public async Task<IActionResult> Suggestions(
        [FromQuery] int warehouseId,
        [FromQuery] Wms.Domain.Enums.WarehouseWorkType? workType,
        [FromQuery] string? queueCode,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SuggestAsync(
            new WorkforceSuggestionsQuery(warehouseId, workType, queueCode, limit),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("work/{workId:int}/activity")]
    [Authorize(Policy = WmsPermissions.WorkExecute)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordActivity(
        int workId,
        [FromBody] WorkforceActivityRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.RecordActivityAsync(
            request.ToInput(workId),
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("metrics")]
    [Authorize(Policy = WmsPermissions.ReportsRead)]
    public async Task<IActionResult> Metrics(
        [FromQuery] int warehouseId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? queueCode,
        [FromQuery] string? workerUserId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.GetMetricsAsync(
            new WorkforceMetricsQuery(warehouseId, fromUtc, toUtc, queueCode, workerUserId),
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

public sealed class WorkforceActivityRequest
{
    public Wms.Domain.Enums.WarehouseWorkActivityCategory Category { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? EndedAtUtc { get; init; }
    public string? Reason { get; init; }

    public WorkforceActivityInput ToInput(int workId) =>
        new(workId, Category, StartedAtUtc, EndedAtUtc, Reason);
}
