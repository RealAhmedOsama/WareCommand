using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Notifications;

public sealed record NotificationAudience(
    IReadOnlyCollection<string>? UserIds = null,
    IReadOnlyCollection<string>? Roles = null,
    int? WarehouseId = null);

public sealed record NotificationPublishInput(
    string Kind,
    NotificationSeverity Severity,
    string TitleEn,
    string TitleAr,
    string MessageEn,
    string MessageAr,
    NotificationAudience Audience,
    IReadOnlyCollection<NotificationChannel>? Channels = null,
    bool Mandatory = false,
    string? RequiredPermission = null,
    string? SourceType = null,
    string? SourceId = null,
    string? SourceReference = null,
    string? DeepLink = null,
    string? DeduplicationKey = null,
    string? CooldownKey = null,
    TimeSpan? Cooldown = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? CorrelationId = null);

public sealed record NotificationPublishResult(
    long NotificationId,
    int RecipientCount,
    bool WasDeduplicated,
    bool WasSuppressed);

public sealed record NotificationQuery(
    string? Kind = null,
    NotificationSeverity? Severity = null,
    int? WarehouseId = null,
    bool UnreadOnly = false,
    bool IncludeExpired = false,
    int Page = 1,
    int PageSize = 50);

public sealed record NotificationDto(
    long RecipientId,
    long NotificationId,
    string Kind,
    NotificationSeverity Severity,
    string Title,
    string Message,
    string? SourceType,
    string? SourceId,
    string? SourceReference,
    string? DeepLink,
    int? WarehouseId,
    bool Mandatory,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    NotificationChannel Channel,
    NotificationDeliveryStatus DeliveryStatus,
    string CorrelationId);

public sealed record NotificationPageDto(
    IReadOnlyList<NotificationDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record NotificationPreferenceInput(
    NotificationPreferenceScope Scope,
    string? RoleName,
    int? WarehouseId,
    string? Kind,
    NotificationChannel Channel,
    bool IsEnabled,
    int? QuietStartMinute = null,
    int? QuietEndMinute = null,
    string? TimeZone = null,
    int? DigestMinutes = null);

public sealed record NotificationPreferenceDto(
    long Id,
    NotificationPreferenceScope Scope,
    string? RoleName,
    int? WarehouseId,
    string? Kind,
    NotificationChannel Channel,
    bool IsEnabled,
    int? QuietStartMinute,
    int? QuietEndMinute,
    string TimeZone,
    int? DigestMinutes,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationRecipientProfile(
    string UserId,
    string UserName,
    string? Email,
    string Locale,
    string TimeZone,
    IReadOnlySet<string> Roles);

public interface INotificationRecipientDirectory
{
    Task<IReadOnlyList<NotificationRecipientProfile>> ResolveAsync(
        NotificationAudience audience,
        string? requiredPermission,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationDeliveryMessage(
    long NotificationId,
    long RecipientId,
    string RecipientUserId,
    string? RecipientEmail,
    string Kind,
    NotificationSeverity Severity,
    string TitleEn,
    string TitleAr,
    string MessageEn,
    string MessageAr,
    string? DeepLink,
    int? WarehouseId,
    bool Mandatory,
    string CorrelationId,
    string Locale = "en-US");

public sealed record NotificationDeliveryResult(
    bool Succeeded,
    bool Retryable,
    string? Error = null,
    NotificationDeliveryStatus? SuccessStatus = null,
    bool Disabled = false);

public sealed record EmailMessage(
    string Recipient,
    string Subject,
    string Body,
    string StableMessageId);

public sealed record EmailTransportCapability(
    bool Enabled,
    bool Configured,
    bool Verified);

public sealed record EmailTransportResult(
    bool Accepted,
    bool Retryable,
    string ErrorCode);

public interface IEmailTransport
{
    EmailTransportCapability Capability { get; }

    Task<EmailTransportResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}

public sealed record NotificationChannelHealth(
    string Status,
    bool Enabled,
    bool Configured,
    bool Verified,
    int Queued,
    int Failed,
    int Disabled,
    int DeadLettered,
    int TransportAccepted);

public sealed record NotificationChannelHealthDto(
    NotificationChannelHealth Email,
    NotificationChannelHealth Webhook);

public interface INotificationChannelHealthService
{
    Task<NotificationChannelHealthDto> GetAsync(CancellationToken cancellationToken = default);
}

public interface INotificationChannelAdapter
{
    NotificationChannel Channel { get; }

    Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryMessage message,
        CancellationToken cancellationToken = default);
}

public interface INotificationDeliveryService
{
    Task<int> DispatchPendingAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    Task<Result<NotificationPublishResult>> PublishAsync(
        NotificationPublishInput input,
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPageDto>> ListAsync(
        NotificationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<int>> GetUnreadCountAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<Result> MarkReadAsync(
        long recipientId,
        CancellationToken cancellationToken = default);

    Task<Result> AcknowledgeAsync(
        long recipientId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> ListPreferencesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<NotificationPreferenceDto>> SavePreferenceAsync(
        NotificationPreferenceInput input,
        CancellationToken cancellationToken = default);
}
