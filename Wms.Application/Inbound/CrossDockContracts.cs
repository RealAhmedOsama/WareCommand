using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inbound;

public sealed record CrossDockPolicyInput(
    int WarehouseId,
    string PolicyKey,
    string Name,
    int Priority = 50,
    int? ItemId = null,
    string? ItemCategory = null,
    int? SupplierId = null,
    string? InboundSourceType = null,
    int? CustomerId = null,
    int? SalesOrderId = null,
    int? DestinationLocationId = null,
    decimal QuantityTolerancePercent = 0m,
    int MinimumShelfLifeDays = 0,
    bool RequireExpiry = false,
    bool AllowPlanned = true,
    bool AllowOpportunistic = true,
    DateTime? EffectiveFromUtc = null,
    DateTime? EffectiveToUtc = null);

public sealed record CrossDockPolicyDto(
    int Id,
    int WarehouseId,
    string PolicyKey,
    string Name,
    int Priority,
    int? ItemId,
    string? ItemCategory,
    int? SupplierId,
    string? InboundSourceType,
    int? CustomerId,
    int? SalesOrderId,
    int? DestinationLocationId,
    decimal QuantityTolerancePercent,
    int MinimumShelfLifeDays,
    bool RequireExpiry,
    bool AllowPlanned,
    bool AllowOpportunistic,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record CrossDockSimulationInput(
    int ReceiptLineId,
    CrossDockMode Mode = CrossDockMode.Opportunistic,
    int? PolicyId = null,
    DateTime? AsOfUtc = null);

public sealed record CrossDockPlanCreateInput(
    int ReceiptLineId,
    CrossDockMode Mode = CrossDockMode.Opportunistic,
    int? PolicyId = null,
    string? CreationKey = null,
    DateTime? AsOfUtc = null,
    string? Notes = null);

public sealed record CrossDockMatchDto(
    int SalesOrderId,
    int SalesOrderLineId,
    int SalesOrderLineNumber,
    string SalesOrderDocumentNumber,
    int CustomerId,
    int ItemId,
    string ItemSku,
    decimal DemandBaseQuantity,
    decimal AlreadyPlannedBaseQuantity,
    decimal MatchedBaseQuantity,
    string Reason);

public sealed record CrossDockSimulationDto(
    int ReceiptId,
    int ReceiptLineId,
    string ReceiptDocumentNumber,
    int WarehouseId,
    int ItemId,
    string ItemSku,
    decimal ReceivedBaseQuantity,
    decimal AcceptedBaseQuantity,
    decimal MatchedBaseQuantity,
    decimal FallbackBaseQuantity,
    int SourceLocationId,
    int? DestinationLocationId,
    int InventoryStatusId,
    CrossDockMode Mode,
    int? PolicyId,
    string? PolicyKey,
    string Decision,
    string Explanation,
    IReadOnlyList<CrossDockMatchDto> Matches);

public sealed record CrossDockPlanLineDto(
    int Id,
    int Sequence,
    int SalesOrderId,
    int SalesOrderLineId,
    int SalesOrderLineNumber,
    string SalesOrderDocumentNumber,
    int CustomerId,
    int ItemId,
    string ItemSku,
    decimal DemandBaseQuantity,
    decimal MatchedBaseQuantity,
    CrossDockPlanLineStatus Status,
    string? Reason);

public sealed record CrossDockPlanDto(
    int Id,
    string PlanNumber,
    string CreationKey,
    int WarehouseId,
    int ReceiptId,
    int ReceiptLineId,
    string ReceiptDocumentNumber,
    int ItemId,
    string ItemSku,
    int PolicyId,
    string PolicyKey,
    CrossDockMode Mode,
    decimal ReceivedBaseQuantity,
    decimal MatchedBaseQuantity,
    decimal FallbackBaseQuantity,
    int SourceLocationId,
    int? DestinationLocationId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    string? LotNumber,
    DateTime? ExpiryDate,
    string? SerialNumber,
    int? LicensePlateId,
    CrossDockPlanStatus Status,
    string Explanation,
    string CreatedByUserId,
    DateTime CreatedAt,
    string? CancelledByUserId,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    long Revision,
    IReadOnlyList<CrossDockPlanLineDto> Lines);

public interface ICrossDockService
{
    Task<Result<CrossDockPolicyDto>> SavePolicyAsync(
        int? policyId,
        CrossDockPolicyInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<CrossDockPolicyDto>>> ListPoliciesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<CrossDockSimulationDto>> SimulateAsync(
        CrossDockSimulationInput input,
        CancellationToken cancellationToken = default);

    Task<Result<CrossDockPlanDto>> CreatePlanAsync(
        CrossDockPlanCreateInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<CrossDockPlanDto>> GetAsync(
        int planId,
        CancellationToken cancellationToken = default);

    Task<Result<CrossDockPlanDto>> CancelAsync(
        int planId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);
}
