using FluentAssertions;
using Wms.Application.Forecasting;

namespace Wms.Application.Tests.Forecasting;

public sealed class ForecastBaselineEngineTests
{
    [Fact]
    public void Selects_a_repeatable_baseline_and_projects_supply_risk()
    {
        var request = Request(
            [10, 12, 11, 14, 13, 15],
            horizon: 3,
            onHand: 20,
            onOrder: 0,
            inTransit: 0,
            leadTime: 1);

        var first = ForecastBaselineEngine.Generate(request);
        var second = ForecastBaselineEngine.Generate(request);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Should().BeEquivalentTo(second.Value);
        first.Value.DataStatus.Should().Be(ForecastDataStatus.Ready);
        first.Value.SelectedModel.Should().NotBeNull();
        first.Value.BacktestScores.Should().HaveCount(2);
        first.Value.Periods.Should().HaveCount(3);
        first.Value.InputFingerprint.Should().HaveLength(64);
        first.Value.CanCreateOrders.Should().BeFalse();
    }

    [Fact]
    public void Censors_stockout_periods_and_reports_insufficient_data_when_cold_starting()
    {
        var request = Request(
            [100, 90, 80],
            horizon: 2,
            stockouts: [true, true, true]);

        var result = ForecastBaselineEngine.Generate(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.DataStatus.Should().Be(ForecastDataStatus.StockoutCensored);
        result.Value.SelectedModel.Should().BeNull();
        result.Value.Periods.Should().BeEmpty();
        result.Value.InsufficientDataReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Produces_backtest_metrics_for_intermittent_demand_without_dividing_by_zero()
    {
        var result = ForecastBaselineEngine.Generate(
            Request([0, 0, 5, 0, 8, 0, 4], horizon: 2));

        result.IsSuccess.Should().BeTrue();
        result.Value.BacktestScores.Should().AllSatisfy(score =>
            score.Metrics.MeanAbsoluteError.Should().BeGreaterThanOrEqualTo(0));
        result.Value.BacktestScores
            .Where(score => score.Metrics.MeanAbsolutePercentageError.HasValue)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Rejects_duplicate_periods_and_negative_demand()
    {
        var duplicate = Request([1, 2, 3]) with
        {
            History =
            [
                new ForecastDemandPoint(new DateOnly(2026, 1, 1), 1),
                new ForecastDemandPoint(new DateOnly(2026, 1, 1), 2),
                new ForecastDemandPoint(new DateOnly(2026, 1, 8), 3)
            ]
        };
        var negative = Request([-1, 2, 3]);

        ForecastBaselineEngine.Generate(duplicate).ErrorCode
            .Should().Be("forecast.periods_duplicate");
        ForecastBaselineEngine.Generate(negative).ErrorCode
            .Should().Be("forecast.demand_invalid");
    }

    [Fact]
    public void Projects_high_risk_when_supply_reaches_stockout_before_lead_time()
    {
        var result = ForecastBaselineEngine.Generate(
            Request([10, 10, 10, 10, 10, 10], horizon: 4, onHand: 5, leadTime: 2));

        result.IsSuccess.Should().BeTrue();
        result.Value.ProjectedStockoutPeriod.Should().NotBeNull();
        result.Value.RiskLevel.Should().Be(ForecastRiskLevel.High);
    }

    private static ForecastRequest Request(
        IReadOnlyList<decimal> values,
        int horizon = 2,
        decimal onHand = 0,
        decimal onOrder = 0,
        decimal inTransit = 0,
        int leadTime = 1,
        IReadOnlyList<bool>? stockouts = null)
    {
        var start = new DateOnly(2026, 1, 1);
        return new ForecastRequest(
            17,
            3,
            ForecastGranularity.Weekly,
            horizon,
            values.Select((value, index) => new ForecastDemandPoint(
                    start.AddDays(index * 7),
                    value,
                    stockouts?[index] ?? false))
                .ToArray(),
            TrainingWindowPeriods: values.Count,
            MovingAverageWindow: 3,
            OnHandQuantity: onHand,
            OnOrderQuantity: onOrder,
            InTransitQuantity: inTransit,
            LeadTimePeriods: leadTime);
    }
}
