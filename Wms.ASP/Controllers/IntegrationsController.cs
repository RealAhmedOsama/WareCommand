using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Integrations;
using Wms.ASP.Security;

namespace Wms.ASP.Controllers;

[ApiController]
[Route("api/integrations")]
[Authorize(Policy = WmsPermissions.SettingsManage)]
[EnableRateLimiting(WmsRateLimitPolicies.Api)]
public sealed class IntegrationsController(
    IIntegrationEventWriter integrationEventWriter,
    IIntegrationInboxService integrationInboxService,
    IIntegrationOutboxDispatcher integrationOutboxDispatcher,
    IWebhookDeliveryTransport webhookDeliveryTransport,
    IWebhookSubscriptionService webhookSubscriptionService,
    IIntegrationDeadLetterReplayService deadLetterReplayService,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("capabilities")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Capabilities(CancellationToken cancellationToken)
    {
        var capability = webhookDeliveryTransport.Capability;
        var verification = await webhookSubscriptionService.GetDeliveryVerificationSummaryAsync(
            cancellationToken);
        var verified = capability.Enabled &&
                       capability.Configured &&
                       verification.VerifiedSubscriptions > 0;
        var status = !capability.Enabled || !capability.Configured
            ? "Disabled"
            : verified
                ? "Verified"
                : "Configured";

        return Ok(new
        {
            outbox = new
            {
                writer = integrationEventWriter.GetType().Name,
                dispatcher = integrationOutboxDispatcher.GetType().Name
            },
            inbox = new
            {
                handler = integrationInboxService.GetType().Name
            },
            webhook = new
            {
                deliveryTransport = webhookDeliveryTransport.GetType().Name,
                status,
                enabled = capability.Enabled,
                configured = capability.Configured,
                verified,
                verificationMeaning = "Verified means an active endpoint has accepted at least one signed delivery; it does not certify an external partner integration.",
                activeSubscriptionCount = verification.ActiveSubscriptions,
                verifiedSubscriptionCount = verification.VerifiedSubscriptions
            }
        });
    }

    [HttpPost("outbox/{outboxMessageId:long}/replay")]
    public async Task<IActionResult> ReplayDeadLetter(
        long outboxMessageId,
        [FromBody] DeadLetterReplayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await deadLetterReplayService.ReplayAsync(
            outboxMessageId,
            request.Reason,
            currentUser.RequireUserId(),
            currentUser.UserName,
            cancellationToken);
        return ToActionResult(result);
    }

    public sealed record DeadLetterReplayRequest(
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(250, MinimumLength = 1)]
        string Reason);

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
