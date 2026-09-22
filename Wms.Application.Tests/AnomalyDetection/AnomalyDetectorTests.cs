using FluentAssertions;
using Wms.Application.AnomalyDetection;
using Wms.Application.Identity;

namespace Wms.Application.Tests.AnomalyDetection;

public sealed class AnomalyDetectorTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Produces_explainable_deduplicated_finding_without_mutation_authority()
    {
        var signal = Signal(AnomalyRuleKind.InventoryAdjustment, 30, 10, 5);
        var result = AnomalyDetector.Detect(
            Request(signal, signal),
            To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value[0].Severity.Should().Be(AnomalySeverity.High);
        result.Value[0].Explanation.Should().Contain("InventoryAdjustment");
        result.Value[0].CanMutateInventory.Should().BeFalse();
        result.Value[0].CanBlockUser.Should().BeFalse();
        result.Value[0].Status.Should().Be(AnomalyStatus.New);
    }

    [Fact]
    public void Does_not_flag_below_threshold_but_flags_negative_balance()
    {
        var below = Signal(AnomalyRuleKind.CountVariance, 11, 10, 5);
        var negative = Signal(AnomalyRuleKind.NegativeBalance, -1, 0, 0);

        var result = AnomalyDetector.Detect(Request(below, negative), To);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(finding =>
            finding.RuleKind == AnomalyRuleKind.NegativeBalance);
    }

    [Fact]
    public void Requires_reports_permission_and_warehouse_scope()
    {
        var noPermission = AnomalyDetector.Detect(
            Request(Signal(AnomalyRuleKind.DuplicateScan, 5, 1, 1)) with
            {
                PermissionCodes = new HashSet<string>()
            },
            To);
        var wrongWarehouse = AnomalyDetector.Detect(
            Request(Signal(AnomalyRuleKind.DuplicateScan, 5, 1, 1)) with
            {
                AllowedWarehouseIds = new HashSet<int> { 99 }
            },
            To);

        noPermission.ErrorCode.Should().Be("anomaly.permission_denied");
        wrongWarehouse.ErrorCode.Should().Be("anomaly.warehouse_forbidden");
    }

    [Fact]
    public void Requires_time_bounded_suppression_and_auditable_comment()
    {
        var invalid = AnomalyDetectionPolicy.ValidateDisposition(
            new AnomalyDispositionRequest(
                AnomalyStatus.New,
                AnomalyStatus.Suppressed,
                "Suppress this signal"),
            From);
        var valid = AnomalyDetectionPolicy.ValidateDisposition(
            new AnomalyDispositionRequest(
                AnomalyStatus.New,
                AnomalyStatus.Suppressed,
                "Known seasonal count window",
                From.AddDays(7),
                ["count-run-123"]),
            From);

        invalid.ErrorCode.Should().Be("anomaly.suppression_expiry_invalid");
        valid.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Rejects_personal_actor_references_and_invalid_transitions()
    {
        var personal = AnomalyDetector.Detect(
            Request(Signal(AnomalyRuleKind.ReversalBurst, 5, 1, 1) with
            {
                ActorReference = "operator@example.com"
            }),
            To);
        var transition = AnomalyDetectionPolicy.ValidateDisposition(
            new AnomalyDispositionRequest(
                AnomalyStatus.New,
                AnomalyStatus.Resolved,
                "Resolved"),
            From);

        personal.ErrorCode.Should().Be("anomaly.actor_reference_invalid");
        transition.ErrorCode.Should().Be("anomaly.transition_invalid");
    }

    private static AnomalyDetectionRequest Request(params AnomalySignal[] signals) =>
        new(
            3,
            From,
            To,
            signals,
            new HashSet<int> { 3 },
            new HashSet<string> { WmsPermissions.ReportsRead });

    private static AnomalySignal Signal(
        AnomalyRuleKind ruleKind,
        decimal observed,
        decimal expected,
        decimal threshold) =>
        new(
            ruleKind,
            "inventory-ledger",
            "reference-1",
            3,
            observed,
            expected,
            threshold,
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
}
