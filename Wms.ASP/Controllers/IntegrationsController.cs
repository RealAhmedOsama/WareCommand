using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    IWebhookSubscriptionService webhookSubscriptionService) : ControllerBase
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
}
