using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;

namespace Wms.Application.Forecasting;

public enum ForecastGranularity
{
    Daily,
    Weekly,
    Monthly
}

public enum ForecastModelKind
{
    MovingAverage,
    SimpleExponentialSmoothing
}

public enum ForecastDataStatus
{
    Ready,
    InsufficientData,
    StockoutCensored
}

public enum ForecastRiskLevel
{
    Unknown,
    Low,
    Medium,
    High
}

public sealed record ForecastDemandPoint(
    DateOnly PeriodStart,
    decimal DemandQuantity,
    bool WasStockout = false);

public sealed record ForecastRequest(
    int ItemId,
    int WarehouseId,
    ForecastGranularity Granularity,
    int HorizonPeriods,
    IReadOnlyList<ForecastDemandPoint> History,
    int TrainingWindowPeriods = 12,
    int MovingAverageWindow = 3,
    decimal OnHandQuantity = 0,
    decimal OnOrderQuantity = 0,
    decimal InTransitQuantity = 0,
    int LeadTimePeriods = 1,
    decimal? SafetyStockQuantity = null,
    string ModelVersion = "baseline.v1");

public sealed record ForecastErrorMetrics(
    decimal MeanAbsoluteError,
    decimal? MeanAbsolutePercentageError,
    decimal Bias,
    int EvaluatedPeriods);

public sealed record ForecastModelScore(
    ForecastModelKind Model,
    ForecastErrorMetrics Metrics);

public sealed record ForecastPeriod(
    DateOnly PeriodStart,
    decimal ForecastQuantity,
    decimal LowerBound,
    decimal UpperBound);

public sealed record ForecastResult(
    int ItemId,
    int WarehouseId,
    ForecastGranularity Granularity,
    ForecastDataStatus DataStatus,
    ForecastModelKind? SelectedModel,
    string ModelVersion,
    DateOnly? InputPeriodStart,
    DateOnly? InputPeriodEnd,
    int HorizonPeriods,
    IReadOnlyList<ForecastModelScore> BacktestScores,
    IReadOnlyList<ForecastPeriod> Periods,
    DateOnly? ProjectedStockoutPeriod,
    decimal? DaysOfSupply,
    ForecastRiskLevel RiskLevel,
    string InputFingerprint,
    bool CanCreateOrders,
    string? InsufficientDataReason);

public interface IForecastBaselineService
{
    Task<Result<ForecastResult>> GenerateAsync(
        ForecastRequest request,
        CancellationToken cancellationToken = default);
}

public static class ForecastBaselineEngine
{
    public const int MinimumUsablePeriods = 3;
    public const int MaximumHistoryPeriods = 1_040;
    public const int MaximumHorizonPeriods = 365;
    private const decimal SmoothingAlpha = 0.3m;

    public static Result<ForecastResult> Generate(ForecastRequest request)
    {
        var validation = Validate(request);
        if (validation.IsFailure)
        {
            return validation.ToFailure<ForecastResult>();
        }

        var orderedHistory = request.History
            .OrderBy(point => point.PeriodStart)
            .ToArray();
        var usableHistory = orderedHistory
            .Where(point => !point.WasStockout)
            .TakeLast(request.TrainingWindowPeriods)
            .ToArray();
        var fingerprint = Fingerprint(request, orderedHistory);

        if (usableHistory.Length < MinimumUsablePeriods)
        {
            var status = orderedHistory.Length > 0 && usableHistory.Length == 0
                ? ForecastDataStatus.StockoutCensored
                : ForecastDataStatus.InsufficientData;
            return Result.Success(new ForecastResult(
                request.ItemId,
                request.WarehouseId,
                request.Granularity,
                status,
                null,
                request.ModelVersion.Trim(),
                orderedHistory.Length == 0 ? null : orderedHistory[0].PeriodStart,
                orderedHistory.Length == 0 ? null : orderedHistory[^1].PeriodStart,
                request.HorizonPeriods,
                [],
                [],
                null,
                null,
                ForecastRiskLevel.Unknown,
                fingerprint,
                false,
                "At least three non-stockout demand periods are required."));
        }

        var values = usableHistory.Select(point => point.DemandQuantity).ToList();
        var holdoutCount = Math.Max(1, values.Count / 3);
        var trainingValues = values.Take(values.Count - holdoutCount).ToArray();
        var validationValues = values.Skip(values.Count - holdoutCount).ToArray();
        var scores = new[]
        {
            Score(
                ForecastModelKind.MovingAverage,
                trainingValues,
                validationValues,
                request.MovingAverageWindow),
            Score(
                ForecastModelKind.SimpleExponentialSmoothing,
                trainingValues,
                validationValues,
                request.MovingAverageWindow)
        };
        var selected = scores
            .OrderBy(score => score.Metrics.MeanAbsoluteError)
            .ThenBy(score => score.Metrics.MeanAbsolutePercentageError ?? decimal.MaxValue)
            .ThenBy(score => score.Model)
            .First();
        var futureValues = Forecast(
            selected.Model,
            values,
            request.HorizonPeriods,
            request.MovingAverageWindow);
        var uncertainty = selected.Metrics.MeanAbsoluteError * 1.96m;
        var periodStart = orderedHistory[^1].PeriodStart;
        var periods = futureValues
            .Select((quantity, index) =>
            {
                var start = AddPeriods(periodStart, request.Granularity, index + 1);
                return new ForecastPeriod(
                    start,
                    quantity,
                    Math.Max(0, quantity - uncertainty),
                    quantity + uncertainty);
            })
            .ToArray();
        var availableSupply = request.OnHandQuantity +
            request.OnOrderQuantity +
            request.InTransitQuantity;
        var projectedStockout = ProjectStockout(periods, availableSupply);
        var averageDailyDemand = AverageDailyDemand(periods, request.Granularity);
        var daysOfSupply = averageDailyDemand > 0
            ? (decimal?)decimal.Round(availableSupply / averageDailyDemand, 2)
            : null;
        var risk = ResolveRisk(
            projectedStockout,
            periods,
            request.Granularity,
            request.LeadTimePeriods,
            request.SafetyStockQuantity,
            availableSupply);

        return Result.Success(new ForecastResult(
            request.ItemId,
            request.WarehouseId,
            request.Granularity,
            ForecastDataStatus.Ready,
            selected.Model,
            request.ModelVersion.Trim(),
            orderedHistory[0].PeriodStart,
            orderedHistory[^1].PeriodStart,
            request.HorizonPeriods,
            scores,
            periods,
            projectedStockout,
            daysOfSupply,
            risk,
            fingerprint,
            false,
            null));
    }

    public static Result Validate(ForecastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ItemId <= 0 || request.WarehouseId <= 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.scope_invalid",
                "A positive item and warehouse identifier are required."));
        }

        if (request.HorizonPeriods is < 1 or > MaximumHorizonPeriods)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.horizon_invalid",
                $"The forecast horizon must be between 1 and {MaximumHorizonPeriods} periods."));
        }

        if (request.History is null || request.History.Count > MaximumHistoryPeriods)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.history_invalid",
                $"History cannot contain more than {MaximumHistoryPeriods} periods."));
        }

        if (request.TrainingWindowPeriods is < MinimumUsablePeriods or > MaximumHistoryPeriods ||
            request.MovingAverageWindow < 1 ||
            request.MovingAverageWindow > request.TrainingWindowPeriods)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.window_invalid",
                "Training and moving-average windows are outside the supported bounds."));
        }

        if (request.LeadTimePeriods < 0 ||
            request.OnHandQuantity < 0 ||
            request.OnOrderQuantity < 0 ||
            request.InTransitQuantity < 0 ||
            request.SafetyStockQuantity is < 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.quantity_invalid",
                "Inventory quantities and lead time cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(request.ModelVersion) || request.ModelVersion.Trim().Length > 100)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.version_invalid",
                "A bounded model version is required."));
        }

        var periods = request.History.Select(point => point.PeriodStart).ToArray();
        if (periods.Distinct().Count() != periods.Length)
        {
            return Result.Failure(WmsErrors.Validation(
                "forecast.periods_duplicate",
                "Each history period must occur exactly once."));
        }

        return Result.Success();
    }

    private static ForecastModelScore Score(
        ForecastModelKind model,
        IReadOnlyList<decimal> training,
        decimal[] validation,
        int movingAverageWindow)
    {
        var predictions = new List<decimal>(validation.Length);
        var observed = training.ToList();
        foreach (var actual in validation)
        {
            var prediction = Forecast(model, observed, 1, movingAverageWindow)[0];
            predictions.Add(prediction);
            observed.Add(actual);
        }

        var absoluteErrors = predictions
            .Zip(validation, (prediction, actual) => Math.Abs(prediction - actual))
            .ToArray();
        var percentageErrors = predictions
            .Zip(validation, (prediction, actual) => (prediction, actual))
            .Where(pair => pair.actual > 0)
            .Select(pair => Math.Abs(pair.prediction - pair.actual) / pair.actual * 100m)
            .ToArray();
        var bias = predictions
            .Zip(validation, (prediction, actual) => prediction - actual)
            .DefaultIfEmpty()
            .Average();
        return new ForecastModelScore(
            model,
            new ForecastErrorMetrics(
                decimal.Round(absoluteErrors.Average(), 4),
                percentageErrors.Length == 0
                    ? null
                    : decimal.Round(percentageErrors.Average(), 4),
                decimal.Round(bias, 4),
                validation.Length));
    }

    private static decimal[] Forecast(
        ForecastModelKind model,
        List<decimal> values,
        int horizon,
        int movingAverageWindow)
    {
        var output = new decimal[horizon];
        var state = values.ToList();
        for (var index = 0; index < horizon; index++)
        {
            var next = model switch
            {
                ForecastModelKind.MovingAverage => state
                    .TakeLast(Math.Min(movingAverageWindow, state.Count))
                    .Average(),
                ForecastModelKind.SimpleExponentialSmoothing => ExponentialLevel(state),
                _ => throw new ArgumentOutOfRangeException(nameof(model))
            };
            next = Math.Max(0, next);
            output[index] = decimal.Round(next, 4);
            state.Add(next);
        }

        return output;
    }

    private static decimal ExponentialLevel(List<decimal> values)
    {
        var level = values[0];
        for (var index = 1; index < values.Count; index++)
        {
            level = SmoothingAlpha * values[index] + (1 - SmoothingAlpha) * level;
        }

        return level;
    }

    private static DateOnly? ProjectStockout(
        ForecastPeriod[] periods,
        decimal availableSupply)
    {
        var remaining = availableSupply;
        foreach (var period in periods)
        {
            remaining -= period.ForecastQuantity;
            if (remaining <= 0)
            {
                return period.PeriodStart;
            }
        }

        return null;
    }

    private static decimal AverageDailyDemand(
        ForecastPeriod[] periods,
        ForecastGranularity granularity)
    {
        if (periods.Length == 0)
        {
            return 0;
        }

        var divisor = granularity switch
        {
            ForecastGranularity.Daily => 1m,
            ForecastGranularity.Weekly => 7m,
            ForecastGranularity.Monthly => 30m,
            _ => 1m
        };
        return periods.Average(period => period.ForecastQuantity) / divisor;
    }

    private static ForecastRiskLevel ResolveRisk(
        DateOnly? projectedStockout,
        ForecastPeriod[] periods,
        ForecastGranularity granularity,
        int leadTimePeriods,
        decimal? safetyStock,
        decimal availableSupply)
    {
        if (periods.Length == 0)
        {
            return ForecastRiskLevel.Unknown;
        }

        if (safetyStock is not null && availableSupply <= safetyStock.Value)
        {
            return ForecastRiskLevel.High;
        }

        if (projectedStockout is null)
        {
            return ForecastRiskLevel.Low;
        }

        var leadTimeEnd = AddPeriods(periods[0].PeriodStart, granularity, leadTimePeriods);
        return projectedStockout <= leadTimeEnd
            ? ForecastRiskLevel.High
            : ForecastRiskLevel.Medium;
    }

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
        ForecastRequest request,
        IReadOnlyList<ForecastDemandPoint> orderedHistory)
    {
        var builder = new StringBuilder();
        builder.Append(request.ItemId)
            .Append('|')
            .Append(request.WarehouseId)
            .Append('|')
            .Append(request.Granularity)
            .Append('|')
            .Append(request.HorizonPeriods)
            .Append('|')
            .Append(request.TrainingWindowPeriods)
            .Append('|')
            .Append(request.MovingAverageWindow)
            .Append('|')
            .Append(request.LeadTimePeriods)
            .Append('|')
            .Append(request.OnHandQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|')
            .Append(request.OnOrderQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|')
            .Append(request.InTransitQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append('|')
            .Append(request.SafetyStockQuantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")
            .Append('|')
            .Append(request.ModelVersion.Trim());
        foreach (var point in orderedHistory)
        {
            builder.Append('|')
                .Append(point.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                .Append(':')
                .Append(point.DemandQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':')
                .Append(point.WasStockout ? '1' : '0');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}

public sealed record ForecastingSearchQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ForecastingRecalculationQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    ForecastGranularity Granularity = ForecastGranularity.Weekly,
    int HorizonPeriods = 26);

public sealed record ForecastRunSummaryDto(
    int Id,
    int WarehouseId,
    int ItemId,
    string ItemSku,
    string ItemName,
    ForecastGranularity Granularity,
    ForecastDataStatus DataStatus,
    ForecastModelKind? SelectedModel,
    string ModelVersion,
    DateTime SourceCutoffUtc,
    bool? IsStale,
    DateOnly? InputPeriodStart,
    DateOnly? InputPeriodEnd,
    int HorizonPeriods,
    ForecastRiskLevel RiskLevel,
    int ForecastPeriodCount,
    IReadOnlyList<string> DataQualityFlags);

public sealed record ForecastRunPointDto(
    DateOnly PeriodStart,
    decimal? ActualDemand,
    decimal? ForecastQuantity,
    decimal? LowerBound,
    decimal? UpperBound,
    bool WasStockoutCensored,
    decimal? OverrideQuantity,
    int? OverrideVersion);

public sealed record ForecastRunDto(
    int Id,
    int WarehouseId,
    int ItemId,
    string ItemSku,
    string ItemName,
    string BaseUnitOfMeasure,
    ForecastGranularity Granularity,
    ForecastDataStatus DataStatus,
    ForecastModelKind? SelectedModel,
    string ModelVersion,
    string InputFingerprint,
    DateTime SourceCutoffUtc,
    DateOnly? InputPeriodStart,
    DateOnly? InputPeriodEnd,
    int HorizonPeriods,
    int TrainingWindowPeriods,
    int MovingAverageWindow,
    IReadOnlyList<ForecastModelScore> BacktestScores,
    DateOnly? ProjectedStockoutPeriod,
    decimal? DaysOfSupply,
    ForecastRiskLevel RiskLevel,
    string? InsufficientDataReason,
    bool? IsStale,
    IReadOnlyList<string> DataQualityFlags,
    IReadOnlyList<ForecastRunPointDto> Points,
    DateTime CreatedAtUtc);

public sealed record ForecastRunPageDto(
    int Page,
    int PageSize,
    int TotalItems,
    IReadOnlyList<ForecastRunSummaryDto> Items);

public sealed record ForecastActualComparisonDto(
    int ForecastRunId,
    int EvaluatedPeriods,
    decimal? MeanAbsoluteError,
    decimal? MeanAbsolutePercentageError,
    decimal? Bias,
    DateTime ActualsThroughUtc,
    string MeasurementUom);

public sealed record ForecastOverrideInput(
    DateOnly PeriodStart,
    decimal Quantity,
    string Reason);

public sealed record ForecastOverrideDto(
    int RunId,
    DateOnly PeriodStart,
    decimal Quantity,
    int Version,
    string Reason,
    string CreatedByUserId,
    DateTime CreatedAtUtc);

public sealed record ForecastRecalculationResultDto(
    int ItemsExamined,
    int RunsCreated,
    int RunsReused,
    IReadOnlyList<string> DataQualityFlags);

public interface IForecastingService
{
    Task<Result<ForecastRecalculationResultDto>> RecalculateAsync(
        ForecastingRecalculationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ForecastRunPageDto>> SearchAsync(
        ForecastingSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ForecastRunDto>> GetAsync(
        int runId,
        CancellationToken cancellationToken = default);

    Task<Result<ForecastActualComparisonDto>> CompareActualsAsync(
        int runId,
        CancellationToken cancellationToken = default);

    Task<Result<ForecastOverrideDto>> CreateOverrideAsync(
        int runId,
        ForecastOverrideInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
