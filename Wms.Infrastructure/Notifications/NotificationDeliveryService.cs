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
    INotificationRecipientDirectory recipientDirectory,
    IClock clock,
    ILogger<NotificationDeliveryService> logger) : INotificationDeliveryService
{
    private const int MaximumAttempts = 10;
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

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
            recipient.LeaseUntilUtc = nowUtc.Add(ProcessingLease);
            await context.SaveChangesAsync(cancellationToken);

            var profile = (await recipientDirectory.ResolveAsync(
                    new NotificationAudience(
                        UserIds: [recipient.RecipientUserId],
                        WarehouseId: recipient.Notification.WarehouseId),
                    recipient.Notification.RequiredPermission,
                    cancellationToken))
                .SingleOrDefault(candidate => string.Equals(
                    candidate.UserId,
                    recipient.RecipientUserId,
                    StringComparison.Ordinal));
            if (profile is null)
            {
                SetTerminal(recipient, NotificationDeliveryStatus.Suppressed, "recipient_not_eligible");
                await context.SaveChangesAsync(cancellationToken);
                processed++;
                continue;
            }

            recipient.RecipientEmail = profile.Email;

            if (recipient.Channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(profile.Email))
            {
                SetTerminal(recipient, NotificationDeliveryStatus.Disabled, "recipient_email_unavailable");
                await context.SaveChangesAsync(cancellationToken);
                processed++;
                continue;
            }

            var userIds = new[] { profile.UserId };
            var roleNames = profile.Roles.ToArray();
            var preferences = await context.NotificationPreferences
                .AsNoTracking()
                .Where(preference =>
                    (preference.UserId != null && userIds.Contains(preference.UserId)) ||
                    (preference.RoleName != null && roleNames.Contains(preference.RoleName)) ||
                    (recipient.Notification.WarehouseId.HasValue &&
                     preference.WarehouseId == recipient.Notification.WarehouseId.Value))
                .ToListAsync(cancellationToken);
            var preference = NotificationPreferencePolicy.Resolve(
                preferences,
                profile,
                recipient.Notification.WarehouseId,
                recipient.Notification.Kind,
                recipient.Channel,
                nowUtc,
                recipient.Notification.Mandatory,
                recipient.Notification.CreatedAtUtc);
            if (!preference.IsEnabled)
            {
                SetTerminal(recipient, NotificationDeliveryStatus.Suppressed, "recipient_preference_disabled");
                await context.SaveChangesAsync(cancellationToken);
                processed++;
                continue;
            }

            if (preference.NextAttemptAtUtc is { } scheduledAtUtc && scheduledAtUtc > nowUtc)
            {
                recipient.NextAttemptAtUtc = scheduledAtUtc;
                recipient.LeaseUntilUtc = null;
                await context.SaveChangesAsync(cancellationToken);
                processed++;
                continue;
            }

            if (recipient.AttemptCount >= MaximumAttempts)
            {
                SetTerminal(recipient, NotificationDeliveryStatus.DeadLettered, "delivery_attempt_limit_reached");
                await context.SaveChangesAsync(cancellationToken);
                processed++;
                continue;
            }

            var result = adapterMap.TryGetValue(recipient.Channel, out var adapter)
                ? await DeliverAsync(adapter, recipient, profile, cancellationToken)
                : new NotificationDeliveryResult(
                    Succeeded: false,
                    Retryable: false,
                    Error: "notification_adapter_unavailable");

            recipient.AttemptCount++;
            recipient.LastAttemptAtUtc = nowUtc;
            recipient.LeaseUntilUtc = null;
            if (result.Succeeded)
            {
                recipient.DeliveryStatus = result.SuccessStatus is
                    NotificationDeliveryStatus.Queued or NotificationDeliveryStatus.TransportAccepted
                    ? result.SuccessStatus.Value
                    : NotificationDeliveryStatus.TransportAccepted;
                recipient.NextAttemptAtUtc = null;
                recipient.LastError = null;
            }
            else if (result.Disabled)
            {
                SetTerminal(recipient, NotificationDeliveryStatus.Disabled, result.Error);
            }
            else
            {
                var shouldRetry = result.Retryable && recipient.AttemptCount < MaximumAttempts;
                recipient.DeliveryStatus = shouldRetry
                    ? NotificationDeliveryStatus.Failed
                    : NotificationDeliveryStatus.DeadLettered;
                recipient.LastError = NormalizeError(result.Error);
                recipient.NextAttemptAtUtc = shouldRetry
                    ? nowUtc.Add(GetRetryDelay(recipient.AttemptCount))
                    : null;
            }

            await context.SaveChangesAsync(cancellationToken);
            processed++;
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Notification delivery failed on channel {Channel}; attempt {Attempt}; retryable {Retryable}",
                    recipient.Channel,
                    recipient.AttemptCount,
                    recipient.DeliveryStatus == NotificationDeliveryStatus.Failed);
            }
        }

        return processed;
    }

    private static async Task<NotificationDeliveryResult> DeliverAsync(
        INotificationChannelAdapter adapter,
        WmsNotificationRecipientEntity recipient,
        NotificationRecipientProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            return await adapter.DeliverAsync(
                new NotificationDeliveryMessage(
                    recipient.NotificationId,
                    recipient.Id,
                    recipient.RecipientUserId,
                    profile.Email,
                    recipient.Notification.Kind,
                    recipient.Notification.Severity,
                    recipient.Notification.TitleEn,
                    recipient.Notification.TitleAr,
                    recipient.Notification.MessageEn,
                    recipient.Notification.MessageAr,
                    recipient.Notification.DeepLink,
                    recipient.Notification.WarehouseId,
                    recipient.Notification.Mandatory,
                    recipient.Notification.CorrelationId,
                    profile.Locale),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: true,
                Error: "notification_adapter_failed");
        }
    }

    private static TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempt - 1))));

    private static void SetTerminal(
        WmsNotificationRecipientEntity recipient,
        NotificationDeliveryStatus status,
        string? error)
    {
        recipient.DeliveryStatus = status;
        recipient.NextAttemptAtUtc = null;
        recipient.LeaseUntilUtc = null;
        recipient.LastError = NormalizeError(error);
    }

    private static string NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error) || error.Length > 80 ||
            error.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            return "delivery_failed";
        }

        return error.ToLowerInvariant();
    }

    private bool UsesPortableDateComparison() =>
        context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) != true;
}
