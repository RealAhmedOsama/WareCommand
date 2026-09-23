using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Integrations;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Integrations;

namespace Wms.Infrastructure.Notifications;

public sealed class IntegrationWebhookNotificationAdapter(
    WmsDbContext context,
    IIntegrationEventWriter eventWriter,
    IWebhookDeliveryTransport transport) : INotificationChannelAdapter
{
    public NotificationChannel Channel => NotificationChannel.Webhook;

    public async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!transport.Capability.Enabled || !transport.Capability.Configured ||
            message.WarehouseId is not > 0)
        {
            return Disabled("webhook_not_configured_for_warehouse");
        }

        var subscriptions = await context.WebhookSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.Status == WmsWebhookSubscriptionStatuses.Active)
            .ToListAsync(cancellationToken);
        var hasScopedSubscription = subscriptions.Any(subscription =>
            HasEventType(subscription.EventTypesJson) &&
            HasWarehouseScope(subscription.WarehouseIdsJson, message.WarehouseId.Value));
        if (!hasScopedSubscription)
        {
            return Disabled("webhook_no_scoped_subscription");
        }

        var eventId = CreateEventId(message.NotificationId);
        var publish = await eventWriter.EnqueueAsync(
            new IntegrationEventDraft(
                WmsIntegrationEventTypes.NotificationPublished,
                AggregateType: "Notification",
                AggregateKey: message.NotificationId.ToString(CultureInfo.InvariantCulture),
                Payload: new
                {
                    notificationId = message.NotificationId,
                    message.Kind,
                    severity = message.Severity.ToString(),
                    message.TitleEn,
                    message.TitleAr,
                    message.MessageEn,
                    message.MessageAr,
                    message.DeepLink,
                    warehouseId = message.WarehouseId,
                    message.Mandatory,
                    message.CorrelationId
                },
                WarehouseId: message.WarehouseId,
                CorrelationId: message.CorrelationId,
                EventId: eventId),
            cancellationToken);

        var outbox = context.IntegrationOutbox.Local
            .FirstOrDefault(item => item.EventId == publish.EventId)
            ?? await context.IntegrationOutbox.SingleAsync(
                item => item.EventId == publish.EventId,
                cancellationToken);
        if (outbox.Status == WmsIntegrationEventStatuses.Delivered)
        {
            return new NotificationDeliveryResult(
                Succeeded: true,
                Retryable: false,
                SuccessStatus: NotificationDeliveryStatus.TransportAccepted);
        }

        if (outbox.Status == WmsIntegrationEventStatuses.DeadLettered)
        {
            return new NotificationDeliveryResult(false, false, "webhook_dead_lettered");
        }

        if (outbox.Status == WmsIntegrationEventStatuses.NoSubscribers)
        {
            return Disabled("webhook_no_scoped_subscription");
        }

        return new NotificationDeliveryResult(
            Succeeded: true,
            Retryable: false,
            SuccessStatus: NotificationDeliveryStatus.Queued);
    }

    private static bool HasEventType(string json)
    {
        try
        {
            var eventTypes = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return eventTypes.Count == 0 || eventTypes.Contains(
                WmsIntegrationEventTypes.NotificationPublished,
                StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasWarehouseScope(string json, int warehouseId)
    {
        try
        {
            var warehouseIds = JsonSerializer.Deserialize<List<int>>(json) ?? [];
            return warehouseIds.Contains(warehouseId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Guid CreateEventId(long notificationId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"warecommand:notification:v1:{notificationId.ToString(CultureInfo.InvariantCulture)}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static NotificationDeliveryResult Disabled(string error) =>
        new(Succeeded: false, Retryable: false, Error: error, Disabled: true);
}
