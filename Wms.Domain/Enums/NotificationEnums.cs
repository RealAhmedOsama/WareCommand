namespace Wms.Domain.Enums;

public enum NotificationSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Webhook = 3
}

public enum NotificationDeliveryStatus
{
    Pending = 1,
    Delivered = 2,
    Failed = 3,
    Suppressed = 4
}

public enum NotificationPreferenceScope
{
    User = 1,
    Role = 2,
    Warehouse = 3
}
