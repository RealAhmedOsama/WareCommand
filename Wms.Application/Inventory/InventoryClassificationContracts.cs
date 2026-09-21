using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record InventoryClassificationPolicyInput(
    int WarehouseId,
    string PolicyKey,
    InventoryClassificationMethod Method,
    int LookbackDays,
    decimal AThresholdPercent,
    decimal BThresholdPercent,
    decimal MinimumActivityValue,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null);

public sealed record InventoryClassificationPolicyQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record InventoryClassificationQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    InventoryClassificationClass? Classification = null,
    int Limit = 500);

public sealed record InventoryClassificationHistoryQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    int Limit = 500);

public sealed record InventoryClassificationRecalculationQuery(
    int? WarehouseId = null,
    int? PolicyId = null,
    DateTime? AsOfUtc = null,
    int Limit = 500,
    bool DryRun = false);

public sealed record InventoryClassificationOverrideInput(
    int WarehouseId,
    int ItemId,
    InventoryClassificationClass Classification,
    string Reason,
    DateTime? ExpiresAtUtc = null);

public sealed record InventoryClassificationPolicyDto(
    int Id,
    string PolicyKey,
    int WarehouseId,
    string WarehouseCode,
    InventoryClassificationMethod Method,
    int LookbackDays,
    decimal AThresholdPercent,
    decimal BThresholdPercent,
    decimal MinimumActivityValue,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record InventoryClassificationDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    InventoryClassificationClass Classification,
    InventoryClassificationSource Source,
    decimal MetricValue,
    decimal CumulativePercent,
    decimal ShippedQuantity,
    int ShippedLineCount,
    decimal MovementQuantity,
    decimal InventoryValue,
    decimal CriticalityScore,
    DateTime LookbackFromUtc,
    DateTime LookbackToUtc,
    int? PolicyId,
    long PolicyRevision,
    string CalculationInputVersion,
    DateTime CalculatedAtUtc,
    string? ManualOverrideReason,
    DateTime? ManualOverrideExpiresAtUtc,
    long Revision);

public sealed record InventoryClassificationHistoryDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    InventoryClassificationClass? PreviousClassification,
    InventoryClassificationClass Classification,
    InventoryClassificationSource Source,
    decimal MetricValue,
    decimal CumulativePercent,
    decimal ShippedQuantity,
    int ShippedLineCount,
    decimal MovementQuantity,
    decimal InventoryValue,
    decimal CriticalityScore,
    DateTime LookbackFromUtc,
    DateTime LookbackToUtc,
    int? PolicyId,
    long PolicyRevision,
    string CalculationInputVersion,
    string CalculationRunKey,
    string Reason,
    DateTime ChangedAtUtc,
    DateTime? ManualOverrideExpiresAtUtc);

public sealed record InventoryClassificationComparisonDto(
    int WarehouseId,
    int ItemId,
    string ItemSku,
    string CurrentClassification,
    string ProposedClassification,
    decimal MetricValue,
    decimal CumulativePercent,
    bool ManualOverrideSkipped,
    string? ManualOverrideReason);

public sealed record InventoryClassificationRecalculationResultDto(
    int PoliciesExamined,
    int ItemsExamined,
    int ClassificationsChanged,
    int ManualOverridesSkipped,
    bool DryRun,
    IReadOnlyList<InventoryClassificationComparisonDto> Comparisons);

public interface IInventoryClassificationService
{
    Task<Result<InventoryClassificationPolicyDto>> SavePolicyAsync(
        int? policyId,
        InventoryClassificationPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryClassificationPolicyDto>>> SearchPoliciesAsync(
        InventoryClassificationPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryClassificationDto>>> SearchAsync(
        InventoryClassificationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryClassificationHistoryDto>>> SearchHistoryAsync(
        InventoryClassificationHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryClassificationRecalculationResultDto>> RecalculateAsync(
        InventoryClassificationRecalculationQuery query,
        string actorUserId,
        bool internalExecution = false,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryClassificationDto>> OverrideAsync(
        InventoryClassificationOverrideInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
