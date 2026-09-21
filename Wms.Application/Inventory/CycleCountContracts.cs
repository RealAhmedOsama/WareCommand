using Wms.Application.Common;
using Wms.Domain.Enums;

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
}
