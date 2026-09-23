using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Integrations;

public sealed class IntegrationOutboxDispatcher(
    WmsDbContext context,
    IWebhookDeliveryTransport transport,
    IWebhookSecretProtector secretProtector,
    IClock clock,
    ILogger<IntegrationOutboxDispatcher> logger) : IIntegrationOutboxDispatcher
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours(1);
    private const int MaximumErrorLength = 2_000;

    public async Task<IntegrationDispatchResult> DispatchAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var now = clock.UtcNow;
        var candidateQuery = context.IntegrationOutbox
            .Where(message =>
                message.Status == WmsIntegrationEventStatuses.Pending ||
                message.Status == WmsIntegrationEventStatuses.Failed ||
                message.Status == WmsIntegrationEventStatuses.Processing);
        IReadOnlyList<WmsIntegrationOutboxEntity> candidates;
        if (UsesPortableDateComparison())
        {
            candidates = (await candidateQuery.ToListAsync(cancellationToken))
                .Where(message =>
                    (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now) &&
                    (message.LeaseUntilUtc == null || message.LeaseUntilUtc <= now))
                .OrderBy(message => message.NextAttemptAtUtc)
                .ThenBy(message => message.Id)
                .Take(maximumCount)
                .ToList();
        }
        else
        {
            candidates = await candidateQuery
                .Where(message =>
                    (message.NextAttemptAtUtc == null || message.NextAttemptAtUtc <= now) &&
                    (message.LeaseUntilUtc == null || message.LeaseUntilUtc <= now))
                .OrderBy(message => message.NextAttemptAtUtc)
                .ThenBy(message => message.Id)
                .Take(maximumCount)
                .ToListAsync(cancellationToken);
        }

        var totals = new DispatchTotals();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = await ClaimAsync(candidate.Id, now, cancellationToken);
            if (message is null)
            {
                continue;
            }

            totals.EventsClaimed++;
            var subscriptions = await context.WebhookSubscriptions
                .Where(subscription => subscription.Status == WmsWebhookSubscriptionStatuses.Active)
                .ToListAsync(cancellationToken);
            var matchingSubscriptions = subscriptions
                .Where(subscription => Matches(subscription, message))
                .ToList();
            if (matchingSubscriptions.Count == 0)
            {
                message.Status = WmsIntegrationEventStatuses.NoSubscribers;
                message.DeliveredAtUtc = now;
                message.LeaseUntilUtc = null;
                message.NextAttemptAtUtc = null;
                await UpdateNotificationRecipientsAsync(
                    message,
                    NotificationRecipientDeliveryState.Disabled,
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                continue;
            }

            await EnsureDeliveriesAsync(message, matchingSubscriptions, cancellationToken);
            foreach (var subscription in matchingSubscriptions)
            {
                var delivery = await context.WebhookDeliveries.SingleAsync(
                    item => item.OutboxMessageId == message.Id && item.SubscriptionId == subscription.Id,
                    cancellationToken);
                var outcome = await DispatchDeliveryAsync(message, subscription, delivery, now, cancellationToken);
                if (outcome is null)
                {
                    continue;
                }

                totals.DeliveriesAttempted++;
                if (outcome.Succeeded)
                {
                    totals.DeliveriesSucceeded++;
                }
                else
                {
                    totals.DeliveriesFailed++;
                    if (delivery.Status == WmsWebhookDeliveryStatuses.DeadLettered)
                    {
                        totals.EventsDeadLettered++;
                    }
                }
            }

            await FinalizeMessageAsync(message.Id, now, cancellationToken);
        }

        return new IntegrationDispatchResult(
            totals.EventsClaimed,
            totals.DeliveriesAttempted,
            totals.DeliveriesSucceeded,
            totals.DeliveriesFailed,
            totals.EventsDeadLettered);
    }

    private async Task<WmsIntegrationOutboxEntity?> ClaimAsync(
        long messageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = await context.IntegrationOutbox.SingleAsync(
            item => item.Id == messageId,
            cancellationToken);
        if (message.LeaseUntilUtc > now)
        {
            return null;
        }

        message.Status = WmsIntegrationEventStatuses.Processing;
        message.AttemptCount++;
        message.LeaseUntilUtc = now.Add(ProcessingLease);
        message.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
        return message;
    }

    private async Task EnsureDeliveriesAsync(
        WmsIntegrationOutboxEntity message,
        IReadOnlyList<WmsWebhookSubscriptionEntity> subscriptions,
        CancellationToken cancellationToken)
    {
        var existing = await context.WebhookDeliveries
            .Where(delivery => delivery.OutboxMessageId == message.Id)
            .Select(delivery => delivery.SubscriptionId)
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions.Where(subscription => !existing.Contains(subscription.Id)))
        {
            context.WebhookDeliveries.Add(new WmsWebhookDeliveryEntity
            {
                OutboxMessageId = message.Id,
                SubscriptionId = subscription.Id,
                Status = WmsWebhookDeliveryStatuses.Pending
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<WebhookDeliveryResult?> DispatchDeliveryAsync(
        WmsIntegrationOutboxEntity message,
        WmsWebhookSubscriptionEntity subscription,
        WmsWebhookDeliveryEntity delivery,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (delivery.Status == WmsWebhookDeliveryStatuses.Delivered)
        {
            return null;
        }

        if (delivery.Status == WmsWebhookDeliveryStatuses.Failed &&
            delivery.NextAttemptAtUtc is { } nextAttemptAtUtc &&
            nextAttemptAtUtc > now)
        {
            return null;
        }

        delivery.Status = WmsWebhookDeliveryStatuses.Processing;
        delivery.AttemptCount++;
        delivery.LeaseUntilUtc = now.Add(ProcessingLease);
        await context.SaveChangesAsync(cancellationToken);

        WebhookDeliveryResult result;
        try
        {
            var secret = secretProtector.Unprotect(subscription.SecretCiphertext);
            result = await transport.SendAsync(
                new WebhookDeliveryRequest(
                    subscription.Id,
                    subscription.EndpointUrl,
                    message.EventType,
                    message.EventId,
                    message.PayloadJson,
                    WebhookSignature.CreateHeaders(
                        secret,
                        message.PayloadJson,
                        now,
                        message.EventId,
                        delivery.Id,
                        message.EventType,
                        message.Version),
                    delivery.Id,
                    message.Version),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Webhook delivery preparation failed for subscription {SubscriptionId}, event {EventId}; failure category {FailureCategory}",
                subscription.Id,
                message.EventId,
                exception.GetType().Name);
            result = new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: false,
                Error: "webhook_delivery_preparation_failed");
        }

        delivery.LeaseUntilUtc = null;
        delivery.ResponseStatusCode = result.ResponseStatusCode;
        delivery.ResponseBody = null;
        if (result.Succeeded)
        {
            delivery.Status = WmsWebhookDeliveryStatuses.Delivered;
            delivery.DeliveredAtUtc = now;
            delivery.NextAttemptAtUtc = null;
            delivery.LastError = null;
            subscription.LastDeliveryAtUtc = now;
            subscription.UpdatedAtUtc = now;
        }
        else
        {
            delivery.LastError = Trim(result.Error ?? "Webhook delivery failed.", MaximumErrorLength);
            var retry = result.Retryable && delivery.AttemptCount < subscription.MaximumAttempts;
            delivery.Status = retry
                ? WmsWebhookDeliveryStatuses.Failed
                : WmsWebhookDeliveryStatuses.DeadLettered;
            delivery.NextAttemptAtUtc = retry
                ? GetNextAttemptAtUtc(now, delivery.AttemptCount, result.RetryAfterUtc)
                : null;
            message.LastError = delivery.LastError;
            logger.LogWarning(
                "Webhook delivery failed for subscription {SubscriptionId}, event {EventId}, attempt {Attempt}, retryable {Retryable}",
                subscription.Id,
                message.EventId,
                delivery.AttemptCount,
                retry);
        }

        await context.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task FinalizeMessageAsync(
        long messageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var message = await context.IntegrationOutbox.SingleAsync(
            item => item.Id == messageId,
            cancellationToken);
        var deliveries = await context.WebhookDeliveries
            .Where(delivery => delivery.OutboxMessageId == messageId)
            .ToListAsync(cancellationToken);
        message.LeaseUntilUtc = null;
        if (deliveries.All(delivery => delivery.Status == WmsWebhookDeliveryStatuses.Delivered))
        {
            message.Status = WmsIntegrationEventStatuses.Delivered;
            message.DeliveredAtUtc = now;
            message.NextAttemptAtUtc = null;
        }
        else if (deliveries.Any(delivery => delivery.Status == WmsWebhookDeliveryStatuses.DeadLettered))
        {
            message.Status = WmsIntegrationEventStatuses.DeadLettered;
            message.DeadLetteredAtUtc = now;
            message.NextAttemptAtUtc = null;
        }
        else
        {
            message.Status = WmsIntegrationEventStatuses.Failed;
            message.NextAttemptAtUtc = deliveries
                .Where(delivery => delivery.Status == WmsWebhookDeliveryStatuses.Failed)
                .Select(delivery => delivery.NextAttemptAtUtc)
                .OfType<DateTimeOffset>()
                .DefaultIfEmpty(now.Add(GetRetryDelay(message.AttemptCount)))
                .Min();
        }

        if (message.Status == WmsIntegrationEventStatuses.Delivered)
        {
            await UpdateNotificationRecipientsAsync(
                message,
                NotificationRecipientDeliveryState.TransportAccepted,
                cancellationToken);
        }
        else if (message.Status == WmsIntegrationEventStatuses.DeadLettered)
        {
            await UpdateNotificationRecipientsAsync(
                message,
                NotificationRecipientDeliveryState.DeadLettered,
                cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateNotificationRecipientsAsync(
        WmsIntegrationOutboxEntity message,
        NotificationRecipientDeliveryState state,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                message.EventType,
                WmsIntegrationEventTypes.NotificationPublished,
                StringComparison.Ordinal) ||
            !long.TryParse(message.AggregateKey, out var notificationId))
        {
            return;
        }

        var recipients = await context.NotificationRecipients
            .Where(recipient =>
                recipient.NotificationId == notificationId &&
                recipient.Channel == Wms.Domain.Enums.NotificationChannel.Webhook &&
                recipient.DeliveryStatus == Wms.Domain.Enums.NotificationDeliveryStatus.Queued)
            .ToListAsync(cancellationToken);
        foreach (var recipient in recipients)
        {
            recipient.DeliveryStatus = state switch
            {
                NotificationRecipientDeliveryState.TransportAccepted =>
                    Wms.Domain.Enums.NotificationDeliveryStatus.TransportAccepted,
                NotificationRecipientDeliveryState.DeadLettered =>
                    Wms.Domain.Enums.NotificationDeliveryStatus.DeadLettered,
                _ => Wms.Domain.Enums.NotificationDeliveryStatus.Disabled
            };
            recipient.LastError = state switch
            {
                NotificationRecipientDeliveryState.TransportAccepted => null,
                NotificationRecipientDeliveryState.DeadLettered => "webhook_dead_lettered",
                _ => "webhook_no_scoped_subscription"
            };
            recipient.NextAttemptAtUtc = null;
        }
    }

    private static bool Matches(
        WmsWebhookSubscriptionEntity subscription,
        WmsIntegrationOutboxEntity message)
    {
        var eventTypes = WebhookSubscriptionService.ReadEventTypes(subscription);
        var warehouseIds = WebhookSubscriptionService.ReadWarehouseIds(subscription);
        if (string.Equals(
                message.EventType,
                WmsIntegrationEventTypes.NotificationPublished,
                StringComparison.Ordinal))
        {
            return message.WarehouseId.HasValue &&
                warehouseIds.Count > 0 &&
                warehouseIds.Contains(message.WarehouseId.Value) &&
                (eventTypes.Count == 0 || eventTypes.Contains(message.EventType, StringComparer.Ordinal));
        }

        return (eventTypes.Count == 0 || eventTypes.Contains(message.EventType, StringComparer.Ordinal)) &&
            (warehouseIds.Count == 0 ||
             message.WarehouseId.HasValue && warehouseIds.Contains(message.WarehouseId.Value));
    }

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

    private static DateTimeOffset GetNextAttemptAtUtc(
        DateTimeOffset nowUtc,
        int attempt,
        DateTimeOffset? requestedRetryAtUtc)
    {
        var exponential = nowUtc.Add(GetRetryDelay(attempt));
        if (!requestedRetryAtUtc.HasValue || requestedRetryAtUtc.Value <= nowUtc)
        {
            return exponential;
        }

        var cappedRetryAfter = requestedRetryAtUtc.Value > nowUtc.Add(MaximumRetryDelay)
            ? nowUtc.Add(MaximumRetryDelay)
            : requestedRetryAtUtc.Value;
        return cappedRetryAfter > exponential ? cappedRetryAfter : exponential;
    }

    private bool UsesPortableDateComparison() =>
        context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) != true;

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed class DispatchTotals
    {
        public int EventsClaimed { get; set; }
        public int DeliveriesAttempted { get; set; }
        public int DeliveriesSucceeded { get; set; }
        public int DeliveriesFailed { get; set; }
        public int EventsDeadLettered { get; set; }
    }

    private enum NotificationRecipientDeliveryState
    {
        Disabled,
        TransportAccepted,
        DeadLettered
    }
}
