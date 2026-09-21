using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Historical, explainable slotting result. Approval creates warehouse work;
/// this aggregate never moves stock directly.
/// </summary>
public sealed class SlottingRecommendation : Entity
{
    private SlottingRecommendation()
    {
    }

    public SlottingRecommendation(
        string recommendationKey,
        int warehouseId,
        int itemId,
        SlottingRecommendationKind kind,
        int sourceLocationId,
        int targetLocationId,
        decimal quantity,
        string baseUnitOfMeasure,
        decimal score,
        decimal currentScore,
        decimal expectedTravelReduction,
        decimal expectedReplenishmentReduction,
        decimal expectedCongestionReduction,
        DateTime sourcePeriodFromUtc,
        DateTime sourcePeriodToUtc,
        string sourceDataVersion,
        string factorSnapshotJson,
        string constraintSnapshotJson,
        string analysisRunKey,
        DateTime expiresAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetLocationId);
        if (sourceLocationId == targetLocationId)
        {
            throw new ArgumentException("A slotting recommendation must change the location.", nameof(targetLocationId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);

        RecommendationKey = Required(recommendationKey, 300, nameof(recommendationKey));
        WarehouseId = warehouseId;
        ItemId = itemId;
        Kind = kind;
        SourceLocationId = sourceLocationId;
        TargetLocationId = targetLocationId;
        Quantity = quantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        Score = score;
        CurrentScore = currentScore;
        ExpectedTravelReduction = Math.Max(0m, expectedTravelReduction);
        ExpectedReplenishmentReduction = Math.Max(0m, expectedReplenishmentReduction);
        ExpectedCongestionReduction = Math.Max(0m, expectedCongestionReduction);
        SourcePeriodFromUtc = NormalizeUtc(sourcePeriodFromUtc);
        SourcePeriodToUtc = NormalizeUtc(sourcePeriodToUtc);
        if (SourcePeriodToUtc <= SourcePeriodFromUtc)
        {
            throw new ArgumentException("The source period must have a positive duration.", nameof(sourcePeriodToUtc));
        }

        SourceDataVersion = Required(sourceDataVersion, 80, nameof(sourceDataVersion));
        FactorSnapshotJson = Required(factorSnapshotJson, 8_000, nameof(factorSnapshotJson));
        ConstraintSnapshotJson = Required(constraintSnapshotJson, 8_000, nameof(constraintSnapshotJson));
        AnalysisRunKey = Required(analysisRunKey, 250, nameof(analysisRunKey));
        ExpiresAtUtc = NormalizeUtc(expiresAtUtc);
        if (ExpiresAtUtc <= SourcePeriodToUtc)
        {
            throw new ArgumentException("A recommendation must expire after its source period.", nameof(expiresAtUtc));
        }

        Status = SlottingRecommendationStatus.Pending;
        Revision = 1;
    }

    public string RecommendationKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public SlottingRecommendationKind Kind { get; private set; }
    public int SourceLocationId { get; private set; }
    public int TargetLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal Score { get; private set; }
    public decimal CurrentScore { get; private set; }
    public decimal ExpectedTravelReduction { get; private set; }
    public decimal ExpectedReplenishmentReduction { get; private set; }
    public decimal ExpectedCongestionReduction { get; private set; }
    public DateTime SourcePeriodFromUtc { get; private set; }
    public DateTime SourcePeriodToUtc { get; private set; }
    public string SourceDataVersion { get; private set; } = string.Empty;
    public string FactorSnapshotJson { get; private set; } = string.Empty;
    public string ConstraintSnapshotJson { get; private set; } = string.Empty;
    public string AnalysisRunKey { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public SlottingRecommendationStatus Status { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? RejectedByUserId { get; private set; }
    public DateTime? RejectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public int? WorkId { get; private set; }
    public DateTime? WorkCreatedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public Location TargetLocation { get; private set; } = null!;
    public WarehouseWork? Work { get; private set; }

    public void Approve(string actorUserId, DateTime approvedAtUtc)
    {
        EnsureActor(actorUserId);
        if (Status == SlottingRecommendationStatus.WorkCreated)
        {
            return;
        }

        if (Status != SlottingRecommendationStatus.Pending)
        {
            throw new InvalidOperationException($"A recommendation in {Status} cannot be approved.");
        }

        ApprovedByUserId = actorUserId.Trim();
        ApprovedAtUtc = NormalizeUtc(approvedAtUtc);
        Status = SlottingRecommendationStatus.Approved;
        Revision++;
        SetUpdatedAt(approvedAtUtc);
    }

    public void Reject(string actorUserId, string reason, DateTime rejectedAtUtc)
    {
        EnsureActor(actorUserId);
        if (Status is SlottingRecommendationStatus.WorkCreated or SlottingRecommendationStatus.Approved)
        {
            throw new InvalidOperationException($"A recommendation in {Status} cannot be rejected.");
        }

        RejectionReason = Required(reason, 1_000, nameof(reason));
        RejectedByUserId = actorUserId.Trim();
        RejectedAtUtc = NormalizeUtc(rejectedAtUtc);
        Status = SlottingRecommendationStatus.Rejected;
        Revision++;
        SetUpdatedAt(rejectedAtUtc);
    }

    public void Expire(DateTime expiredAtUtc)
    {
        if (Status == SlottingRecommendationStatus.Pending && NormalizeUtc(expiredAtUtc) >= ExpiresAtUtc)
        {
            Status = SlottingRecommendationStatus.Expired;
            Revision++;
            SetUpdatedAt(expiredAtUtc);
        }
    }

    public void MarkWorkCreated(int workId, DateTime createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workId);
        if (Status != SlottingRecommendationStatus.Approved && Status != SlottingRecommendationStatus.WorkCreated)
        {
            throw new InvalidOperationException($"A recommendation in {Status} cannot be linked to work.");
        }

        WorkId = workId;
        WorkCreatedAtUtc = NormalizeUtc(createdAtUtc);
        Status = SlottingRecommendationStatus.WorkCreated;
        Revision++;
        SetUpdatedAt(createdAtUtc);
    }

    private static void EnsureActor(string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
