using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Wms.Application.Forecasting;

public enum ForecastSourceEventKind
{
    Shipment,
    CustomerReturn
}

public sealed record ForecastSourceEvent(
    long SourceId,
    DateTime OccurredAtUtc,
    decimal Quantity,
    string UnitOfMeasure,
    ForecastSourceEventKind Kind);

public sealed record ForecastUnitConversion(
    string FromUnit,
    string ToUnit,
    decimal Factor,
    int Version);

public sealed record ForecastDemandSeries(
    IReadOnlyList<ForecastDemandPoint> History,
    bool HasShipmentHistory,
    bool IsComplete,
    IReadOnlyList<string> DataQualityFlags,
    string SourceFingerprint);

/// <summary>
/// Converts persisted shipment and customer-return facts into a causal series.
/// Returns reduce demand on the warehouse-local day they are physically
/// received. Unshipped/cancelled orders never become demand, and no period
/// after the last complete business period is synthesized.
/// </summary>
public static class ForecastDemandSeriesBuilder
{
    public const int DailyHistoryPeriods = 365;
    public const int WeeklyHistoryPeriods = 520;
    public const int MonthlyHistoryPeriods = 120;

    public static ForecastDemandSeries Build(
        IEnumerable<ForecastSourceEvent> sourceEvents,
        IEnumerable<ForecastUnitConversion> unitConversions,
        string targetUnitOfMeasure,
        string warehouseTimeZoneId,
        ForecastGranularity granularity,
        DateTime sourceCutoffUtc,
        IReadOnlySet<DateOnly>? stockoutPeriodStarts = null)
    {
        ArgumentNullException.ThrowIfNull(sourceEvents);
        ArgumentNullException.ThrowIfNull(unitConversions);
        if (string.IsNullOrWhiteSpace(targetUnitOfMeasure))
        {
            throw new ArgumentException("A target base unit is required.", nameof(targetUnitOfMeasure));
        }

        if (string.IsNullOrWhiteSpace(warehouseTimeZoneId))
        {
            return Empty(false, ["warehouse-time-zone-missing"]);
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(warehouseTimeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return Empty(false, ["warehouse-time-zone-invalid"]);
        }
        catch (InvalidTimeZoneException)
        {
            return Empty(false, ["warehouse-time-zone-invalid"]);
        }

        var sourceCutoff = DateTime.SpecifyKind(sourceCutoffUtc, DateTimeKind.Utc);
        var normalizedTargetUnit = NormalizeUnit(targetUnitOfMeasure);
        var conversions = unitConversions
            .Where(conversion => conversion.Factor > 0m && conversion.Version > 0)
            .GroupBy(conversion => (From: NormalizeUnit(conversion.FromUnit), To: NormalizeUnit(conversion.ToUnit)))
            .Select(group => group.OrderByDescending(conversion => conversion.Version).First())
            .ToArray();
        var usableEvents = sourceEvents
            .Where(sourceEvent => sourceEvent.SourceId > 0 &&
                sourceEvent.Quantity > 0m &&
                sourceEvent.OccurredAtUtc <= sourceCutoff)
            .OrderBy(sourceEvent => sourceEvent.OccurredAtUtc)
            .ThenBy(sourceEvent => sourceEvent.SourceId)
            .ToArray();
        var shipmentEvents = usableEvents
            .Where(sourceEvent => sourceEvent.Kind == ForecastSourceEventKind.Shipment)
            .ToArray();
        if (shipmentEvents.Length == 0)
        {
            return Empty(false, ["no-completed-shipment-history"]);
        }

        var flags = new SortedSet<string>(StringComparer.Ordinal)
        {
            "unshipped-and-cancelled-orders-excluded",
            "customer-returns-applied-on-warehouse-local-receipt-period",
            "incomplete-current-period-excluded"
        };
        var normalizedEvents = new List<(DateOnly PeriodStart, decimal Quantity, ForecastSourceEvent Source)>(usableEvents.Length);
        var incomplete = false;
        foreach (var sourceEvent in usableEvents)
        {
            var converted = ConvertToTarget(
                sourceEvent.Quantity,
                sourceEvent.UnitOfMeasure,
                normalizedTargetUnit,
                conversions);
            if (converted is null)
            {
                incomplete = true;
                flags.Add("unconvertible-source-unit");
                continue;
            }

            var occurredAtUtc = DateTime.SpecifyKind(sourceEvent.OccurredAtUtc, DateTimeKind.Utc);
            var localTimestamp = TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, timeZone);
            var periodStart = PeriodStart(DateOnly.FromDateTime(localTimestamp), granularity);
            var signedQuantity = sourceEvent.Kind == ForecastSourceEventKind.CustomerReturn
                ? -converted.Value
                : converted.Value;
            normalizedEvents.Add((periodStart, signedQuantity, sourceEvent));
        }

        var localCutoff = TimeZoneInfo.ConvertTimeFromUtc(sourceCutoff, timeZone);
        var lastCompletePeriod = LastCompletePeriodStart(DateOnly.FromDateTime(localCutoff), granularity);
        var firstShipmentPeriod = normalizedEvents
            .Where(entry => entry.Source.Kind == ForecastSourceEventKind.Shipment)
            .Select(entry => entry.PeriodStart)
            .DefaultIfEmpty(lastCompletePeriod)
            .Min();
        var maximumPeriods = MaximumPeriods(granularity);
        var earliestIncludedPeriod = AddPeriods(lastCompletePeriod, granularity, -(maximumPeriods - 1));
        if (firstShipmentPeriod < earliestIncludedPeriod)
        {
            firstShipmentPeriod = earliestIncludedPeriod;
            flags.Add("history-truncated-to-configured-window");
        }

        var endPeriod = lastCompletePeriod;
        if (firstShipmentPeriod > endPeriod)
        {
            return new ForecastDemandSeries([], true, !incomplete, flags.ToArray(), Fingerprint(
                usableEvents, conversions, normalizedTargetUnit, sourceCutoff, granularity));
        }

        var totals = normalizedEvents
            .Where(entry => entry.PeriodStart >= firstShipmentPeriod && entry.PeriodStart <= endPeriod)
            .GroupBy(entry => entry.PeriodStart)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Quantity));
        var history = new List<ForecastDemandPoint>();
        var missingPeriods = false;
        for (var period = firstShipmentPeriod; period <= endPeriod; period = AddPeriods(period, granularity, 1))
        {
            if (!totals.ContainsKey(period))
            {
                missingPeriods = true;
            }

            history.Add(new ForecastDemandPoint(
                period,
                totals.GetValueOrDefault(period),
                stockoutPeriodStarts?.Contains(period) == true));
        }

        if (missingPeriods)
        {
            flags.Add("missing-periods-filled-with-zero");
        }

        if (history.Any(point => point.WasStockout))
        {
            flags.Add("available-stockout-period-censored");
        }

        return new ForecastDemandSeries(
            history,
            true,
            !incomplete,
            flags.ToArray(),
            Fingerprint(usableEvents, conversions, normalizedTargetUnit, sourceCutoff, granularity));
    }

    public static decimal? ConvertToTarget(
        decimal quantity,
        string sourceUnit,
        string targetUnit,
        IReadOnlyList<ForecastUnitConversion> conversions)
    {
        var source = NormalizeUnit(sourceUnit);
        var target = NormalizeUnit(targetUnit);
        if (source == target)
        {
            return quantity;
        }

        var adjacency = new Dictionary<string, List<(string Unit, decimal Factor)>>(StringComparer.Ordinal);
        foreach (var conversion in conversions)
        {
            var from = NormalizeUnit(conversion.FromUnit);
            var to = NormalizeUnit(conversion.ToUnit);
            if (!adjacency.TryGetValue(from, out var forward))
            {
                adjacency[from] = forward = [];
            }

            forward.Add((to, conversion.Factor));
            if (!adjacency.TryGetValue(to, out var reverse))
            {
                adjacency[to] = reverse = [];
            }

            reverse.Add((from, 1m / conversion.Factor));
        }

        var pending = new Queue<(string Unit, decimal Factor)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { source };
        pending.Enqueue((source, 1m));
        while (pending.TryDequeue(out var current))
        {
            if (!adjacency.TryGetValue(current.Unit, out var edges))
            {
                continue;
            }

            foreach (var edge in edges.OrderBy(edge => edge.Unit, StringComparer.Ordinal))
            {
                if (!visited.Add(edge.Unit))
                {
                    continue;
                }

                var factor = current.Factor * edge.Factor;
                if (edge.Unit == target)
                {
                    return quantity * factor;
                }

                pending.Enqueue((edge.Unit, factor));
            }
        }

        return null;
    }

    public static DateOnly PeriodStart(DateOnly date, ForecastGranularity granularity) =>
        granularity switch
        {
            ForecastGranularity.Daily => date,
            ForecastGranularity.Weekly => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            ForecastGranularity.Monthly => new DateOnly(date.Year, date.Month, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

    public static DateOnly LatestCompletePeriodStart(
        DateTime cutoffUtc,
        string warehouseTimeZoneId,
        ForecastGranularity granularity)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(warehouseTimeZoneId);
        var localCutoff = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(cutoffUtc, DateTimeKind.Utc),
            timeZone);
        return LastCompletePeriodStart(DateOnly.FromDateTime(localCutoff), granularity);
    }

    private static DateOnly LastCompletePeriodStart(DateOnly localDate, ForecastGranularity granularity) =>
        granularity switch
        {
            ForecastGranularity.Daily => localDate.AddDays(-1),
            ForecastGranularity.Weekly => PeriodStart(localDate, ForecastGranularity.Weekly).AddDays(-7),
            ForecastGranularity.Monthly => new DateOnly(localDate.Year, localDate.Month, 1).AddMonths(-1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

    private static int MaximumPeriods(ForecastGranularity granularity) =>
        granularity switch
        {
            ForecastGranularity.Daily => DailyHistoryPeriods,
            ForecastGranularity.Weekly => WeeklyHistoryPeriods,
            ForecastGranularity.Monthly => MonthlyHistoryPeriods,
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

    private static DateOnly AddPeriods(
        DateOnly period,
        ForecastGranularity granularity,
        int count) =>
        granularity switch
        {
            ForecastGranularity.Daily => period.AddDays(count),
            ForecastGranularity.Weekly => period.AddDays(7 * count),
            ForecastGranularity.Monthly => period.AddMonths(count),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

    private static string Fingerprint(
        IReadOnlyList<ForecastSourceEvent> events,
        IReadOnlyList<ForecastUnitConversion> conversions,
        string targetUnit,
        DateTime cutoffUtc,
        ForecastGranularity granularity)
    {
        var builder = new StringBuilder()
            .Append(granularity)
            .Append('|')
            .Append(targetUnit)
            .Append('|');
        foreach (var conversion in conversions.OrderBy(value => value.FromUnit, StringComparer.Ordinal)
                     .ThenBy(value => value.ToUnit, StringComparer.Ordinal)
                     .ThenBy(value => value.Version))
        {
            builder.Append("u:")
                .Append(NormalizeUnit(conversion.FromUnit)).Append('>')
                .Append(NormalizeUnit(conversion.ToUnit)).Append(':')
                .Append(conversion.Factor.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(conversion.Version).Append('|');
        }

        foreach (var sourceEvent in events)
        {
            builder.Append("e:")
                .Append(sourceEvent.SourceId).Append(':')
                .Append(DateTime.SpecifyKind(sourceEvent.OccurredAtUtc, DateTimeKind.Utc)
                    .ToString("O", CultureInfo.InvariantCulture)).Append(':')
                .Append(sourceEvent.Kind).Append(':')
                .Append(sourceEvent.Quantity.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(NormalizeUnit(sourceEvent.UnitOfMeasure)).Append('|');
        }

        // The exact UTC cutoff is persisted separately. It is deliberately not
        // fingerprinted so a repeat request with identical source facts reuses
        // the immutable run instead of creating a timestamp-only version.
        _ = cutoffUtc;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static ForecastDemandSeries Empty(bool hasShipmentHistory, IReadOnlyList<string> flags) =>
        new([], hasShipmentHistory, false, flags, Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant());

    private static string NormalizeUnit(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}
