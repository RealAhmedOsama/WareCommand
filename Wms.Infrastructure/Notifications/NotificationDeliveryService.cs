using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Context;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Notifications;

public sealed class NotificationDeliveryService(
    WmsDbContext context,
    IEnumerable<INotificationChannelAdapter> adapters,
    IClock clock,
    ILogger<NotificationDeliveryService> logger) : INotificationDeliveryService
{
    private const int MaximumAttempts = 10;
    private const int MaximumErrorLength = 2_000;

    public async Task<int> DispatchPendingAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var nowUtc = clock.UtcNow;
        var candidates = context.NotificationRecipients
            .Include(recipient => recipient.Notification)
            .Where(recipient =>
                recipient.Channel != NotificationChannel.InApp &&
                (recipient.DeliveryStatus == NotificationDeliveryStatus.Pending ||
                 recipient.DeliveryStatus == NotificationDeliveryStatus.Failed));
        IReadOnlyList<WmsNotificationRecipientEntity> rows;
        if (UsesPortableDateComparison())
        {
            rows = (await candidates.ToListAsync(cancellationToken))
                .Where(recipient =>
                    (recipient.NextAttemptAtUtc == null || recipient.NextAttemptAtUtc <= nowUtc) &&
                    (recipient.LeaseUntilUtc == null || recipient.LeaseUntilUtc <= nowUtc) &&
                    (recipient.Notification.ExpiresAtUtc == null || recipient.Notification.ExpiresAtUtc > nowUtc))
                .OrderBy(recipient => recipient.NextAttemptAtUtc)
                .ThenBy(recipient => recipient.Id)
                .Take(maximumCount)
                .ToList();
        }
        else
        {
            rows = await candidates
                .Where(recipient =>
                    (recipient.NextAttemptAtUtc == null || recipient.NextAttemptAtUtc <= nowUtc) &&
                    (recipient.LeaseUntilUtc == null || recipient.LeaseUntilUtc <= nowUtc) &&
                    (recipient.Notification.ExpiresAtUtc == null || recipient.Notification.ExpiresAtUtc > nowUtc))
                .OrderBy(recipient => recipient.NextAttemptAtUtc)
                .ThenBy(recipient => recipient.Id)
                .Take(maximumCount)
                .ToListAsync(cancellationToken);
        }

        var adapterMap = adapters
            .GroupBy(adapter => adapter.Channel)
            .ToDictionary(group => group.Key, group => group.Last());
        var processed = 0;
        foreach (var recipient in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recipient.AttemptCount++;
            recipient.LastAttemptAtUtc = nowUtc;
            recipient.LeaseUntilUtc = nowUtc.AddMinutes(5);
            await context.SaveChangesAsync(cancellationToken);

            var result = adapterMap.TryGetValue(recipient.Channel, out var adapter)
                ? await DeliverAsync(adapter, recipient, cancellationToken)
                : new NotificationDeliveryResult(
                    Succeeded: false,
                    Retryable: false,
                    Error: $"No adapter is registered for notification channel '{recipient.Channel}'.");

            recipient.LeaseUntilUtc = null;
            if (result.Succeeded)
            {
                recipient.DeliveryStatus = NotificationDeliveryStatus.Delivered;
                recipient.NextAttemptAtUtc = null;
                recipient.LastError = null;
            }
            else
            {
                recipient.DeliveryStatus = NotificationDeliveryStatus.Failed;
                recipient.LastError = TrimError(result.Error ?? "Notification delivery failed.");
                recipient.NextAttemptAtUtc = result.Retryable && recipient.AttemptCount < MaximumAttempts
                    ? nowUtc.Add(GetRetryDelay(recipient.AttemptCount))
                    : null;
            }

            await context.SaveChangesAsync(cancellationToken);
            processed++;
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Notification delivery failed for recipient {RecipientId} on channel {Channel}; attempt {Attempt}; retryable {Retryable}",
                    recipient.Id,
                    recipient.Channel,
                    recipient.AttemptCount,
                    result.Retryable);
            }
        }

        return processed;
    }

    private static async Task<NotificationDeliveryResult> DeliverAsync(
        INotificationChannelAdapter adapter,
        WmsNotificationRecipientEntity recipient,
        CancellationToken cancellationToken)
    {
        try
        {
            return await adapter.DeliverAsync(
                new NotificationDeliveryMessage(
                    recipient.NotificationId,
                    recipient.Id,
                    recipient.RecipientUserId,
                    recipient.RecipientEmail,
                    recipient.Notification.Kind,
                    recipient.Notification.Severity,
                    recipient.Notification.TitleEn,
                    recipient.Notification.TitleAr,
                    recipient.Notification.MessageEn,
                    recipient.Notification.MessageAr,
                    recipient.Notification.DeepLink,
                    recipient.Notification.WarehouseId,
                    recipient.Notification.Mandatory,
                    recipient.Notification.CorrelationId),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: true,
                Error: exception.Message);
        }
    }

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

    private static string TrimError(string error) =>
        error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];

    private bool UsesPortableDateComparison() =>
        context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) != true;
}
