using Wms.Application.Common;

namespace Wms.Application.Performance;

public enum PerformanceWorkloadKind
{
    LoginDashboard,
    InventoryLookup,
    ReceivingScan,
    Allocation,
    PickCompletion,
    PackingShipping,
    Transfer,
    ReportQuery,
    BulkImport,
    WebhookDelivery,
    BackgroundJob,
    Reconciliation
}

public sealed record PerformanceBudget(
    PerformanceWorkloadKind Workload,
    decimal MinimumThroughputPerSecond,
    decimal MaximumP50Milliseconds,
    decimal MaximumP95Milliseconds,
    decimal MaximumP99Milliseconds,
    decimal MaximumErrorRate,
    decimal MaximumConflictRate);

public sealed record PerformanceRunMetadata(
    string Environment,
    string DatasetFingerprint,
    string HardwareProfile,
    string ApplicationRevision,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record PerformanceRunResult(
    PerformanceBudget Budget,
    PerformanceRunMetadata Metadata,
    decimal ThroughputPerSecond,
    decimal P50Milliseconds,
    decimal P95Milliseconds,
    decimal P99Milliseconds,
    decimal ErrorRate,
    decimal ConflictRate,
    int ReconciliationErrorCount,
    bool BusinessOutcomeAssertionsPassed);

public sealed record PerformanceEvaluation(
    PerformanceWorkloadKind Workload,
    bool Passed,
    IReadOnlyList<string> Breaches,
    PerformanceRunMetadata Metadata,
    decimal ThroughputPerSecond,
    decimal P50Milliseconds,
    decimal P95Milliseconds,
    decimal P99Milliseconds);

public static class PerformanceWorkloadCatalog
{
    public static IReadOnlyList<PerformanceBudget> Default { get; } =
    [
        new(PerformanceWorkloadKind.LoginDashboard, 5, 500, 1_500, 3_000, 0.01m, 0.01m),
        new(PerformanceWorkloadKind.InventoryLookup, 20, 150, 500, 1_000, 0.01m, 0.02m),
        new(PerformanceWorkloadKind.ReceivingScan, 10, 250, 750, 1_500, 0.01m, 0.05m),
        new(PerformanceWorkloadKind.Allocation, 5, 500, 2_000, 4_000, 0.01m, 0.05m),
        new(PerformanceWorkloadKind.ReportQuery, 3, 750, 2_500, 5_000, 0.01m, 0.01m),
        new(PerformanceWorkloadKind.BulkImport, 1, 1_000, 5_000, 10_000, 0.02m, 0.02m),
        new(PerformanceWorkloadKind.BackgroundJob, 1, 1_000, 5_000, 10_000, 0.01m, 0.05m),
        new(PerformanceWorkloadKind.Reconciliation, 0.1m, 2_000, 10_000, 20_000, 0, 0)
    ];
}

public static class PerformanceQualificationPolicy
{
    public static Result ValidateBudget(PerformanceBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.MinimumThroughputPerSecond < 0 ||
            budget.MaximumP50Milliseconds <= 0 ||
            budget.MaximumP95Milliseconds < budget.MaximumP50Milliseconds ||
            budget.MaximumP99Milliseconds < budget.MaximumP95Milliseconds ||
            budget.MaximumErrorRate is < 0 or > 1 ||
            budget.MaximumConflictRate is < 0 or > 1)
        {
            return Result.Failure(WmsErrors.Validation(
                "performance.budget_invalid",
                "Performance budgets must be ordered and use bounded rates."));
        }

        return Result.Success();
    }

    public static Result ValidateRun(PerformanceRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var budgetResult = ValidateBudget(run.Budget);
        if (budgetResult.IsFailure)
        {
            return budgetResult;
        }

        if (run.Metadata is null ||
            string.IsNullOrWhiteSpace(run.Metadata.Environment) ||
            string.IsNullOrWhiteSpace(run.Metadata.DatasetFingerprint) ||
            string.IsNullOrWhiteSpace(run.Metadata.HardwareProfile) ||
            string.IsNullOrWhiteSpace(run.Metadata.ApplicationRevision) ||
            run.Metadata.StartedAtUtc > run.Metadata.CompletedAtUtc ||
            run.ThroughputPerSecond < 0 ||
            run.P50Milliseconds < 0 ||
            run.P95Milliseconds < run.P50Milliseconds ||
            run.P99Milliseconds < run.P95Milliseconds ||
            run.ErrorRate is < 0 or > 1 ||
            run.ConflictRate is < 0 or > 1 ||
            run.ReconciliationErrorCount < 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "performance.run_invalid",
                "A performance result requires bounded metrics and workload/environment metadata."));
        }

        return Result.Success();
    }
}

public static class PerformanceBudgetEvaluator
{
    public static Result<PerformanceEvaluation> Evaluate(PerformanceRunResult run)
    {
        var validation = PerformanceQualificationPolicy.ValidateRun(run);
        if (validation.IsFailure)
        {
            return validation.ToFailure<PerformanceEvaluation>();
        }

        var breaches = new List<string>();
        if (run.ThroughputPerSecond < run.Budget.MinimumThroughputPerSecond)
        {
            breaches.Add("throughput");
        }

        if (run.P50Milliseconds > run.Budget.MaximumP50Milliseconds)
        {
            breaches.Add("p50");
        }

        if (run.P95Milliseconds > run.Budget.MaximumP95Milliseconds)
        {
            breaches.Add("p95");
        }

        if (run.P99Milliseconds > run.Budget.MaximumP99Milliseconds)
        {
            breaches.Add("p99");
        }

        if (run.ErrorRate > run.Budget.MaximumErrorRate)
        {
            breaches.Add("errors");
        }

        if (run.ConflictRate > run.Budget.MaximumConflictRate)
        {
            breaches.Add("conflicts");
        }

        if (!run.BusinessOutcomeAssertionsPassed)
        {
            breaches.Add("business-outcomes");
        }

        if (run.ReconciliationErrorCount != 0)
        {
            breaches.Add("reconciliation");
        }

        return Result.Success(new PerformanceEvaluation(
            run.Budget.Workload,
            breaches.Count == 0,
            breaches,
            run.Metadata,
            run.ThroughputPerSecond,
            run.P50Milliseconds,
            run.P95Milliseconds,
            run.P99Milliseconds));
    }
}
