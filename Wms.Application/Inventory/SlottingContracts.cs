using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record SlottingPolicyInput(
    int WarehouseId,
    string PolicyKey,
    string Name,
    int LookbackDays,
    decimal VelocityWeight,
    decimal TravelWeight,
    decimal SpaceWeight,
    decimal ReplenishmentWeight,
    decimal AffinityWeight,
    int MaxRecommendationsPerItem,
    int RecommendationExpiryDays,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null,
    string? AllowedLocationTypes = null);

public sealed record SlottingPolicyQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record SlottingAnalysisQuery(
    int? WarehouseId = null,
    int? PolicyId = null,
    int? ItemId = null,
    DateTime? AsOfUtc = null,
    int Limit = 500,
    bool DryRun = false);

public sealed record SlottingRecommendationQuery(
    int? WarehouseId = null,
    int? ItemId = null,
    SlottingRecommendationStatus? Status = null,
    bool IncludeHistorical = true,
    int Limit = 500);

public sealed record SlottingRejectionInput(string Reason);

public sealed record SlottingPolicyDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    string PolicyKey,
    string Name,
    int LookbackDays,
    decimal VelocityWeight,
    decimal TravelWeight,
    decimal SpaceWeight,
    decimal ReplenishmentWeight,
    decimal AffinityWeight,
    int MaxRecommendationsPerItem,
    int RecommendationExpiryDays,
    string AllowedLocationTypes,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    bool IsActive,
    long Revision);

public sealed record SlottingRecommendationDto(
    int Id,
    string RecommendationKey,
    int WarehouseId,
    string WarehouseCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    SlottingRecommendationKind Kind,
    int SourceLocationId,
    string SourceLocationCode,
    int TargetLocationId,
    string TargetLocationCode,
    decimal Quantity,
    string BaseUnitOfMeasure,
    decimal Score,
    decimal CurrentScore,
    decimal ExpectedTravelReduction,
    decimal ExpectedReplenishmentReduction,
    decimal ExpectedCongestionReduction,
    DateTime SourcePeriodFromUtc,
    DateTime SourcePeriodToUtc,
    string SourceDataVersion,
    string FactorSnapshotJson,
    string ConstraintSnapshotJson,
    string AnalysisRunKey,
    DateTime ExpiresAtUtc,
    SlottingRecommendationStatus Status,
    string? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? RejectedByUserId,
    DateTime? RejectedAtUtc,
    string? RejectionReason,
    int? WorkId,
    DateTime? WorkCreatedAtUtc,
    long Revision);

public sealed record SlottingAnalysisResultDto(
    int PoliciesExamined,
    int ItemsExamined,
    int RecommendationsCreated,
    int RecommendationsReused,
    int Blocked,
    bool DryRun,
    IReadOnlyList<SlottingRecommendationDto> Recommendations);

public interface ISlottingService
{
    Task<Result<SlottingPolicyDto>> SavePolicyAsync(
        int? policyId,
        SlottingPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SlottingPolicyDto>>> SearchPoliciesAsync(
        SlottingPolicyQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<SlottingAnalysisResultDto>> AnalyzeAsync(
        SlottingAnalysisQuery query,
        string actorUserId,
        bool internalExecution = false,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SlottingRecommendationDto>>> SearchRecommendationsAsync(
        SlottingRecommendationQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<SlottingRecommendationDto>> ApproveAsync(
        int recommendationId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<SlottingRecommendationDto>> RejectAsync(
        int recommendationId,
        SlottingRejectionInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
