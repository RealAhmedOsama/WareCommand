using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.Inventory;

public sealed record InventoryAllocationStrategyPolicyInput(
    int WarehouseId,
    string PolicyKey,
    InventoryAllocationStrategyKind Strategy,
    DateTime EffectiveFromUtc,
    int? ItemId = null,
    string? ItemCategory = null,
    string? DemandType = null,
    int? FixedLocationId = null,
    bool PreferWholeLicensePlate = false,
    int MinimumShelfLifeDays = 0,
    InventoryAllocationMissingExpiryFallback MissingExpiryFallback = InventoryAllocationMissingExpiryFallback.Last,
    DateTime? EffectiveToUtc = null);

public sealed record InventoryAllocationStrategyPolicyQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    string? ItemCategory = null,
    string? DemandType = null,
    bool IncludeInactive = false);

public sealed record InventoryAllocationStrategyPolicyDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    string PolicyKey,
    int? ItemId,
    string? ItemSku,
    string? ItemCategory,
    string? DemandType,
    InventoryAllocationStrategyKind Strategy,
    int? FixedLocationId,
    string? FixedLocationCode,
    bool PreferWholeLicensePlate,
    int MinimumShelfLifeDays,
    InventoryAllocationMissingExpiryFallback MissingExpiryFallback,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record InventoryAllocationStrategySnapshot(
    int? PolicyId,
    string StrategyKey,
    InventoryAllocationStrategyKind Strategy,
    long Revision,
    int? FixedLocationId,
    bool PreferWholeLicensePlate,
    int MinimumShelfLifeDays,
    InventoryAllocationMissingExpiryFallback MissingExpiryFallback);

public sealed record InventoryAllocationStrategyResolution(
    InventoryAllocationStrategySnapshot Snapshot,
    IReadOnlyList<InventoryBalance> OrderedCandidates,
    IReadOnlyDictionary<int, string> RejectedReasons,
    IReadOnlyDictionary<int, string> SelectionReasons);

public sealed record InventoryAllocationSimulationInput(
    int WarehouseId,
    int ItemId,
    decimal RequestedQuantity,
    string DemandType = "AllocationSimulation",
    string DemandId = "preview",
    int? DemandLine = null,
    InventoryReservationSelector? Selector = null,
    DateTime? AsOfUtc = null);

public sealed record InventoryAllocationCandidateDto(
    int BalanceId,
    int LocationId,
    int? LotId,
    int? LicensePlateId,
    decimal AvailableQuantity,
    DateTime? ExpiryDate,
    DateTime ReceiptDateUtc,
    bool Selected,
    string Decision,
    string Explanation,
    int? Rank);

public sealed record InventoryAllocationSimulationResultDto(
    int WarehouseId,
    int ItemId,
    decimal RequestedQuantity,
    decimal ProposedAllocatedQuantity,
    decimal BackorderQuantity,
    InventoryAllocationStrategySnapshot Snapshot,
    IReadOnlyList<InventoryAllocationCandidateDto> Candidates);

public interface IInventoryAllocationStrategyService
{
    Task<Result<InventoryAllocationStrategyPolicyDto>> SaveAsync(
        int? policyId,
        InventoryAllocationStrategyPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryAllocationStrategyPolicyDto>>> SearchAsync(
        InventoryAllocationStrategyPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<InventoryAllocationStrategyResolution> ResolveAsync(
        InventoryReservationRequest request,
        Item item,
        Warehouse warehouse,
        IReadOnlyList<InventoryBalance> candidates,
        DateOnly businessDate,
        InventoryAllocationStrategySnapshot? pinnedSnapshot = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryAllocationSimulationResultDto>> SimulateAsync(
        InventoryAllocationSimulationInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
