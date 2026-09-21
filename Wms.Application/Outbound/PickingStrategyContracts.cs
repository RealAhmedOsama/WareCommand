using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Outbound;

public sealed record PickingStrategyPolicyInput(
    int WarehouseId,
    string PolicyKey,
    string Name,
    PickingStrategyKind Strategy = PickingStrategyKind.SingleOrder,
    int Priority = 50,
    int MaxOrders = 100,
    int MaxContainers = 100,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? WaveTemplateId = null,
    string? OrderProfileCode = null,
    int? ItemId = null,
    int? LocationZoneId = null,
    string? PackageProfileCode = null,
    string? SequenceMode = null,
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveToUtc = null);

public sealed record PickingStrategyPolicyQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false,
    PickingStrategyKind? Strategy = null);

public sealed record PickingStrategyPolicyDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    string PolicyKey,
    string Name,
    PickingStrategyKind Strategy,
    int Priority,
    int MaxOrders,
    int MaxContainers,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCubicMeters,
    int? WaveTemplateId,
    string? OrderProfileCode,
    int? ItemId,
    int? LocationZoneId,
    string? PackageProfileCode,
    string SequenceMode,
    DateTime? EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record PickingPlanCreateInput(
    int WarehouseId,
    string CreationKey,
    int? WaveId = null,
    int? PolicyId = null,
    PickingStrategyKind? Strategy = null,
    int MaxOrders = 100,
    int MaxContainers = 100,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    string? Reason = null);

public sealed record PickingPlanQuery(
    int? WarehouseId = null,
    int? WaveId = null,
    PickingStrategyKind? Strategy = null,
    PickingPlanStatus? Status = null,
    int Page = 1,
    int PageSize = 50);

public sealed record PickingContainerScanInput(
    string ScanCode,
    string IdempotencyKey);

public sealed record PickingHandoffInput(
    string ContainerScanCode,
    string IdempotencyKey);

public sealed record PickingPlanLineDto(
    int Id,
    int Sequence,
    int WarehouseWorkId,
    int WarehouseWorkLineId,
    int SalesOrderId,
    int SalesOrderLineId,
    string OrderNumber,
    int ItemId,
    string ItemSku,
    decimal PlannedQuantity,
    decimal PickedQuantity,
    string BaseUnitOfMeasure,
    int? SourceLocationId,
    int? ZoneLocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? SourceLicensePlateId,
    int? InventoryStatusId,
    int? ReservationId,
    int? ReservationAllocationId,
    string? BatchKey,
    int? ContainerId,
    int? HandoffId,
    int ZoneSequence,
    int ContainerSequence,
    PickingPlanLineStatus Status,
    string? LastError,
    long Revision);

public sealed record PickingPlanContainerDto(
    int Id,
    int Sequence,
    string ContainerKey,
    string ExpectedScanCode,
    bool TargetScanRequired,
    int? SalesOrderId,
    int? ZoneLocationId,
    int? TargetLicensePlateId,
    PickingContainerStatus Status,
    string? ScannedByUserId,
    DateTime? ScannedAtUtc,
    int LineCount,
    long Revision);

public sealed record PickingPlanHandoffDto(
    int Id,
    int Sequence,
    int ContainerId,
    int FromZoneLocationId,
    int? ToZoneLocationId,
    string ExpectedContainerScanCode,
    PickingHandoffStatus Status,
    string? CompletedByUserId,
    DateTime? CompletedAtUtc,
    long Revision);

public sealed record PickingPlanDto(
    int Id,
    int WarehouseId,
    string PlanNumber,
    string CreationKey,
    PickingStrategyKind Strategy,
    int? WaveId,
    int? PolicyId,
    int MaxOrders,
    int MaxContainers,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCubicMeters,
    PickingPlanStatus Status,
    string? LastError,
    long Revision,
    IReadOnlyList<PickingPlanLineDto> Lines,
    IReadOnlyList<PickingPlanContainerDto> Containers,
    IReadOnlyList<PickingPlanHandoffDto> Handoffs,
    int OrderCount,
    decimal PlannedQuantity,
    decimal PickedQuantity,
    decimal PlannedWeightKg,
    decimal PlannedVolumeCubicMeters);

public sealed record PickingPlanPageDto(
    IReadOnlyList<PickingPlanDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public interface IPickingStrategyService
{
    Task<Result<PickingStrategyPolicyDto>> SavePolicyAsync(
        int? policyId,
        PickingStrategyPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PickingStrategyPolicyDto>>> SearchPoliciesAsync(
        PickingStrategyPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> CreatePlanAsync(
        PickingPlanCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> SimulateAsync(
        PickingPlanCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanPageDto>> SearchAsync(
        PickingPlanQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> GetAsync(
        int planId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> ScanContainerAsync(
        int planId,
        int containerId,
        PickingContainerScanInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> CompleteHandoffAsync(
        int planId,
        int handoffId,
        PickingHandoffInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> RefreshAsync(
        int planId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<PickingPlanDto>> CancelAsync(
        int planId,
        string idempotencyKey,
        string reason,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
