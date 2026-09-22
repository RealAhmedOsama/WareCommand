using FluentAssertions;
using Wms.Application.Resilience;

namespace Wms.Application.Tests.Resilience;

public sealed class ResiliencePolicyTests
{
    private static readonly ResilienceTestPlan Plan = new(
        "Test",
        true,
        ResilienceScenarioCatalog.Critical,
        "revision-1",
        "dataset-1");

    [Fact]
    public void Critical_catalog_is_non_production_and_bounded()
    {
        ResiliencePolicy.ValidatePlan(Plan).IsSuccess.Should().BeTrue();
        ResilienceScenarioCatalog.Critical.Should().Contain(scenario =>
            scenario.ExpectedRecovery == ResilienceRecoveryKind.IdempotentReplay);
        ResilienceScenarioCatalog.Critical.Should().Contain(scenario =>
            scenario.ExpectedRecovery == ResilienceRecoveryKind.DeadLetter);
    }

    [Fact]
    public void Rejects_production_fault_injection_and_infinite_retry_configuration()
    {
        var production = ResiliencePolicy.ValidatePlan(Plan with { Environment = "Production" });
        var infinite = ResiliencePolicy.ValidatePlan(Plan with
        {
            Scenarios =
            [
                Plan.Scenarios[0] with { MaximumRetries = ResiliencePolicy.MaximumRetries + 1 }
            ]
        });

        production.ErrorCode.Should().Be("resilience.production_disabled");
        infinite.ErrorCode.Should().Be("resilience.scenario_invalid");
    }

    [Fact]
    public void Accepts_ambiguous_timeout_when_idempotent_replay_is_proven()
    {
        var scenario = Plan.Scenarios.Single(item => item.ScenarioId == "timeout-after-commit");
        var outcome = new ResilienceOutcome(
            scenario.ScenarioId,
            1,
            0,
            0,
            0,
            false,
            false,
            true,
            true,
            "Replay returned the existing committed business outcome.");

        ResiliencePolicy.ValidateOutcome(Plan, outcome).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Requires_durable_resume_or_dead_letter_as_declared()
    {
        var worker = Plan.Scenarios.Single(item => item.ScenarioId == "worker-restart-durable-job");
        var outcome = new ResilienceOutcome(
            worker.ScenarioId,
            1,
            0,
            0,
            0,
            false,
            false,
            true,
            true,
            "Worker restarted but durable job was not resumed.");

        var result = ResiliencePolicy.ValidateOutcome(Plan, outcome);

        result.ErrorCode.Should().Be("resilience.expected_recovery_missing");
    }

    [Fact]
    public void Rejects_partial_mutations_duplicate_outcomes_and_reconciliation_errors()
    {
        var scenario = Plan.Scenarios[0];
        var outcome = new ResilienceOutcome(
            scenario.ScenarioId,
            1,
            1,
            1,
            1,
            false,
            false,
            true,
            true,
            "Injected failure left a partial state.");

        var result = ResiliencePolicy.ValidateOutcome(Plan, outcome);

        result.ErrorCode.Should().Be("resilience.integrity_failed");
    }
}
