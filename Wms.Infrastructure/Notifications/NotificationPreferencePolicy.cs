using Wms.Application.Notifications;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Notifications;

internal static class NotificationPreferencePolicy
{
    public static PreferenceDecision Resolve(
        IReadOnlyCollection<WmsNotificationPreferenceEntity> preferences,
        NotificationRecipientProfile recipient,
        int? warehouseId,
        string kind,
        NotificationChannel channel,
        DateTimeOffset nowUtc,
        bool mandatory,
        DateTimeOffset? scheduleOriginUtc = null)
    {
        var matches = preferences
            .Where(preference =>
                preference.Channel == channel &&
                (preference.Kind is null || string.Equals(preference.Kind, kind, StringComparison.OrdinalIgnoreCase)) &&
                ((preference.Scope == NotificationPreferenceScope.User &&
                  preference.UserId == recipient.UserId) ||
                 (preference.Scope == NotificationPreferenceScope.Role &&
                  preference.RoleName is not null &&
                  recipient.Roles.Contains(preference.RoleName)) ||
                 (preference.Scope == NotificationPreferenceScope.Warehouse &&
                  preference.WarehouseId == warehouseId)))
            .OrderByDescending(preference => PreferenceSpecificity(preference))
            .ThenByDescending(preference => preference.UpdatedAtUtc)
            .FirstOrDefault();
        if (mandatory ||
            matches is null ||
            (matches.IsEnabled && (channel == NotificationChannel.InApp || !IsQuiet(matches, nowUtc))))
        {
            var nextAttempt = matches?.DigestMinutes is > 0 && channel != NotificationChannel.InApp
                ? (scheduleOriginUtc ?? nowUtc).AddMinutes(matches.DigestMinutes.Value)
                : nowUtc;
            return new PreferenceDecision(true, nextAttempt);
        }

        return new PreferenceDecision(false, null);
    }

    private static int PreferenceSpecificity(WmsNotificationPreferenceEntity preference) =>
        (preference.Scope, preference.Kind is not null) switch
        {
            (NotificationPreferenceScope.User, true) => 60,
            (NotificationPreferenceScope.User, false) => 50,
            (NotificationPreferenceScope.Role, true) => 40,
            (NotificationPreferenceScope.Role, false) => 30,
            (NotificationPreferenceScope.Warehouse, true) => 20,
            _ => 10
        };

    private static bool IsQuiet(WmsNotificationPreferenceEntity preference, DateTimeOffset nowUtc)
    {
        if (!preference.QuietStartMinute.HasValue || !preference.QuietEndMinute.HasValue)
        {
            return false;
        }

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(preference.TimeZone);
            var local = TimeZoneInfo.ConvertTime(nowUtc, timeZone).TimeOfDay;
            var minute = (int)local.TotalMinutes;
            return preference.QuietStartMinute <= preference.QuietEndMinute
                ? minute >= preference.QuietStartMinute && minute < preference.QuietEndMinute
                : minute >= preference.QuietStartMinute || minute < preference.QuietEndMinute;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    internal sealed record PreferenceDecision(bool IsEnabled, DateTimeOffset? NextAttemptAtUtc);
}
