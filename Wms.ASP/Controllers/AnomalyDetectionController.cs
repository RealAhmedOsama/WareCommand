using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.AnomalyDetection;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Jobs;
using Wms.ASP.Jobs;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/anomalies")]
[Authorize(Policy = WmsPermissions.AnomalyRead)]
public sealed class AnomalyDetectionController(
    IAnomalyDetectionService anomalyDetectionService,
    IWarehouseAccessService warehouseAccessService,
    IWmsJobDispatcher jobDispatcher,
    ICurrentUser currentUser,
    IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AnomalyFindingPageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] int? warehouseId,
        [FromQuery] AnomalyRuleKind? ruleKind,
        [FromQuery] AnomalySeverity? severity,
        [FromQuery] AnomalyStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await anomalyDetectionService.SearchAsync(
            new AnomalyFindingSearchQuery(warehouseId, ruleKind, severity, status, page, pageSize),
            cancellationToken));

    [HttpGet("{fingerprint}")]
    [ProducesResponseType(typeof(AnomalyFindingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        string fingerprint,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await anomalyDetectionService.GetAsync(fingerprint, cancellationToken));

    [HttpPost("recalculate")]
    [Authorize(Policy = WmsPermissions.AnomalyManage)]
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(AnomalyRecalculationAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Recalculate(
        [FromBody] AnomalyRecalculationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.WarehouseId <= 0)
        {
            ModelState.AddModelError(nameof(input.WarehouseId), "A positive warehouse identifier is required.");
        }

        var toUtc = input.ToUtc?.ToUniversalTime() ?? UtcDayStart(clock.UtcNow);
        var fromUtc = input.FromUtc?.ToUniversalTime() ?? toUtc.AddDays(-7);
        if (fromUtc > toUtc || toUtc - fromUtc > TimeSpan.FromDays(AnomalyDetectionPolicy.MaximumWindowDays))
        {
            ModelState.AddModelError(nameof(input.FromUtc), "The anomaly window must be ordered and no longer than thirty-one days.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AnomalyManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return ToActionResult(Result.Failure<AnomalyRecalculationAcceptedDto>(authorization.FirstError!));
        }

        var actorId = currentUser.RequireUserId();
        var idempotencyKey = string.Create(
            CultureInfo.InvariantCulture,
            $"{WmsJobNames.AnomalyDetection}:manual:{input.WarehouseId}:{fromUtc:yyyyMMddHHmmssfffffff}:{toUtc:yyyyMMddHHmmssfffffff}");
        var referenceId = string.Create(CultureInfo.InvariantCulture, $"{fromUtc:O}|{toUtc:O}");
        try
        {
            var jobId = jobDispatcher.Enqueue(
                WmsJobNames.AnomalyDetection,
                idempotencyKey,
                actorId,
                currentUser.UserName,
                input.WarehouseId,
                referenceId);
            return Accepted(new AnomalyRecalculationAcceptedDto(
                jobId,
                idempotencyKey,
                input.WarehouseId,
                fromUtc,
                toUtc));
        }
        catch (InvalidOperationException)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Anomaly jobs unavailable",
                detail: "Durable background jobs are disabled for this host.");
        }
    }

    [HttpPost("{fingerprint}/transition")]
    [Authorize(Policy = WmsPermissions.AnomalyManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(
        string fingerprint,
        [FromBody] AnomalyDispositionRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await anomalyDetectionService.TransitionAsync(
            fingerprint,
            request,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpPost("{fingerprint}/assignment")]
    [Authorize(Policy = WmsPermissions.AnomalyManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(
        string fingerprint,
        [FromBody] AnomalyAssignmentInput input,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await anomalyDetectionService.AssignAsync(
            fingerprint,
            input,
            currentUser.RequireUserId(),
            cancellationToken));

    [HttpGet("rules")]
    [ProducesResponseType(typeof(IReadOnlyList<AnomalyRuleSettingDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rules(
        [FromQuery] int? warehouseId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await anomalyDetectionService.ListRuleSettingsAsync(warehouseId, cancellationToken));

    [HttpPut("rules")]
    [Authorize(Policy = WmsPermissions.AnomalyRulesManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRule(
        [FromBody] AnomalyRuleSettingInput input,
        CancellationToken cancellationToken = default)
    {
        var result = await anomalyDetectionService.SaveRuleSettingAsync(
            input,
            currentUser.RequireUserId(),
            cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Rules), new { warehouseId = result.Value.WarehouseId }, result.Value)
            : ToActionResult(result);
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

    private static DateTimeOffset UtcDayStart(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }
}

public sealed record AnomalyRecalculationAcceptedDto(
    string JobId,
    string IdempotencyKey,
    int WarehouseId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc);
