using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record InventoryReplenishmentPolicyInput(
    int ItemId,
    int WarehouseId,
    int? LocationId,
    decimal MinimumQuantity,
    decimal MaximumQuantity,
    decimal SafetyStockQuantity,
    decimal ReorderPointQuantity,
    decimal TargetQuantity,
    InventoryPolicyQuantityBasis QuantityBasis,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null,
    string? PreferredSource = null,
    int? LeadTimeDays = null);

public sealed record InventoryReplenishmentPolicyQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    int? LocationId = null,
    bool IncludeInactive = false);

public sealed record InventoryReplenishmentSignalQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    int? LocationId = null,
    int Limit = 200);

public sealed record InventoryReplenishmentPolicyDto(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    int WarehouseId,
    string WarehouseCode,
    int? LocationId,
    string? LocationCode,
    decimal MinimumQuantity,
    decimal MaximumQuantity,
    decimal SafetyStockQuantity,
    decimal ReorderPointQuantity,
    decimal TargetQuantity,
    InventoryPolicyQuantityBasis QuantityBasis,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    string? PreferredSource,
    int? LeadTimeDays,
    bool IsActive,
    long Revision);

public sealed record InventoryReplenishmentSignalDto(
    int PolicyId,
    int ItemId,
    string ItemSku,
    string ItemName,
    int WarehouseId,
    string WarehouseCode,
    int? LocationId,
    string? LocationCode,
    InventoryPolicyQuantityBasis QuantityBasis,
    decimal EvaluatedQuantity,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal PhysicalAvailableQuantity,
    decimal AvailableToPromiseQuantity,
    decimal InboundQuantity,
    decimal OrderedQuantity,
    decimal MinimumQuantity,
    decimal SafetyStockQuantity,
    decimal ReorderPointQuantity,
    decimal TargetQuantity,
    decimal MaximumQuantity,
    InventoryReplenishmentSignalKind SignalKind,
    decimal ShortfallQuantity,
    DateTime EvaluatedAtUtc,
    string? ClassificationClass = null);

public interface IInventoryReplenishmentPolicyService
{
    Task<Result<InventoryReplenishmentPolicyDto>> SaveAsync(
        int? policyId,
        InventoryReplenishmentPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryReplenishmentPolicyDto>>> SearchAsync(
        InventoryReplenishmentPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryReplenishmentSignalDto>>> GetSignalsAsync(
        InventoryReplenishmentSignalQuery query,
        CancellationToken cancellationToken = default);
}
