namespace Wms.Infrastructure.Identity;

public sealed class WmsAuthenticationEvent
{
    public long Id { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public bool Succeeded { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? RemoteIpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? Details { get; set; }
}

public static class WmsAuthenticationEventTypes
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginRejected = "LoginRejected";
    public const string AccountLockedOut = "AccountLockedOut";
    public const string AccountDisabled = "AccountDisabled";
    public const string Logout = "Logout";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordResetSucceeded = "PasswordResetSucceeded";
    public const string PasswordResetFailed = "PasswordResetFailed";
    public const string PasswordChanged = "PasswordChanged";
    public const string AccountCreated = "AccountCreated";
    public const string AccountDisabledByAdmin = "AccountDisabledByAdmin";
    public const string AccountEnabledByAdmin = "AccountEnabledByAdmin";
    public const string BootstrapAdminCreated = "BootstrapAdminCreated";
}
