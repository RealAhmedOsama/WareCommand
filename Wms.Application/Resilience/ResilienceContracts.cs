using Wms.Application.Common;

namespace Wms.Application.Resilience;

public enum ResilienceFailurePoint
{
    DatabaseConnection,
    TransactionConflict,
    BeforeCommit,
    AfterCommitBeforeResponse,
    JobWorkerRestart,
    ProviderTimeout,
    ProviderPermanentFailure,
    StorageFailure,
    ClientDisconnect,
    ProcessRestart,
    DataProtectionKeyUnavailable,
    BackupRestore
}

public enum ResilienceRecoveryKind
{
    Rollback,
    IdempotentReplay,
    DeadLetter,
    ResumeDurableWork,
    RestoreAndReconcile,
    OperatorIntervention
}

public enum ResilienceFailureClassification
{
    Retryable,
    Permanent,
    Ambiguous,
    DependencyUnavailable
}

public sealed record ResilienceScenario(
    string ScenarioId,
    ResilienceFailurePoint FailurePoint,
    ResilienceRecoveryKind ExpectedRecovery,
    ResilienceFailureClassification Classification,
    int MaximumRetries,
    bool ExercisesMutation,
    bool ExercisesDurableWork,
    bool RequiresReconciliation);

public sealed record ResilienceTestPlan(
    string Environment,
    bool FaultInjectionEnabled,
    IReadOnlyList<ResilienceScenario> Scenarios,
    string ApplicationRevision,
    string DatasetFingerprint);

public sealed record ResilienceOutcome(
    string ScenarioId,
    int RetryCount,
    int PartialMutationCount,
    int DuplicateBusinessOutcomeCount,
    int ReconciliationErrorCount,
    bool DurableWorkResumed,
    bool DeadLettered,
    bool NoInfiniteRetry,
    bool RecoveryProcedureCompleted,
    string Diagnostics);

public static class ResiliencePolicy
{
    public const int MaximumScenarios = 100;
    public const int MaximumRetries = 10;
    public const int MaximumDiagnosticsLength = 2_000;

    public static Result ValidatePlan(ResilienceTestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Environment) ||
            plan.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase) ||
            plan.Environment.Equals("Staging", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "resilience.production_disabled",
                "Fault injection is disabled for production and staging environments."));
        }

        if (!plan.FaultInjectionEnabled ||
            string.IsNullOrWhiteSpace(plan.ApplicationRevision) ||
            string.IsNullOrWhiteSpace(plan.DatasetFingerprint) ||
            plan.Scenarios is null ||
            plan.Scenarios.Count is < 1 or > MaximumScenarios)
        {
            return Result.Failure(WmsErrors.Validation(
                "resilience.plan_invalid",
                $"A non-production plan with 1 to {MaximumScenarios} bounded scenarios is required."));
        }

        foreach (var scenario in plan.Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.ScenarioId) ||
                scenario.ScenarioId.Trim().Length > 100 ||
                scenario.MaximumRetries is < 0 or > MaximumRetries ||
                scenario.Classification == ResilienceFailureClassification.Permanent &&
                scenario.MaximumRetries > 0)
            {
                return Result.Failure(WmsErrors.Validation(
                    "resilience.scenario_invalid",
                    "Each scenario requires a bounded id, retry limit, and permanent-failure safety."));
            }
        }

        return Result.Success();
    }

    public static Result ValidateOutcome(
        ResilienceTestPlan plan,
        ResilienceOutcome outcome)
    {
        var planResult = ValidatePlan(plan);
        if (planResult.IsFailure)
        {
            return planResult;
        }

        ArgumentNullException.ThrowIfNull(outcome);
        if (string.IsNullOrWhiteSpace(outcome.ScenarioId) ||
            !plan.Scenarios.Any(scenario =>
                string.Equals(scenario.ScenarioId, outcome.ScenarioId, StringComparison.Ordinal)) ||
            outcome.RetryCount < 0 ||
            outcome.PartialMutationCount < 0 ||
            outcome.DuplicateBusinessOutcomeCount < 0 ||
            outcome.ReconciliationErrorCount < 0 ||
            string.IsNullOrWhiteSpace(outcome.Diagnostics) ||
            outcome.Diagnostics.Trim().Length > MaximumDiagnosticsLength)
        {
            return Result.Failure(WmsErrors.Validation(
                "resilience.outcome_invalid",
                "A resilience outcome must reference its plan and carry bounded diagnostics."));
        }

        var scenario = plan.Scenarios.Single(item =>
            string.Equals(item.ScenarioId, outcome.ScenarioId, StringComparison.Ordinal));
        if (outcome.RetryCount > scenario.MaximumRetries || !outcome.NoInfiniteRetry ||
            !outcome.RecoveryProcedureCompleted)
        {
            return Result.Failure(WmsErrors.Conflict(
                "resilience.recovery_failed",
                "The injected failure exceeded its retry/recovery contract."));
        }

        if (outcome.PartialMutationCount != 0 ||
            outcome.DuplicateBusinessOutcomeCount != 0 ||
            (scenario.RequiresReconciliation && outcome.ReconciliationErrorCount != 0))
        {
            return Result.Failure(WmsErrors.Conflict(
                "resilience.integrity_failed",
                "Fault recovery left a partial, duplicate, or unreconciled business outcome."));
        }

        if (scenario.ExpectedRecovery == ResilienceRecoveryKind.DeadLetter && !outcome.DeadLettered ||
            scenario.ExpectedRecovery == ResilienceRecoveryKind.ResumeDurableWork && !outcome.DurableWorkResumed)
        {
            return Result.Failure(WmsErrors.Conflict(
                "resilience.expected_recovery_missing",
                "The expected durable recovery behavior was not observed."));
        }

        return Result.Success();
    }
}

public static class ResilienceScenarioCatalog
{
    public static IReadOnlyList<ResilienceScenario> Critical { get; } =
    [
        new(
            "timeout-after-commit",
            ResilienceFailurePoint.AfterCommitBeforeResponse,
            ResilienceRecoveryKind.IdempotentReplay,
            ResilienceFailureClassification.Ambiguous,
            1,
            true,
            false,
            true),
        new(
            "worker-restart-durable-job",
            ResilienceFailurePoint.JobWorkerRestart,
            ResilienceRecoveryKind.ResumeDurableWork,
            ResilienceFailureClassification.Retryable,
            3,
            true,
            true,
            true),
        new(
            "provider-permanent-failure-dead-letter",
            ResilienceFailurePoint.ProviderPermanentFailure,
            ResilienceRecoveryKind.DeadLetter,
            ResilienceFailureClassification.Permanent,
            0,
            false,
            true,
            false),
        new(
            "backup-restore-reconcile",
            ResilienceFailurePoint.BackupRestore,
            ResilienceRecoveryKind.RestoreAndReconcile,
            ResilienceFailureClassification.DependencyUnavailable,
            0,
            true,
            true,
            true)
    ];
}
