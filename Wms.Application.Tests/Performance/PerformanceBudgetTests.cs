using FluentAssertions;
using Wms.Application.Performance;

namespace Wms.Application.Tests.Performance;

public sealed class PerformanceBudgetTests
{
    [Fact]
    public void Default_workloads_have_ordered_budgets()
    {
        PerformanceWorkloadCatalog.Default.Should().NotBeEmpty();

        foreach (var budget in PerformanceWorkloadCatalog.Default)
        {
            PerformanceQualificationPolicy.ValidateBudget(budget).IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public void Evaluates_latency_throughput_and_error_budgets_with_metadata()
    {
        var budget = PerformanceWorkloadCatalog.Default
            .Single(item => item.Workload == PerformanceWorkloadKind.InventoryLookup);
        var evaluation = PerformanceBudgetEvaluator.Evaluate(
            new PerformanceRunResult(
                budget,
                Metadata(),
                25,
                100,
                400,
                800,
                0.001m,
                0.01m,
                0,
                true));

        evaluation.IsSuccess.Should().BeTrue();
        evaluation.Value.Passed.Should().BeTrue();
        evaluation.Value.Breaches.Should().BeEmpty();
    }

    [Fact]
    public void Reports_budget_breaches_instead_of_claiming_capacity()
    {
        var budget = PerformanceWorkloadCatalog.Default
            .Single(item => item.Workload == PerformanceWorkloadKind.ReportQuery);
        var evaluation = PerformanceBudgetEvaluator.Evaluate(
            new PerformanceRunResult(
                budget,
                Metadata(),
                1,
                800,
                3_000,
                6_000,
                0.02m,
                0.01m,
                0,
                true));

        evaluation.Value.Passed.Should().BeFalse();
        evaluation.Value.Breaches.Should().Contain(["throughput", "p95", "p99", "errors"]);
    }

    [Fact]
    public void Reconciliation_errors_fail_mutation_workload_even_when_latency_is_good()
    {
        var budget = PerformanceWorkloadCatalog.Default
            .Single(item => item.Workload == PerformanceWorkloadKind.ReceivingScan);
        var evaluation = PerformanceBudgetEvaluator.Evaluate(
            new PerformanceRunResult(
                budget,
                Metadata(),
                20,
                100,
                200,
                300,
                0,
                0,
                1,
                true));

        evaluation.Value.Passed.Should().BeFalse();
        evaluation.Value.Breaches.Should().Contain("reconciliation");
    }

    [Fact]
    public void Rejects_missing_dataset_or_unordered_percentiles()
    {
        var budget = PerformanceWorkloadCatalog.Default[0];
        var result = PerformanceQualificationPolicy.ValidateRun(
            new PerformanceRunResult(
                budget,
                Metadata() with { DatasetFingerprint = string.Empty },
                1,
                100,
                90,
                200,
                0,
                0,
                0,
                true));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("performance.run_invalid");
    }

    private static PerformanceRunMetadata Metadata() =>
        new(
            "qualification",
            "dataset-fingerprint-1",
            "local-postgresql-4cpu-16gb",
            "revision-1",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow);
}
