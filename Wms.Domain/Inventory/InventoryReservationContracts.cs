using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Inventory;

public sealed record InventoryReservationSelector(
    int? LocationId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int? InventoryStatusId = null,
    string? BaseUnitOfMeasure = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record InventoryReservationRequest(
    string DemandType,
    string DemandId,
    int? DemandLine,
    int WarehouseId,
    int ItemId,
    decimal RequestedQuantity,
    InventoryReservationMode Mode = InventoryReservationMode.Hard,
    int Priority = 0,
    DateTime? ExpiresAtUtc = null,
    InventoryReservationSelector? Selector = null,
    string ActorUserId = "system",
    string? CorrelationId = null,
    string? Reason = null);

public sealed record InventoryReservationMutationRequest(
    int ReservationId,
    decimal? Quantity,
    string ActorUserId,
    string? CorrelationId = null,
    string? Reason = null,
    int? AllocationId = null);

public sealed record InventoryReservationResult(
    int ReservationId,
    string DemandType,
    string DemandId,
    int? DemandLine,
    int WarehouseId,
    int ItemId,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal ConsumedQuantity,
    decimal ReleasedQuantity,
    decimal ActiveQuantity,
    decimal BackorderQuantity,
    InventoryReservationMode Mode,
    InventoryReservationStatus Status,
    DateTime? ExpiresAtUtc,
    IReadOnlyList<InventoryReservationAllocationResult> Allocations,
    IReadOnlyList<InventoryReservationEventResult> Events,
    int? AllocationStrategyPolicyId = null,
    string? AllocationStrategyKey = null,
    InventoryAllocationStrategyKind? AllocationStrategy = null,
    long? AllocationStrategyRevision = null);

public sealed record InventoryReservationCandidateResult(
    int BalanceId,
    int WarehouseId,
    int LocationId,
    int ItemId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ProposedQuantity,
    bool Selected,
    string Decision,
    string Reason,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record InventoryReservationSimulationResult(
    string DemandType,
    string DemandId,
    int? DemandLine,
    int WarehouseId,
    int ItemId,
    decimal RequestedQuantity,
    decimal ProposedAllocatedQuantity,
    decimal BackorderQuantity,
    InventoryReservationStatus ProjectedStatus,
    string Explanation,
    IReadOnlyList<InventoryReservationCandidateResult> Candidates);

public sealed record InventoryReservationAllocationResult(
    int AllocationId,
    int WarehouseId,
    int LocationId,
    int ItemId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    decimal AllocatedQuantity,
    decimal ConsumedQuantity,
    decimal ReleasedQuantity,
    decimal RemainingQuantity,
    InventoryReservationAllocationStatus Status,
    string? Reason,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record InventoryReservationEventResult(
    int EventId,
    int? AllocationId,
    InventoryReservationEventType Type,
    decimal Quantity,
    string ActorUserId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? Reason);
