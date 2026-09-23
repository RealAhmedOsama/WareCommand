using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Lots;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/lots")]
[Authorize(Policy = WmsPermissions.InventoryRead)]
[EnableRateLimiting(WmsRateLimitPolicies.Report)]
public sealed class LotsController(
    ILotService lotService,
    ICurrentUser currentUser,
    IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<LotDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? itemSku,
        [FromQuery] string? number,
        [FromQuery] LotStatus? status,
        [FromQuery] bool includeClosed = false,
        CancellationToken cancellationToken = default)
    {
        var result = await lotService.SearchAsync(
            new LotSearchQuery(itemSku, number, status, includeClosed),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{lotId:int}/traceability")]
    [ProducesResponseType(typeof(LotTraceabilityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Traceability(
        int lotId,
        CancellationToken cancellationToken = default)
    {
        var result = await lotService.GetTraceabilityAsync(lotId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("expiry-alerts")]
    [ProducesResponseType(typeof(IEnumerable<LotExpiryAlertDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExpiryAlerts(
        [FromQuery] DateOnly? businessDate,
        [FromQuery, Range(0, 3_650)] int warningDays = 30,
        CancellationToken cancellationToken = default)
    {
        var date = businessDate ?? DateOnly.FromDateTime(clock.UtcNow.DateTime);
        var result = await lotService.GetExpiryAlertsAsync(date, warningDays, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("{lotId:int}")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int lotId,
        [FromBody] LotDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await lotService.UpdateAsync(
            lotId,
            request,
            currentUser.RequireUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{lotId:int}/status")]
    [Authorize(Policy = WmsPermissions.InventoryAdjust)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(
        int lotId,
        [FromBody] LotStatusChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = await lotService.ChangeStatusAsync(
            lotId,
            request.Status,
            request.Reason,
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

public sealed class LotStatusChangeRequest
{
    [Required]
    public LotStatus Status { get; init; }

    [Required]
    [StringLength(1_000)]
    public string Reason { get; init; } = string.Empty;
}
