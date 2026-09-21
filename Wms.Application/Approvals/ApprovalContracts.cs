using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Approvals;

public sealed record ReasonCodeInput(
    string Code,
    ReasonCodeCategory Category,
    string Module,
    string Operation,
    string NameEn,
    string NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool RequiresNotes,
    bool RequiresAttachment,
    ReasonCodeSeverity Severity,
    int? WarehouseId,
    bool IsActive = true);

public sealed record ReasonCodeQuery(
    string? Module = null,
    string? Operation = null,
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record ReasonCodeDto(
    int Id,
    string Code,
    ReasonCodeCategory Category,
    string Module,
    string Operation,
    string NameEn,
    string NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool RequiresNotes,
    bool RequiresAttachment,
    ReasonCodeSeverity Severity,
    int? WarehouseId,
    bool IsActive,
    long Revision);

public sealed record ApprovalPolicyLevelInput(
    int Level,
    IReadOnlyList<string> Roles);

public sealed record ApprovalPolicyInput(
    string Code,
    string Name,
    string Module,
    string Operation,
    int? WarehouseId,
    string? ReasonCode,
    int Priority,
    decimal? MinimumQuantity,
    decimal? MinimumValue,
    decimal? MinimumVariancePercent,
    string? ItemRisk,
    string? StatusRisk,
    IReadOnlyList<ApprovalPolicyLevelInput> Levels,
    int ExpiryMinutes,
    bool RequireSeparationOfDuties,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActive = true);

public sealed record ApprovalPolicyQuery(
    string? Module = null,
    string? Operation = null,
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record ApprovalPolicyLevelDto(
    int Level,
    IReadOnlyList<string> Roles);

public sealed record ApprovalPolicyDto(
    int Id,
    string Code,
    string Name,
    string Module,
    string Operation,
    int? WarehouseId,
    string? ReasonCode,
    int Priority,
    decimal? MinimumQuantity,
    decimal? MinimumValue,
    decimal? MinimumVariancePercent,
    string? ItemRisk,
    string? StatusRisk,
    IReadOnlyList<ApprovalPolicyLevelDto> Levels,
    int ExpiryMinutes,
    bool RequireSeparationOfDuties,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record ApprovalEvaluationInput(
    string Module,
    string Operation,
    int? WarehouseId,
    string ReasonCode,
    string SourceEntityType,
    string SourceEntityId,
    string? SourceReference,
    string RequiredPermission,
    decimal? Quantity,
    decimal? Value,
    decimal? VariancePercent,
    string? ItemRisk,
    string? StatusRisk,
    string CurrentStateHash,
    string? Notes,
    string? AttachmentReference,
    string? IdempotencyKey);

public sealed record ApprovalRequestQuery(
    ApprovalRequestStatus? Status = null,
    int? WarehouseId = null,
    string? RequesterUserId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ApprovalDecisionInput(
    string IdempotencyKey,
    string? Comment = null);

public sealed record ApprovalExecutionInput(
    string IdempotencyKey,
    string CurrentStateHash);

public sealed record ApprovalCompletionInput(
    string? ResultReference = null);

public sealed record ApprovalRequestDto(
    int Id,
    string RequestIdempotencyKey,
    string Module,
    string Operation,
    int? WarehouseId,
    string ReasonCode,
    int ReasonCodeId,
    string? PolicyCode,
    int? PolicyId,
    string SourceEntityType,
    string SourceEntityId,
    string? SourceReference,
    string RequesterUserId,
    string RequiredPermission,
    decimal? Quantity,
    decimal? Value,
    decimal? VariancePercent,
    string? ItemRisk,
    string? StatusRisk,
    string CurrentStateHash,
    string? Notes,
    string? AttachmentReference,
    IReadOnlyList<ApprovalPolicyLevelDto> Levels,
    bool RequireSeparationOfDuties,
    int CurrentLevel,
    ApprovalRequestStatus Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset? ExecutingAtUtc,
    DateTimeOffset? ExecutedAtUtc,
    string? LastComment,
    long Revision,
    IReadOnlyList<ApprovalDecisionDto> Decisions);

public sealed record ApprovalDecisionDto(
    int Id,
    int ApprovalRequestId,
    int Level,
    ApprovalDecisionType Type,
    string IdempotencyKey,
    string ActorUserId,
    string ActorRoleSnapshotJson,
    string? Comment,
    DateTimeOffset OccurredAtUtc);

public sealed record ApprovalExecutionDto(
    int Id,
    int ApprovalRequestId,
    string IdempotencyKey,
    string CurrentStateHash,
    string StartedByUserId,
    ApprovalExecutionStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ResultReference);

public sealed record ApprovalEvaluationDto(
    bool ApprovalRequired,
    string ReasonCode,
    string? SelectedPolicyCode,
    int? SelectedPolicyId,
    ApprovalRequestDto? Request,
    bool WasReplayed = false);

public sealed record ApprovalRequestPageDto(
    IReadOnlyList<ApprovalRequestDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ApprovalInboxItemDto(
    int Id,
    int ApprovalRequestId,
    string RecipientUserId,
    int Level,
    string DeduplicationKey,
    string Title,
    string Message,
    int? WarehouseId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ReadAtUtc,
    DateTimeOffset? ResolvedAtUtc);

public sealed record ApprovalInboxQuery(
    bool IncludeResolved = false,
    int Page = 1,
    int PageSize = 50);

public sealed record ApprovalInboxPageDto(
    IReadOnlyList<ApprovalInboxItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public interface IApprovalRoleDirectory
{
    Task<IReadOnlySet<string>> GetRolesAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> FindUserIdsAsync(
        IReadOnlyCollection<string> roles,
        int? warehouseId,
        CancellationToken cancellationToken = default);
}

public interface IApprovalService
{
    Task<Result<ReasonCodeDto>> SaveReasonCodeAsync(
        int? reasonCodeId,
        ReasonCodeInput input,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ReasonCodeDto>>> ListReasonCodesAsync(
        ReasonCodeQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalPolicyDto>> SaveApprovalPolicyAsync(
        int? policyId,
        ApprovalPolicyInput input,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ApprovalPolicyDto>>> ListApprovalPoliciesAsync(
        ApprovalPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalEvaluationDto>> RequestAsync(
        ApprovalEvaluationInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> GetRequestAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestPageDto>> ListRequestsAsync(
        ApprovalRequestQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> ApproveAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> RejectAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> CancelAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> EscalateAsync(
        int requestId,
        ApprovalDecisionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> ExpireAsync(
        int requestId,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalExecutionDto>> BeginExecutionAsync(
        int requestId,
        ApprovalExecutionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalExecutionDto>> CompleteExecutionAsync(
        int requestId,
        ApprovalCompletionInput input,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalInboxPageDto>> ListInboxAsync(
        ApprovalInboxQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ApprovalInboxItemDto>> MarkInboxItemReadAsync(
        int inboxItemId,
        CancellationToken cancellationToken = default);
}
