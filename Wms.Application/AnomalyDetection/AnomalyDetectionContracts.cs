using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;
using Wms.Application.Identity;

namespace Wms.Application.AnomalyDetection;

public enum AnomalyRuleKind
{
    InventoryAdjustment,
    ReversalBurst,
    CountVariance,
    DuplicateScan,
    ReceivingDiscrepancy,
    ShippingDiscrepancy,
    AgeingWork,
    IntegrationFailure,
    NegativeBalance
}

public enum AnomalySeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum AnomalyStatus
{
    New,
    Investigating,
    Explained,
    ConfirmedIssue,
    FalsePositive,
    Resolved,
    Suppressed
}

public sealed record AnomalySignal(
    AnomalyRuleKind RuleKind,
    string ReferenceType,
    string ReferenceId,
    int WarehouseId,
    decimal ObservedValue,
    decimal ExpectedValue,
    decimal Threshold,
    DateTimeOffset ObservedAtUtc,
    string? ActorReference = null);

public sealed record AnomalyDetectionRequest(
    int WarehouseId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<AnomalySignal> Signals,
    IReadOnlySet<int>? AllowedWarehouseIds = null,
    IReadOnlySet<string>? PermissionCodes = null,
    int MaximumFindings = 1_000,
    string RuleVersion = "rules.v1");

public sealed record AnomalyFinding(
    string Fingerprint,
    AnomalyRuleKind RuleKind,
    AnomalySeverity Severity,
    AnomalyStatus Status,
    int WarehouseId,
    string ReferenceType,
    string ReferenceId,
    decimal ObservedValue,
    decimal ExpectedValue,
    decimal Threshold,
    string Explanation,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset GeneratedAtUtc,
    string RuleVersion,
    bool CanMutateInventory,
    bool CanBlockUser);

public sealed record AnomalyDispositionRequest(
    AnomalyStatus CurrentStatus,
    AnomalyStatus NextStatus,
    string Comment,
    DateTimeOffset? SuppressionExpiresAtUtc = null,
    IReadOnlyList<string>? EvidenceReferences = null);

public interface IAnomalyDetectionService
{
    Task<Result<IReadOnlyList<AnomalyFinding>>> DetectAsync(
        AnomalyDetectionRequest request,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default);
}

public static class AnomalyDetectionPolicy
{
    public const int MaximumWindowDays = 31;
    public const int MaximumSignals = 10_000;
    public const int MaximumFindings = 1_000;
    public const int MaximumCommentLength = 1_000;

    private static readonly string[] SensitiveActorMarkers =
    ["@", "email", "phone", "mobile", "name", "password", "token"];

    public static Result Validate(AnomalyDetectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.WarehouseId <= 0 || request.FromUtc > request.ToUtc ||
            request.ToUtc - request.FromUtc > TimeSpan.FromDays(MaximumWindowDays))
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.window_invalid",
                "The anomaly window must be ordered, warehouse-scoped, and no longer than thirty-one days."));
        }

        if (request.Signals is null || request.Signals.Count > MaximumSignals)
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.signals_invalid",
                $"At most {MaximumSignals} anomaly signals may be evaluated in one run."));
        }

        if (request.MaximumFindings is < 1 or > MaximumFindings)
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.findings_limit_invalid",
                $"The finding limit must be between 1 and {MaximumFindings}."));
        }

        if (string.IsNullOrWhiteSpace(request.RuleVersion) || request.RuleVersion.Trim().Length > 100)
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.rule_version_invalid",
                "A bounded rule version is required."));
        }

        if (request.AllowedWarehouseIds is null ||
            !request.AllowedWarehouseIds.Contains(request.WarehouseId))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "anomaly.warehouse_forbidden",
                "The requested warehouse is outside the authenticated warehouse scope."));
        }

        if (request.PermissionCodes is null ||
            (!request.PermissionCodes.Contains(WmsPermissions.All) &&
             !request.PermissionCodes.Contains(WmsPermissions.ReportsRead)))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "anomaly.permission_denied",
                "Reports permission is required to view operational anomaly signals."));
        }

        foreach (var signal in request.Signals)
        {
            if (signal.WarehouseId != request.WarehouseId ||
                string.IsNullOrWhiteSpace(signal.ReferenceType) ||
                string.IsNullOrWhiteSpace(signal.ReferenceId) ||
                signal.ReferenceType.Trim().Length > 100 ||
                signal.ReferenceId.Trim().Length > 200 ||
                signal.Threshold < 0 ||
                signal.ObservedAtUtc < request.FromUtc ||
                signal.ObservedAtUtc > request.ToUtc)
            {
                return Result.Failure(WmsErrors.Validation(
                    "anomaly.signal_invalid",
                    "Every anomaly signal must be bounded to the requested warehouse and time window."));
            }

            if (!string.IsNullOrWhiteSpace(signal.ActorReference) &&
                SensitiveActorMarkers.Any(marker =>
                    signal.ActorReference.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Failure(WmsErrors.Validation(
                    "anomaly.actor_reference_invalid",
                    "Actor references must be opaque operational identifiers without personal data."));
            }
        }

        return Result.Success();
    }

    public static Result ValidateDisposition(
        AnomalyDispositionRequest request,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Comment) ||
            request.Comment.Trim().Length > MaximumCommentLength)
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.comment_invalid",
                "A bounded investigation comment is required."));
        }

        if (!IsTransitionAllowed(request.CurrentStatus, request.NextStatus))
        {
            return Result.Failure(WmsErrors.Conflict(
                "anomaly.transition_invalid",
                "The anomaly lifecycle transition is not allowed."));
        }

        if (request.NextStatus == AnomalyStatus.Suppressed &&
            (request.SuppressionExpiresAtUtc is null ||
             request.SuppressionExpiresAtUtc <= nowUtc ||
             request.SuppressionExpiresAtUtc > nowUtc.AddDays(31)))
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.suppression_expiry_invalid",
                "Suppression must have a future expiry no later than thirty-one days."));
        }

        if (request.NextStatus != AnomalyStatus.Suppressed &&
            request.SuppressionExpiresAtUtc is not null)
        {
            return Result.Failure(WmsErrors.Validation(
                "anomaly.suppression_expiry_unexpected",
                "A suppression expiry is only valid for a suppressed finding."));
        }

        return Result.Success();
    }

    public static bool IsTransitionAllowed(
        AnomalyStatus current,
        AnomalyStatus next) =>
        current == next || (current, next) switch
        {
            (AnomalyStatus.New, AnomalyStatus.Investigating) => true,
            (AnomalyStatus.New, AnomalyStatus.FalsePositive) => true,
            (AnomalyStatus.New, AnomalyStatus.Suppressed) => true,
            (AnomalyStatus.Investigating, AnomalyStatus.Explained) => true,
            (AnomalyStatus.Investigating, AnomalyStatus.ConfirmedIssue) => true,
            (AnomalyStatus.Investigating, AnomalyStatus.FalsePositive) => true,
            (AnomalyStatus.Investigating, AnomalyStatus.Suppressed) => true,
            (AnomalyStatus.ConfirmedIssue, AnomalyStatus.Resolved) => true,
            (AnomalyStatus.Suppressed, AnomalyStatus.Investigating) => true,
            _ => false
        };
}

public static class AnomalyDetector
{
    public static Result<IReadOnlyList<AnomalyFinding>> Detect(
        AnomalyDetectionRequest request,
        DateTimeOffset generatedAtUtc)
    {
        var validation = AnomalyDetectionPolicy.Validate(request);
        if (validation.IsFailure)
        {
            return validation.ToFailure<IReadOnlyList<AnomalyFinding>>();
        }

        var findings = request.Signals
            .Where(IsAnomalous)
            .Select(signal => CreateFinding(signal, request.RuleVersion.Trim(), generatedAtUtc))
            .DistinctBy(finding => finding.Fingerprint, StringComparer.Ordinal)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.ObservedAtUtc)
            .ThenBy(finding => finding.Fingerprint, StringComparer.Ordinal)
            .Take(request.MaximumFindings)
            .ToArray();
        return Result.Success<IReadOnlyList<AnomalyFinding>>(findings);
    }

    private static bool IsAnomalous(AnomalySignal signal) =>
        signal.RuleKind == AnomalyRuleKind.NegativeBalance
            ? signal.ObservedValue < 0
            : Math.Abs(signal.ObservedValue - signal.ExpectedValue) >= signal.Threshold &&
              signal.Threshold > 0;

    private static AnomalyFinding CreateFinding(
        AnomalySignal signal,
        string ruleVersion,
        DateTimeOffset generatedAtUtc)
    {
        var delta = Math.Abs(signal.ObservedValue - signal.ExpectedValue);
        var ratio = signal.Threshold > 0 ? delta / signal.Threshold : decimal.MaxValue;
        var severity = signal.RuleKind == AnomalyRuleKind.NegativeBalance || ratio >= 3
            ? AnomalySeverity.High
            : ratio >= 2
                ? AnomalySeverity.Medium
                : AnomalySeverity.Low;
        var fingerprint = Fingerprint(signal, ruleVersion);
        return new AnomalyFinding(
            fingerprint,
            signal.RuleKind,
            severity,
            AnomalyStatus.New,
            signal.WarehouseId,
            signal.ReferenceType.Trim(),
            signal.ReferenceId.Trim(),
            signal.ObservedValue,
            signal.ExpectedValue,
            signal.Threshold,
            $"{signal.RuleKind} observed {signal.ObservedValue} against expected {signal.ExpectedValue} with threshold {signal.Threshold}.",
            signal.ObservedAtUtc,
            generatedAtUtc,
            ruleVersion,
            false,
            false);
    }

    private static string Fingerprint(AnomalySignal signal, string ruleVersion)
    {
        var input = string.Join(
            "|",
            signal.WarehouseId,
            signal.RuleKind,
            signal.ReferenceType.Trim(),
            signal.ReferenceId.Trim(),
            signal.ObservedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ruleVersion);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();
    }
}
