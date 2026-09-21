using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record InventoryDispositionPolicyInput(
    int WarehouseId,
    string PolicyKey,
    string Name,
    int Priority,
    int? ItemId,
    string? ItemCategory,
    int WarningDays,
    int MinimumShelfLifeDays,
    bool RequireApprovalForScrap,
    bool RequireWitnessForDestruction,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null,
    bool IsActive = true);

public sealed record InventoryDispositionPolicyQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    string? ItemCategory = null,
    bool IncludeInactive = false);

public sealed record InventoryExpiryCandidateQuery(
    int WarehouseId,
    DateTime? AsOfUtc = null,
    int Limit = 500);

public sealed record InventoryDispositionCreateInput(
    string IdempotencyKey,
    int StockId,
    InventoryDispositionKind Kind,
    decimal Quantity,
    string Reason,
    string? ReferenceNumber = null,
    string? WitnessUserId = null);

public sealed record InventoryDispositionApprovalInput(string? Note = null);

public sealed record InventoryRecallCreateInput(
    string IdempotencyKey,
    int WarehouseId,
    int? ItemId,
    int? LotId,
    int? SerialNumberId,
    int? LicensePlateId,
    string Reason,
    string? ExternalReference = null);

public sealed record InventoryRecallCloseInput(string Reason);

public sealed record InventoryDispositionPolicyDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    string PolicyKey,
    string Name,
    int Priority,
    int? ItemId,
    string? ItemCategory,
    int WarningDays,
    int MinimumShelfLifeDays,
    bool RequireApprovalForScrap,
    bool RequireWitnessForDestruction,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record InventoryExpiryCandidateDto(
    int StockBalanceId,
    int WarehouseId,
    string WarehouseCode,
    int LocationId,
    string LocationCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    int LotId,
    string LotNumber,
    DateOnly ExpiryDate,
    DateOnly BusinessDate,
    int DaysUntilExpiry,
    decimal AvailableQuantity,
    int InventoryStatusId,
    string InventoryStatusCode,
    bool IsExpired,
    bool BelowMinimumShelfLife,
    int WarningDays,
    int MinimumShelfLifeDays,
    int? PolicyId);

public sealed record InventoryDispositionDto(
    int Id,
    string DispositionNumber,
    string IdempotencyKey,
    int WarehouseId,
    int StockId,
    int ItemId,
    int LocationId,
    int? LotId,
    int? SerialNumberId,
    int? LicensePlateId,
    int SourceInventoryStatusId,
    int TargetInventoryStatusId,
    string BaseUnitOfMeasure,
    InventoryDispositionKind Kind,
    InventoryDispositionStatus Status,
    decimal RequestedQuantity,
    decimal CompletedQuantity,
    string Reason,
    string RequestedByUserId,
    string? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? CompletedByUserId,
    DateTime? CompletedAtUtc,
    string? RejectionReason,
    string? FailureReason,
    string? ReferenceNumber,
    string? WitnessUserId,
    bool ApprovalRequired,
    DateTime RequestedAtUtc,
    int? DestinationStockId,
    int? OutboundMovementId,
    int? InboundMovementId,
    long Revision);

public sealed record InventoryRecallCaseDto(
    int Id,
    string CaseNumber,
    string IdempotencyKey,
    int WarehouseId,
    int? ItemId,
    int? LotId,
    int? SerialNumberId,
    int? LicensePlateId,
    string Reason,
    string CreatedByUserId,
    string? ExternalReference,
    DateTime CreatedAtUtc,
    InventoryRecallStatus Status,
    string? ClosedByUserId,
    DateTime? ClosedAtUtc,
    string? ClosureReason,
    long Revision);

public sealed record InventoryRecallTraceDto(
    string Source,
    int? InventoryTransactionId,
    int? StockBalanceId,
    int? ItemId,
    int? LocationId,
    int? LotId,
    int? SerialNumberId,
    int? LicensePlateId,
    int InventoryStatusId,
    string? InventoryStatusCode,
    decimal Quantity,
    string? ReferenceType,
    string? ReferenceId,
    InventoryTransactionType? TransactionType,
    DateTime? OccurredAtUtc);

public sealed record InventoryRecallTraceResultDto(
    InventoryRecallCaseDto Case,
    IReadOnlyList<InventoryRecallTraceDto> Trace);

public sealed record InventoryDispositionMetricsQuery(
    int WarehouseId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record InventoryDispositionMetricsDto(
    int WarehouseId,
    int TotalDispositions,
    decimal RequestedQuantity,
    decimal CompletedQuantity,
    int OpenRecallCases,
    IReadOnlyDictionary<InventoryDispositionKind, int> ByKind,
    IReadOnlyDictionary<InventoryDispositionStatus, int> ByStatus);

public interface IInventoryDispositionService
{
    Task<Result<InventoryDispositionPolicyDto>> SavePolicyAsync(
        int? policyId,
        InventoryDispositionPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryDispositionPolicyDto>>> SearchPoliciesAsync(
        InventoryDispositionPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryExpiryCandidateDto>>> GetExpiryCandidatesAsync(
        InventoryExpiryCandidateQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryDispositionDto>> CreateAsync(
        InventoryDispositionCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryDispositionDto>> ApproveAsync(
        int dispositionId,
        string actorUserId,
        InventoryDispositionApprovalInput? input = null,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryDispositionDto>> ExecuteAsync(
        int dispositionId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryRecallCaseDto>> CreateRecallAsync(
        InventoryRecallCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryRecallCaseDto>> CloseRecallAsync(
        int recallCaseId,
        string actorUserId,
        InventoryRecallCloseInput input,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryRecallTraceResultDto>> GetRecallTraceAsync(
        int recallCaseId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryDispositionMetricsDto>> GetMetricsAsync(
        InventoryDispositionMetricsQuery query,
        CancellationToken cancellationToken = default);
}
