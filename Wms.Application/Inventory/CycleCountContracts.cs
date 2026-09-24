using Wms.Application.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.Inventory;

public sealed record CycleCountPlanInput(
    string PlanKey,
    int WarehouseId,
    int? LocationId,
    int? ItemId,
    string? ItemClass,
    int FrequencyDays,
    decimal ThresholdQuantity,
    bool Blind,
    CycleCountFreezePolicy FreezePolicy,
    DateTime NextDueAtUtc);

public sealed record CycleCountPlanQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record CycleCountGenerationQuery(
    int? WarehouseId = null,
    int? PlanId = null,
    int Limit = 200);

public sealed record CycleCountPlanDto(
    int Id,
    string PlanKey,
    int WarehouseId,
    int? LocationId,
    int? ItemId,
    string? ItemClass,
    int FrequencyDays,
    decimal ThresholdQuantity,
    bool Blind,
    CycleCountFreezePolicy FreezePolicy,
    DateTime NextDueAtUtc,
    bool IsActive,
    long Revision);

public sealed record CycleCountTaskSummaryDto(
    int TaskId,
    string TaskNumber,
    int PlanId,
    int WarehouseId,
    int? LocationId,
    int? WarehouseWorkId,
    bool Blind,
    DateTime SnapshotAtUtc,
    CycleCountTaskStatus Status,
    int LineCount,
    decimal? ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? VarianceQuantity);

public sealed record CycleCountGenerationResultDto(
    int PlansExamined,
    int TasksCreated,
    int TasksReused,
    int LinesCreated,
    IReadOnlyList<CycleCountTaskSummaryDto> Tasks);

public sealed record CycleCountTaskLineDto(
    int Id,
    int Sequence,
    int ItemId,
    int LocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    InventoryOwnerKind OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    bool EmptyLocationCandidate,
    decimal? ExpectedQuantity,
    decimal? CountedQuantity,
    decimal? VarianceQuantity,
    CycleCountLineStatus Status,
    long Revision);

public sealed record CycleCountTaskDto(
    int Id,
    string TaskNumber,
    int PlanId,
    int WarehouseId,
    int? LocationId,
    int? WarehouseWorkId,
    bool Blind,
    CycleCountFreezePolicy FreezePolicy,
    DateTime SnapshotAtUtc,
    CycleCountTaskStatus Status,
    string CreatedByUserId,
    string? StartedByUserId,
    DateTime? StartedAtUtc,
    DateTime? SubmittedAtUtc,
    string? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? ApprovalReason,
    long Revision,
    IReadOnlyList<CycleCountTaskLineDto> Lines);

public sealed record CycleCountTaskStartInput(long ExpectedRevision);

public sealed record CycleCountTaskApprovalInput(long ExpectedRevision, string Reason);

public sealed record CycleCountLineCountInput(int LineId, decimal CountedQuantity);

public interface ICycleCountService
{
    Task<Result<CycleCountPlanDto>> SavePlanAsync(
        int? planId,
        CycleCountPlanInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CycleCountPlanDto>>> SearchPlansAsync(
        CycleCountPlanQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CycleCountGenerationResultDto>> GenerateAsync(
        CycleCountGenerationQuery query,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<CycleCountTaskDto>> GetTaskAsync(
        int taskId,
        CancellationToken cancellationToken = default);

    Task<Result<CycleCountTaskDto>> StartTaskAsync(
        int taskId,
        CycleCountTaskStartInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<CycleCountTaskDto>> RecordCountsAsync(
        int taskId,
        IReadOnlyList<CycleCountLineCountInput> counts,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<CycleCountTaskDto>> ApproveTaskAsync(
        int taskId,
        CycleCountTaskApprovalInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
