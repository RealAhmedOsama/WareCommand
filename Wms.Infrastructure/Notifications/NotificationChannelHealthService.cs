using Microsoft.EntityFrameworkCore;
using Wms.Application.Integrations;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Integrations;

namespace Wms.Infrastructure.Notifications;

public sealed class NotificationChannelHealthService(
    WmsDbContext context,
    IEmailTransport emailTransport,
    IWebhookDeliveryTransport webhookTransport) : INotificationChannelHealthService
{
    public async Task<NotificationChannelHealthDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var emailCounts = await ReadCountsAsync(NotificationChannel.Email, cancellationToken);
        var webhookCounts = await ReadCountsAsync(NotificationChannel.Webhook, cancellationToken);
        var emailCapability = emailTransport.Capability;
        var webhookReadiness = await GetWebhookReadinessAsync(cancellationToken);
        var webhookCapability = webhookTransport.Capability;

        return new NotificationChannelHealthDto(
            CreateHealth(
                emailCapability.Enabled,
                emailCapability.Configured,
                emailCapability.Verified,
                emailCounts),
            CreateHealth(
                webhookCapability.Enabled && webhookReadiness.HasScopedSubscription,
                webhookCapability.Configured && webhookReadiness.HasScopedSubscription,
                webhookCapability.Enabled && webhookCapability.Configured && webhookReadiness.Verified,
                webhookCounts));
    }

    private async Task<IReadOnlyDictionary<NotificationDeliveryStatus, int>> ReadCountsAsync(
        NotificationChannel channel,
        CancellationToken cancellationToken) =>
        await context.NotificationRecipients
            .AsNoTracking()
            .Where(recipient => recipient.Channel == channel)
            .GroupBy(recipient => recipient.DeliveryStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

    private async Task<WebhookReadiness> GetWebhookReadinessAsync(CancellationToken cancellationToken)
    {
        var subscriptions = await context.WebhookSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.Status == WmsWebhookSubscriptionStatuses.Active)
            .ToListAsync(cancellationToken);
        var eligible = subscriptions.Where(subscription =>
        {
            try
            {
                var eventTypes = WebhookSubscriptionService.ReadEventTypes(subscription);
                var warehouseIds = WebhookSubscriptionService.ReadWarehouseIds(subscription);
                return (eventTypes.Count == 0 || eventTypes.Contains(
                           WmsIntegrationEventTypes.NotificationPublished,
                           StringComparer.Ordinal)) &&
                       warehouseIds.Count > 0;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }).ToArray();
        return new WebhookReadiness(
            eligible.Length > 0,
            eligible.Any(subscription => subscription.LastDeliveryAtUtc.HasValue));
    }

    private static NotificationChannelHealth CreateHealth(
        bool enabled,
        bool configured,
        bool verified,
        IReadOnlyDictionary<NotificationDeliveryStatus, int> counts)
    {
        var accepted = Get(counts, NotificationDeliveryStatus.TransportAccepted);
        var state = !enabled || !configured
            ? "Disabled"
            : verified
                ? "Verified"
                : "Configured";
        return new NotificationChannelHealth(
            state,
            enabled,
            configured,
            verified,
            Get(counts, NotificationDeliveryStatus.Pending) + Get(counts, NotificationDeliveryStatus.Queued),
            Get(counts, NotificationDeliveryStatus.Failed),
            Get(counts, NotificationDeliveryStatus.Disabled),
            Get(counts, NotificationDeliveryStatus.DeadLettered),
            accepted);
    }

    private static int Get(
        IReadOnlyDictionary<NotificationDeliveryStatus, int> counts,
        NotificationDeliveryStatus status) =>
        counts.TryGetValue(status, out var count) ? count : 0;

    private sealed record WebhookReadiness(bool HasScopedSubscription, bool Verified);
}
