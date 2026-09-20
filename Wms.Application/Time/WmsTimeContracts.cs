namespace Wms.Application.Time;

/// <summary>
/// The canonical timezone contract for business settings is an IANA identifier.
/// Windows identifiers are accepted only as legacy input and normalized when a
/// corresponding IANA identifier is available.
/// </summary>
public static class WmsTimeZoneCatalog
{
    public const string Utc = "UTC";

    public static bool TryResolve(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = null!;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        var value = timeZoneId.Trim();
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            if (!TimeZoneInfo.TryConvertWindowsIdToIanaId(value, out var ianaId) ||
                string.IsNullOrWhiteSpace(ianaId))
            {
                return false;
            }

            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                return true;
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
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static bool TryNormalize(string? timeZoneId, out string normalizedTimeZoneId)
    {
        normalizedTimeZoneId = string.Empty;
        if (!TryResolve(timeZoneId, out var timeZone))
        {
            return false;
        }

        var value = timeZoneId!.Trim();
        if (value.Equals(Utc, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase) ||
            timeZone.Equals(TimeZoneInfo.Utc))
        {
            normalizedTimeZoneId = Utc;
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(value, out var ianaId) &&
            !string.IsNullOrWhiteSpace(ianaId))
        {
            normalizedTimeZoneId = ianaId;
            return true;
        }

        normalizedTimeZoneId = value;
        return true;
    }

    public static TimeZoneInfo Resolve(string timeZoneId) =>
        TryResolve(timeZoneId, out var timeZone)
            ? timeZone
            : throw new TimeZoneNotFoundException(
                $"The configured time zone '{timeZoneId}' is not installed or is invalid.");

    public static string Normalize(string timeZoneId) =>
        TryNormalize(timeZoneId, out var normalized)
            ? normalized
            : throw new TimeZoneNotFoundException(
                $"The configured time zone '{timeZoneId}' is not installed or is invalid.");
}

public sealed record WmsUtcDateRange(
    DateOnly FromBusinessDateInclusive,
    DateOnly ToBusinessDateExclusive,
    DateTime FromUtc,
    DateTime ToUtcExclusive);

public static class WmsBusinessTime
{
    public static DateTimeOffset ToLocal(DateTimeOffset utcInstant, string timeZoneId) =>
        TimeZoneInfo.ConvertTime(EnsureUtc(utcInstant), WmsTimeZoneCatalog.Resolve(timeZoneId));

    public static DateTime ToLocalDateTime(DateTime utcInstant, string timeZoneId) =>
        ToLocal(new DateTimeOffset(EnsureUtc(utcInstant)), timeZoneId).DateTime;

    public static DateOnly GetBusinessDate(DateTimeOffset utcInstant, string timeZoneId) =>
        DateOnly.FromDateTime(ToLocal(utcInstant, timeZoneId).DateTime);

    public static WmsUtcDateRange GetInclusiveDateRange(
        DateOnly fromBusinessDateInclusive,
        DateOnly toBusinessDateInclusive,
        string timeZoneId)
    {
        if (toBusinessDateInclusive < fromBusinessDateInclusive)
        {
            throw new ArgumentException(
                "The report end business date cannot be before the start business date.",
                nameof(toBusinessDateInclusive));
        }

        return GetExclusiveDateRange(
            fromBusinessDateInclusive,
            toBusinessDateInclusive.AddDays(1),
            timeZoneId);
    }

    public static WmsUtcDateRange GetExclusiveDateRange(
        DateOnly fromBusinessDateInclusive,
        DateOnly toBusinessDateExclusive,
        string timeZoneId)
    {
        if (toBusinessDateExclusive <= fromBusinessDateInclusive)
        {
            throw new ArgumentException(
                "The exclusive end business date must be after the start business date.",
                nameof(toBusinessDateExclusive));
        }

        var timeZone = WmsTimeZoneCatalog.Resolve(timeZoneId);
        var fromUtc = ConvertBoundaryToUtc(fromBusinessDateInclusive, timeZone);
        var toUtc = ConvertBoundaryToUtc(toBusinessDateExclusive, timeZone);
        return new WmsUtcDateRange(
            fromBusinessDateInclusive,
            toBusinessDateExclusive,
            fromUtc,
            toUtc);
    }

    private static DateTime ConvertBoundaryToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var localBoundary = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A small number of time zones transition at midnight. Move a skipped
        // boundary to the first valid local instant so the date range remains
        // deterministic instead of throwing during DST changes.
        while (timeZone.IsInvalidTime(localBoundary))
        {
            localBoundary = localBoundary.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(localBoundary))
        {
            // Pick the earliest UTC instant for an ambiguous business boundary.
            var offset = timeZone.GetAmbiguousTimeOffsets(localBoundary).Max();
            return new DateTimeOffset(localBoundary, offset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localBoundary, timeZone);
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value) => value.ToUniversalTime();

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
