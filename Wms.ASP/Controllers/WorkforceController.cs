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

    [HttpGet("routes")]
    public async Task<IActionResult> Routes(
        [FromQuery] int warehouseId,
        [FromQuery] string? routeCode,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.ListRoutesAsync(
            warehouseId,
            routeCode,
            includeInactive,
            cancellationToken));

    [HttpPost("routes")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRoute(
        [FromBody] WorkRouteInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveRouteAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPut("routes/{routeId:int}")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRoute(
        int routeId,
        [FromBody] WorkRouteInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveRouteAsync(
            routeId,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("interleaving-policies")]
    public async Task<IActionResult> InterleavingPolicies(
        [FromQuery] int warehouseId,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.ListInterleavingPoliciesAsync(
            warehouseId,
            includeInactive,
            cancellationToken));

    [HttpPost("interleaving-policies")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInterleavingPolicy(
        [FromBody] WorkInterleavingPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveInterleavingPolicyAsync(
            null,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPut("interleaving-policies/{policyId:int}")]
    [Authorize(Policy = WmsPermissions.WorkManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInterleavingPolicy(
        int policyId,
        [FromBody] WorkInterleavingPolicyInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SaveInterleavingPolicyAsync(
            policyId,
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
        [FromQuery] int? currentLocationId = null,
        [FromQuery] string? routeCode = null,
        [FromQuery] string? policyCode = null,
        [FromQuery] int? manualOverrideWorkId = null,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await workforceService.SuggestAsync(
            new WorkforceSuggestionsQuery(
                warehouseId,
                workType,
                queueCode,
                limit,
                currentLocationId,
                routeCode,
                policyCode,
                manualOverrideWorkId),
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
