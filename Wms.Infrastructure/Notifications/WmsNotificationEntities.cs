using Wms.Domain.Enums;

namespace Wms.Infrastructure.Notifications;

public sealed class WmsNotificationEntity
{
    public long Id { get; set; }

    public string DeduplicationKey { get; set; } = string.Empty;

    public string? CooldownKey { get; set; }

    public string Kind { get; set; } = string.Empty;

    public NotificationSeverity Severity { get; set; }

    public string TitleEn { get; set; } = string.Empty;

    public string TitleAr { get; set; } = string.Empty;

    public string MessageEn { get; set; } = string.Empty;

    public string MessageAr { get; set; } = string.Empty;

    public string? SourceType { get; set; }

    public string? SourceId { get; set; }

    public string? SourceReference { get; set; }

    public string? RequiredPermission { get; set; }

    public string? DeepLink { get; set; }

    public int? WarehouseId { get; set; }

    public bool Mandatory { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public ICollection<WmsNotificationRecipientEntity> Recipients { get; set; } =
        new List<WmsNotificationRecipientEntity>();
}

public sealed class WmsNotificationRecipientEntity
{
    public long Id { get; set; }

    public long NotificationId { get; set; }

    public string RecipientUserId { get; set; } = string.Empty;

    public string? RecipientEmail { get; set; }

    public NotificationChannel Channel { get; set; }

    public NotificationDeliveryStatus DeliveryStatus { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? ReadAtUtc { get; set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public WmsNotificationEntity Notification { get; set; } = null!;
}

public sealed class WmsNotificationPreferenceEntity
{
    public long Id { get; set; }

    public NotificationPreferenceScope Scope { get; set; }

    public string? UserId { get; set; }

    public string? RoleName { get; set; }

    public int? WarehouseId { get; set; }

    public string? Kind { get; set; }

    public NotificationChannel Channel { get; set; }

    public bool IsEnabled { get; set; }

    public int? QuietStartMinute { get; set; }

    public int? QuietEndMinute { get; set; }

    public string TimeZone { get; set; } = "UTC";

    public int? DigestMinutes { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
