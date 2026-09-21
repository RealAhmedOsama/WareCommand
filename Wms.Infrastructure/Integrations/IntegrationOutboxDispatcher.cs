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
    private const int MaximumErrorLength = 2_000;
    private const int MaximumResponseLength = 8_000;

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

    private async Task<WebhookDeliveryResult> DispatchDeliveryAsync(
        WmsIntegrationOutboxEntity message,
        WmsWebhookSubscriptionEntity subscription,
        WmsWebhookDeliveryEntity delivery,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (delivery.Status == WmsWebhookDeliveryStatuses.Delivered)
        {
            return new WebhookDeliveryResult(true, false);
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
                    WebhookSignature.CreateHeaders(secret, message.PayloadJson, now)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = new WebhookDeliveryResult(Succeeded: false, Retryable: false, Error: exception.Message);
        }

        delivery.LeaseUntilUtc = null;
        delivery.ResponseStatusCode = result.ResponseStatusCode;
        delivery.ResponseBody = Trim(result.ResponseBody, MaximumResponseLength);
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
                ? now.Add(GetRetryDelay(delivery.AttemptCount))
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
            message.NextAttemptAtUtc = now.Add(GetRetryDelay(message.AttemptCount));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool Matches(
        WmsWebhookSubscriptionEntity subscription,
        WmsIntegrationOutboxEntity message)
    {
        var eventTypes = WebhookSubscriptionService.ReadEventTypes(subscription);
        var warehouseIds = WebhookSubscriptionService.ReadWarehouseIds(subscription);
        return (eventTypes.Count == 0 || eventTypes.Contains(message.EventType, StringComparer.Ordinal)) &&
            (warehouseIds.Count == 0 ||
             message.WarehouseId.HasValue && warehouseIds.Contains(message.WarehouseId.Value));
    }

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

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
}
