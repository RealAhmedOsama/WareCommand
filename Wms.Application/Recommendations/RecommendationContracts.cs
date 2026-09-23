using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;

namespace Wms.Application.Recommendations;

public enum RecommendationType
{
    Replenishment,
    Slotting,
    WorkloadPriority,
    ExceptionResolution,
    RiskSummary
}

public enum RecommendationStatus
{
    Proposed,
    Reviewed,
    Approved,
    Rejected,
    Expired,
    Executed,
    Executing,
    ExecutionFailed
}

public sealed record RecommendationAction(
    string ActionType,
    string TargetReference,
    decimal? SuggestedQuantity = null,
    string? SuggestedLocation = null);

public sealed record RecommendationDraft(
    RecommendationType Type,
    int WarehouseId,
    string SourceSnapshotId,
    DateTimeOffset SourceFromUtc,
    DateTimeOffset SourceToUtc,
    string SourceStateFingerprint,
    string ProviderName,
    string ModelVersion,
    string DeterministicBaselineVersion,
    decimal Confidence,
    string Explanation,
    RecommendationAction Action,
    IReadOnlyDictionary<string, decimal> NumericFeatures,
    decimal ExpectedImpactLow,
    decimal ExpectedImpactHigh,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record RecommendationRequest(
    RecommendationDraft Draft,
    IReadOnlySet<int>? AllowedWarehouseIds,
    IReadOnlySet<string>? PermissionCodes,
    bool KillSwitchEnabled = false,
    bool ProviderAvailable = true,
    bool ShadowMode = true);

public sealed record RecommendationRecord(
    string RecommendationId,
    RecommendationType Type,
    RecommendationStatus Status,
    int WarehouseId,
    string SourceSnapshotId,
    DateTimeOffset SourceFromUtc,
    DateTimeOffset SourceToUtc,
    string SourceStateFingerprint,
    string ProviderName,
    string ModelVersion,
    string DeterministicBaselineVersion,
    decimal Confidence,
    string Explanation,
    RecommendationAction Action,
    IReadOnlyDictionary<string, decimal> NumericFeatures,
    decimal ExpectedImpactLow,
    decimal ExpectedImpactHigh,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool ShadowMode,
    bool RequiresRevalidation,
    bool CanMutateInventory,
    string? LastDispositionComment,
    long Revision = 1,
    string? ExecutionReference = null,
    string? LastExecutionErrorCode = null,
    int ExecutionAttempts = 0,
    string? ShadowComparison = null);

public sealed record RecommendationApprovalRequest(
    string CurrentStateFingerprint,
    bool DeterministicEligibility,
    IReadOnlySet<int>? AllowedWarehouseIds,
    IReadOnlySet<string>? PermissionCodes);

public sealed record RecommendationQuery(
    int? WarehouseId = null,
    RecommendationType? Type = null,
    RecommendationStatus? Status = null,
    int Page = 1,
    int PageSize = 50);

public sealed record RecommendationPage(
    IReadOnlyList<RecommendationRecord> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record RecommendationGenerationRequest(int WarehouseId, int Limit = 50);

public sealed record RecommendationLifecycleCommand(
    long ExpectedRevision,
    string IdempotencyKey,
    string? Comment = null);

public sealed record RecommendationHistoryEntry(
    string EventType,
    string ActorUserId,
    DateTimeOffset OccurredAtUtc,
    long ResultingRevision,
    string? Comment,
    string? StateFingerprint,
    string? CommandReference,
    string? OutcomeCode);

public sealed record RecommendationRevalidation(
    string CurrentStateFingerprint,
    bool DeterministicEligibility,
    string? Reason = null);

public sealed record RecommendationCommandResult(
    string CommandReference,
    string OutcomeCode);

public sealed record ReplenishmentRecommendationSource(
    ReplenishmentWorkPlanDto Plan,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface IRecommendationDraftProvider
{
    string Name { get; }

    bool IsAvailable { get; }

    Task<Result<RecommendationDraft>> CreateReplenishmentDraftAsync(
        ReplenishmentRecommendationSource source,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Connects one advisory type to authoritative WMS reads and existing commands.
/// Implementations must never mutate inventory directly.
/// </summary>
public interface IRecommendationCommandAdapter
{
    RecommendationType Type { get; }

    Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default);
}

public interface IRecommendationGovernanceService
{
    Task<Result<IReadOnlyList<RecommendationRecord>>> GenerateReplenishmentAsync(
        RecommendationGenerationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationPage>> SearchAsync(
        RecommendationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> GetAsync(
        string recommendationId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RecommendationHistoryEntry>>> GetHistoryAsync(
        string recommendationId,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> ApproveAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> ReviewAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> RejectAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> ExpireAsync(
        string recommendationId,
        long expectedRevision,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<RecommendationRecord>> ExecuteAsync(
        string recommendationId,
        RecommendationLifecycleCommand command,
        CancellationToken cancellationToken = default);
}

public static class RecommendationPolicy
{
    public const int MaximumExplanationLength = 2_000;
    public const int MaximumFeatureCount = 64;
    public const int MaximumSnapshotLength = 200;
    public static readonly TimeSpan MaximumSourceWindow = TimeSpan.FromDays(31);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(30);

    private static readonly string[] UnsafeProviderMarkers =
    [
        "ignore previous",
        "system prompt",
        "developer message",
        "drop table",
        "delete from",
        "execute command",
        "bypass authorization",
        "تجاهل التعليمات",
        "تجاوز الصلاحيات"
    ];

    private static readonly string[] UnsafeFeatureMarkers =
    ["prompt", "raw", "secret", "password", "token", "payload", "email", "phone"];

    public static Result Validate(
        RecommendationRequest request,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Draft);
        var draft = request.Draft;

        if (request.KillSwitchEnabled)
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.kill_switch_active",
                "Recommendation generation is disabled by the operational kill switch.",
                isRetryable: false));
        }

        if (!request.ProviderAvailable)
        {
            return Result.Failure(WmsErrors.Dependency(
                "recommendation.provider_unavailable",
                "The recommendation provider is unavailable; deterministic workflows remain authoritative.",
                isRetryable: true));
        }

        if (!request.ShadowMode)
        {
            return Result.Failure(WmsErrors.Forbidden(
                "recommendation.shadow_required",
                "Recommendations must remain in shadow mode until governance qualification is complete."));
        }

        if (draft.WarehouseId <= 0 || request.AllowedWarehouseIds is null ||
            !request.AllowedWarehouseIds.Contains(draft.WarehouseId))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "recommendation.warehouse_forbidden",
                "The recommendation warehouse is outside the authenticated scope."));
        }

        var requiredPermission = RequiredPermission(draft.Type);
        if (request.PermissionCodes is null ||
            !HasPermission(request.PermissionCodes, WmsPermissions.ReportsRead) ||
            !HasPermission(request.PermissionCodes, requiredPermission))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "recommendation.permission_denied",
                "The authenticated user is not authorized for this recommendation type."));
        }

        if (string.IsNullOrWhiteSpace(draft.SourceSnapshotId) ||
            draft.SourceSnapshotId.Trim().Length > MaximumSnapshotLength ||
            string.IsNullOrWhiteSpace(draft.SourceStateFingerprint) ||
            draft.SourceStateFingerprint.Trim().Length > 128)
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.source_invalid",
                "A bounded source snapshot and state fingerprint are required."));
        }

        if (draft.SourceFromUtc > draft.SourceToUtc ||
            draft.SourceToUtc - draft.SourceFromUtc > MaximumSourceWindow ||
            draft.GeneratedAtUtc > nowUtc.AddMinutes(5) ||
            draft.ExpiresAtUtc <= draft.GeneratedAtUtc ||
            draft.ExpiresAtUtc > draft.GeneratedAtUtc.Add(MaximumLifetime))
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.window_invalid",
                "Recommendation source and expiry windows are outside the bounded governance policy."));
        }

        if (draft.Confidence is < 0 or > 1 ||
            draft.ExpectedImpactLow > draft.ExpectedImpactHigh)
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.impact_invalid",
                "Confidence must be between zero and one and impact bounds must be ordered."));
        }

        if (string.IsNullOrWhiteSpace(draft.ProviderName) || draft.ProviderName.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(draft.ModelVersion) || draft.ModelVersion.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(draft.DeterministicBaselineVersion) ||
            draft.DeterministicBaselineVersion.Trim().Length > 100)
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.version_invalid",
                "Provider, model, and deterministic-baseline versions must be bounded."));
        }

        if (string.IsNullOrWhiteSpace(draft.Explanation) ||
            draft.Explanation.Trim().Length > MaximumExplanationLength ||
            ContainsUnsafeProviderText(draft.Explanation))
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.explanation_invalid",
                "The recommendation explanation is missing, too large, or contains unsafe provider instructions."));
        }

        if (draft.Action is null ||
            string.IsNullOrWhiteSpace(draft.Action.ActionType) ||
            string.IsNullOrWhiteSpace(draft.Action.TargetReference) ||
            draft.Action.ActionType.Trim().Length > 100 ||
            draft.Action.TargetReference.Trim().Length > 200 ||
            draft.Action.SuggestedQuantity is < 0 ||
            (!string.IsNullOrWhiteSpace(draft.Action.SuggestedLocation) &&
             draft.Action.SuggestedLocation.Trim().Length > 200))
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.action_invalid",
                "The advisory action must contain bounded, non-negative, non-secret references."));
        }

        if (draft.NumericFeatures is null || draft.NumericFeatures.Count > MaximumFeatureCount ||
            draft.NumericFeatures.Keys.Any(key =>
                string.IsNullOrWhiteSpace(key) ||
                UnsafeFeatureMarkers.Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase))))
        {
            return Result.Failure(WmsErrors.Validation(
                "recommendation.features_invalid",
                "Only bounded numeric, non-sensitive feature names are accepted."));
        }

        return Result.Success();
    }

    public static Result<RecommendationRecord> Create(
        RecommendationRequest request,
        DateTimeOffset nowUtc)
    {
        var validation = Validate(request, nowUtc);
        if (validation.IsFailure)
        {
            return validation.ToFailure<RecommendationRecord>();
        }

        var draft = request.Draft;
        var recommendationId = Fingerprint(draft);
        return Result.Success(new RecommendationRecord(
            recommendationId,
            draft.Type,
            RecommendationStatus.Proposed,
            draft.WarehouseId,
            draft.SourceSnapshotId.Trim(),
            draft.SourceFromUtc,
            draft.SourceToUtc,
            draft.SourceStateFingerprint.Trim(),
            draft.ProviderName.Trim(),
            draft.ModelVersion.Trim(),
            draft.DeterministicBaselineVersion.Trim(),
            draft.Confidence,
            draft.Explanation.Trim(),
            draft.Action,
            new Dictionary<string, decimal>(draft.NumericFeatures, StringComparer.Ordinal),
            draft.ExpectedImpactLow,
            draft.ExpectedImpactHigh,
            draft.GeneratedAtUtc,
            draft.ExpiresAtUtc,
            request.ShadowMode,
            true,
            false,
            null));
    }

    public static string RequiredPermission(RecommendationType type) =>
        type switch
        {
            RecommendationType.Replenishment => WmsPermissions.InventoryRead,
            RecommendationType.Slotting => WmsPermissions.LocationsRead,
            RecommendationType.WorkloadPriority => WmsPermissions.WorkRead,
            RecommendationType.ExceptionResolution => WmsPermissions.ReportsRead,
            RecommendationType.RiskSummary => WmsPermissions.ReportsRead,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

    public static bool ContainsUnsafeProviderText(string text) =>
        UnsafeProviderMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool HasPermission(IReadOnlySet<string> permissions, string requiredPermission) =>
        permissions.Contains(WmsPermissions.All) || permissions.Contains(requiredPermission);

    public static string Fingerprint(RecommendationDraft draft)
    {
        var canonical = string.Join(
            "|",
            draft.Type,
            draft.WarehouseId,
            draft.SourceSnapshotId.Trim(),
            draft.SourceStateFingerprint.Trim(),
            draft.ProviderName.Trim(),
            draft.ModelVersion.Trim(),
            draft.DeterministicBaselineVersion.Trim(),
            draft.Confidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.Action.ActionType.Trim(),
            draft.Action.TargetReference.Trim(),
            draft.Action.SuggestedQuantity?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            draft.Action.SuggestedLocation?.Trim() ?? string.Empty,
            string.Join(";", draft.NumericFeatures.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")),
            draft.ExpectedImpactLow.ToString(System.Globalization.CultureInfo.InvariantCulture),
            draft.ExpectedImpactHigh.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

public static class RecommendationLifecycle
{
    public const int MaximumCommentLength = 1_000;

    public static Result<RecommendationRecord> MarkReviewed(
        RecommendationRecord recommendation,
        string comment,
        DateTimeOffset nowUtc)
    {
        if (recommendation.Status != RecommendationStatus.Proposed ||
            !IsCommentValid(comment))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.review_invalid",
                "Only a proposed recommendation with a bounded review comment can be reviewed."));
        }

        return Result.Success(recommendation with
        {
            Status = recommendation.ExpiresAtUtc <= nowUtc
                ? RecommendationStatus.Expired
                : RecommendationStatus.Reviewed,
            LastDispositionComment = comment.Trim()
        });
    }

    public static Result<RecommendationRecord> Approve(
        RecommendationRecord recommendation,
        RecommendationApprovalRequest request,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(request);
        if (recommendation.Status != RecommendationStatus.Reviewed)
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.approval_state_invalid",
                "Only a reviewed recommendation can be approved."));
        }

        if (recommendation.ExpiresAtUtc <= nowUtc)
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.expired",
                "The recommendation expired before approval."));
        }

        if (!request.DeterministicEligibility)
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.deterministic_gate_failed",
                "The deterministic WMS eligibility gate rejected this recommendation."));
        }

        if (!string.Equals(
                recommendation.SourceStateFingerprint,
                request.CurrentStateFingerprint.Trim(),
                StringComparison.Ordinal))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.stale",
                "The source state changed; refresh and revalidate before approval."));
        }

        if (request.AllowedWarehouseIds is null ||
            !request.AllowedWarehouseIds.Contains(recommendation.WarehouseId) ||
            request.PermissionCodes is null ||
            !request.PermissionCodes.Contains(WmsPermissions.All) &&
            (!request.PermissionCodes.Contains(WmsPermissions.ReportsRead) ||
             !request.PermissionCodes.Contains(RecommendationPolicy.RequiredPermission(recommendation.Type))))
        {
            return Result.Failure<RecommendationRecord>(WmsErrors.Forbidden(
                "recommendation.approval_forbidden",
                "The current approval context is outside the recommendation scope."));
        }

        return Result.Success(recommendation with
        {
            Status = RecommendationStatus.Approved,
            LastDispositionComment = "Approved after deterministic revalidation.",
            RequiresRevalidation = true
        });
    }

    public static Result<RecommendationRecord> MarkRejected(
        RecommendationRecord recommendation,
        string comment) =>
        recommendation.Status is RecommendationStatus.Proposed or RecommendationStatus.Reviewed
            ? IsCommentValid(comment)
                ? Result.Success(recommendation with
                {
                    Status = RecommendationStatus.Rejected,
                    LastDispositionComment = comment.Trim()
                })
                : Result.Failure<RecommendationRecord>(WmsErrors.Validation(
                    "recommendation.comment_invalid",
                    "A bounded rejection comment is required."))
            : Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.rejection_state_invalid",
                "Only a proposed or reviewed recommendation can be rejected."));

    public static Result<RecommendationRecord> MarkExecuted(
        RecommendationRecord recommendation,
        string normalCommandId,
        DateTimeOffset nowUtc) =>
        (recommendation.Status is RecommendationStatus.Approved or RecommendationStatus.ExecutionFailed) &&
        recommendation.ExpiresAtUtc > nowUtc &&
        !string.IsNullOrWhiteSpace(normalCommandId)
            ? Result.Success(recommendation with
            {
                Status = RecommendationStatus.Executed,
                LastDispositionComment = $"Executed through normal command {normalCommandId.Trim()}."
            })
            : Result.Failure<RecommendationRecord>(WmsErrors.Conflict(
                "recommendation.execution_invalid",
                "Only a current approved recommendation may be linked to a normal command."));

    private static bool IsCommentValid(string comment) =>
        !string.IsNullOrWhiteSpace(comment) && comment.Trim().Length <= MaximumCommentLength;
}
