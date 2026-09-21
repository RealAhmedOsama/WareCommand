using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.ASP.Security;
using Wms.Domain.Enums;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class NotificationsController(INotificationService notificationService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    public async Task<IActionResult> List(
        [FromQuery] string? kind,
        [FromQuery] NotificationSeverity? severity,
        [FromQuery] int? warehouseId,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] bool includeExpired = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.ListAsync(
            new NotificationQuery(kind, severity, warehouseId, unreadOnly, includeExpired, page, pageSize),
            cancellationToken));

    [HttpGet("unread-count")]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    public async Task<IActionResult> UnreadCount(
        [FromQuery] int? warehouseId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.GetUnreadCountAsync(warehouseId, cancellationToken));

    [HttpPost("{recipientId:long}/read")]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(
        long recipientId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.MarkReadAsync(recipientId, cancellationToken));

    [HttpPost("{recipientId:long}/acknowledge")]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(
        long recipientId,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.AcknowledgeAsync(recipientId, cancellationToken));

    [HttpGet("preferences")]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    public async Task<IActionResult> Preferences(
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.ListPreferencesAsync(cancellationToken));

    [HttpPut("preferences")]
    [Authorize(Policy = WmsPermissions.NotificationsRead)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreference(
        [FromBody] PreferenceRequest request,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await notificationService.SavePreferenceAsync(
            new NotificationPreferenceInput(
                request.Scope,
                request.RoleName,
                request.WarehouseId,
                request.Kind,
                request.Channel,
                request.IsEnabled,
                request.QuietStartMinute,
                request.QuietEndMinute,
                request.TimeZone,
                request.DigestMinutes),
            cancellationToken));

    private IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ToProblem(result.FirstError!);
    }

    private IActionResult ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ToProblem(result.FirstError!);
    }

    private ObjectResult ToProblem(ResultError error)
    {
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

    public sealed class PreferenceRequest
    {
        public NotificationPreferenceScope Scope { get; init; }

        [StringLength(100)]
        public string? RoleName { get; init; }

        [Range(1, int.MaxValue)]
        public int? WarehouseId { get; init; }

        [StringLength(100)]
        public string? Kind { get; init; }

        public NotificationChannel Channel { get; init; } = NotificationChannel.InApp;

        public bool IsEnabled { get; init; } = true;

        [Range(0, 1_439)]
        public int? QuietStartMinute { get; init; }

        [Range(0, 1_439)]
        public int? QuietEndMinute { get; init; }

        [StringLength(100)]
        public string? TimeZone { get; init; }

        [Range(1, 1_440)]
        public int? DigestMinutes { get; init; }
    }
}
