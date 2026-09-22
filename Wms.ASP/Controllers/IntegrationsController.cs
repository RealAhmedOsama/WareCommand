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
    IWebhookDeliveryTransport webhookDeliveryTransport) : ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities() => Ok(new
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
            deliveryTransport = webhookDeliveryTransport.GetType().Name
        }
    });
}
