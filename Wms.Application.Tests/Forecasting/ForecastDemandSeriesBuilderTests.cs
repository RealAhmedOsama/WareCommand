using FluentAssertions;
using Wms.Application.Forecasting;

namespace Wms.Application.Tests.Forecasting;

public sealed class ForecastDemandSeriesBuilderTests
{
    [Fact]
    public void Builds_causal_warehouse_local_daily_series_with_returns_and_missing_zeros()
    {
        var cutoff = Utc(2026, 9, 23, 12);
        var events = new[]
        {
            Source(1, Utc(2026, 9, 18, 1), 10m, "EA", ForecastSourceEventKind.Shipment),
            Source(2, Utc(2026, 9, 20, 1), 12m, "EA", ForecastSourceEventKind.Shipment),
            Source(3, Utc(2026, 9, 21, 2), 3m, "EA", ForecastSourceEventKind.CustomerReturn),
            Source(4, Utc(2026, 9, 23, 16), 500m, "EA", ForecastSourceEventKind.Shipment)
        };

        var result = ForecastDemandSeriesBuilder.Build(
            events,
            [],
            "EA",
            "America/New_York",
            ForecastGranularity.Daily,
            cutoff);

        result.IsComplete.Should().BeTrue();
        result.HasShipmentHistory.Should().BeTrue();
        result.History.Select(point => point.PeriodStart)
            .Should().Equal(
                new DateOnly(2026, 9, 17),
                new DateOnly(2026, 9, 18),
                new DateOnly(2026, 9, 19),
                new DateOnly(2026, 9, 20),
                new DateOnly(2026, 9, 21),
                new DateOnly(2026, 9, 22));
        result.History.Select(point => point.DemandQuantity)
            .Should().Equal(10m, 0m, 12m, -3m, 0m, 0m);
        result.DataQualityFlags.Should().Contain("missing-periods-filled-with-zero");
        result.DataQualityFlags.Should().Contain(
            "customer-returns-applied-on-warehouse-local-receipt-period");
        result.History.Should().NotContain(point => point.DemandQuantity == 500m);
    }

    [Fact]
    public void Uses_monday_week_boundaries_and_excludes_the_open_business_week()
    {
        var result = ForecastDemandSeriesBuilder.Build(
            [
                Source(1, Utc(2026, 9, 11, 12), 7m, "EA", ForecastSourceEventKind.Shipment),
                Source(2, Utc(2026, 9, 22, 12), 90m, "EA", ForecastSourceEventKind.Shipment)
            ],
            [],
            "EA",
            "UTC",
            ForecastGranularity.Weekly,
            Utc(2026, 9, 23, 12));

        result.History.Select(point => point.PeriodStart)
            .Should().Equal(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 14));
        result.History.Select(point => point.DemandQuantity).Should().Equal(7m, 0m);
    }

    [Fact]
    public void Converts_historical_units_and_marks_unconvertible_source_history_incomplete()
    {
        var events = new[]
        {
            Source(1, Utc(2026, 9, 20, 10), 2m, "CASE", ForecastSourceEventKind.Shipment)
        };
        var conversions = new[] { new ForecastUnitConversion("CASE", "EA", 12m, 2) };

        var converted = ForecastDemandSeriesBuilder.Build(
            events,
            conversions,
            "EA",
            "UTC",
            ForecastGranularity.Daily,
            Utc(2026, 9, 23, 12));
        var incomplete = ForecastDemandSeriesBuilder.Build(
            events,
            [],
            "EA",
            "UTC",
            ForecastGranularity.Daily,
            Utc(2026, 9, 23, 12));

        converted.IsComplete.Should().BeTrue();
        converted.History.First(point => point.PeriodStart == new DateOnly(2026, 9, 20))
            .DemandQuantity.Should().Be(24m);
        incomplete.IsComplete.Should().BeFalse();
        incomplete.DataQualityFlags.Should().Contain("unconvertible-source-unit");
    }

    [Fact]
    public void Does_not_fabricate_zero_history_without_a_completed_shipment()
    {
        var result = ForecastDemandSeriesBuilder.Build(
            [Source(1, Utc(2026, 9, 22, 12), 10m, "EA", ForecastSourceEventKind.CustomerReturn)],
            [],
            "EA",
            "UTC",
            ForecastGranularity.Daily,
            Utc(2026, 9, 23, 12));

        result.History.Should().BeEmpty();
        result.HasShipmentHistory.Should().BeFalse();
        result.DataQualityFlags.Should().Contain("no-completed-shipment-history");
    }

    [Fact]
    public void Engine_keeps_return_adjustments_signed_and_starts_after_censored_history()
    {
        var first = new DateOnly(2026, 9, 1);
        var request = new ForecastRequest(
            1,
            1,
            ForecastGranularity.Daily,
            1,
            [
                new ForecastDemandPoint(first, 10m),
                new ForecastDemandPoint(first.AddDays(1), -2m),
                new ForecastDemandPoint(first.AddDays(2), 8m),
                new ForecastDemandPoint(first.AddDays(3), 70m, WasStockout: true)
            ]);

        var result = ForecastBaselineEngine.Generate(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Periods.Should().ContainSingle()
            .Which.PeriodStart.Should().Be(first.AddDays(4));
        result.Value.Periods[0].ForecastQuantity.Should().BeGreaterThanOrEqualTo(0m);
        result.Value.InputPeriodEnd.Should().Be(first.AddDays(3));
    }

    private static ForecastSourceEvent Source(
        long id,
        DateTime occurredAtUtc,
        decimal quantity,
        string unit,
        ForecastSourceEventKind kind) =>
        new(id, occurredAtUtc, quantity, unit, kind);

    private static DateTime Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
