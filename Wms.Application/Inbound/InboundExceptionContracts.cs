using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inbound;

public sealed record InboundExceptionQuery(
    int? WarehouseId = null,
    InboundExceptionCode? Code = null,
    InboundExceptionSeverity? Severity = null,
    InboundExceptionStatus? Status = null,
    string? QueueCode = null,
    string? OwnerUserId = null,
    int? ReceiptId = null,
    int? ReceivingSessionId = null,
    string? SearchTerm = null,
    bool IncludeClosed = false,
    bool OverdueOnly = false,
    DateTime? AsOfUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record InboundExceptionInput(
    int WarehouseId,
    InboundExceptionCode Code,
    InboundExceptionSeverity Severity,
    string Reason,
    string IdempotencyKey,
    string? QueueCode = null,
    DateTime? DueAtUtc = null,
    int? AdvanceShippingNoticeId = null,
    int? AdvanceShippingNoticeLineId = null,
    int? ReceiptId = null,
    int? ReceiptLineId = null,
    int? ReceivingSessionId = null,
    int? LicensePlateId = null,
    int? WarehouseWorkId = null,
    int? ItemId = null,
    int? StagingLocationId = null,
    decimal? ExpectedBaseQuantity = null,
    decimal? ActualBaseQuantity = null,
    decimal? VarianceBaseQuantity = null,
    string? ItemSkuSnapshot = null,
    string? Notes = null,
    string? AttachmentReferences = null);

public sealed record InboundExceptionAssignmentInput(
    string? OwnerUserId,
    string? TeamCode);

public sealed record InboundExceptionResolutionInput(
    InboundExceptionResolution Resolution,
    string Reason,
    bool SupervisorOverride = false,
    string? OverrideReason = null);

public sealed record InboundExceptionDto(
    int Id,
    int WarehouseId,
    string ExceptionNumber,
    string IdempotencyKey,
    InboundExceptionCode Code,
    InboundExceptionSeverity Severity,
    InboundExceptionStatus Status,
    string Reason,
    string QueueCode,
    DateTime? DueAtUtc,
    int? AdvanceShippingNoticeId,
    int? AdvanceShippingNoticeLineId,
    int? ReceiptId,
    int? ReceiptLineId,
    int? ReceivingSessionId,
    int? LicensePlateId,
    int? WarehouseWorkId,
    int? ItemId,
    int? StagingLocationId,
    decimal? ExpectedBaseQuantity,
    decimal? ActualBaseQuantity,
    decimal? VarianceBaseQuantity,
    string? ItemSkuSnapshot,
    string? Notes,
    string? AttachmentReferences,
    string CreatedByUserId,
    string? OwnerUserId,
    string? AssignedTeamCode,
    string? AssignedByUserId,
    DateTime? AssignedAtUtc,
    string? ReviewStartedByUserId,
    DateTime? ReviewStartedAtUtc,
    InboundExceptionResolution? Resolution,
    string? ResolutionReason,
    string? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    long Revision,
    bool IsTerminal,
    bool IsOverdue);

public sealed record InboundExceptionPageDto(
    IReadOnlyList<InboundExceptionDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int OpenCount,
    int OverdueCount);

public interface IInboundExceptionService
{
    Task<Result<InboundExceptionPageDto>> ListAsync(
        InboundExceptionQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<InboundExceptionDto>> GetAsync(
        int exceptionId,
        CancellationToken cancellationToken = default);

    Task<Result<InboundExceptionDto>> CreateAsync(
        InboundExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InboundExceptionDto>> AssignAsync(
        int exceptionId,
        InboundExceptionAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InboundExceptionDto>> StartReviewAsync(
        int exceptionId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InboundExceptionDto>> ResolveAsync(
        int exceptionId,
        InboundExceptionResolutionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
