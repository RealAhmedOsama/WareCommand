using Wms.Application.Notifications;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Notifications;

/// <summary>
/// External delivery is deliberately fail-closed until a provider adapter is
/// configured. The durable recipient row records the missing dependency and the
/// integration-retry job can retry after a real adapter is registered.
/// </summary>
public sealed class UnconfiguredEmailNotificationAdapter : INotificationChannelAdapter
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryMessage message,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NotificationDeliveryResult(
            Succeeded: false,
            Retryable: true,
            Error: "The email notification adapter is not configured for this host."));
}

public sealed class UnconfiguredWebhookNotificationAdapter : INotificationChannelAdapter
{
    public NotificationChannel Channel => NotificationChannel.Webhook;

    public Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryMessage message,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NotificationDeliveryResult(
            Succeeded: false,
            Retryable: true,
            Error: "The webhook notification adapter is not configured for this host."));
}
