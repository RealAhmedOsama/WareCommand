using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Persisted, bounded advisory proposal. It records no prompt or raw provider
/// payload and has no inventory mutation capability.
/// </summary>
public sealed class GovernedRecommendation : Entity
{
    private GovernedRecommendation()
    {
    }

    public GovernedRecommendation(
        string recommendationId,
        int type,
        int warehouseId,
        string sourceSnapshotId,
        DateTimeOffset sourceFromUtc,
        DateTimeOffset sourceToUtc,
        string sourceStateFingerprint,
        string providerName,
        string modelVersion,
        string deterministicBaselineVersion,
        decimal confidence,
        string explanation,
        string actionJson,
        string numericFeaturesJson,
        decimal expectedImpactLow,
        decimal expectedImpactHigh,
        DateTimeOffset generatedAtUtc,
        DateTimeOffset expiresAtUtc,
        bool shadowMode,
        string? shadowComparison,
        string createdByUserId)
    {
        RecommendationId = Required(recommendationId, 64, nameof(recommendationId));
        if (type < 0 || type > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        Type = type;
        WarehouseId = warehouseId;
        SourceSnapshotId = Required(sourceSnapshotId, 200, nameof(sourceSnapshotId));
        SourceFromUtc = sourceFromUtc.ToUniversalTime();
        SourceToUtc = sourceToUtc.ToUniversalTime();
        if (SourceToUtc < SourceFromUtc)
        {
            throw new ArgumentException("The source window is invalid.", nameof(sourceToUtc));
        }

        SourceStateFingerprint = Required(sourceStateFingerprint, 128, nameof(sourceStateFingerprint));
        ProviderName = Required(providerName, 100, nameof(providerName));
        ModelVersion = Required(modelVersion, 100, nameof(modelVersion));
        DeterministicBaselineVersion = Required(deterministicBaselineVersion, 100, nameof(deterministicBaselineVersion));
        Confidence = confidence;
        Explanation = Required(explanation, 2_000, nameof(explanation));
        ActionJson = Required(actionJson, 2_000, nameof(actionJson));
        NumericFeaturesJson = Required(numericFeaturesJson, 8_000, nameof(numericFeaturesJson));
        ExpectedImpactLow = expectedImpactLow;
        ExpectedImpactHigh = expectedImpactHigh;
        GeneratedAtUtc = generatedAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        ShadowMode = shadowMode;
        ShadowComparison = Optional(shadowComparison, 80);
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Status = 0;
        RequiresRevalidation = true;
        CanMutateInventory = false;
        Revision = 1;
    }

    public string RecommendationId { get; private set; } = string.Empty;
    public int Type { get; private set; }
    public int WarehouseId { get; private set; }
    public string SourceSnapshotId { get; private set; } = string.Empty;
    public DateTimeOffset SourceFromUtc { get; private set; }
    public DateTimeOffset SourceToUtc { get; private set; }
    public string SourceStateFingerprint { get; private set; } = string.Empty;
    public string ProviderName { get; private set; } = string.Empty;
    public string ModelVersion { get; private set; } = string.Empty;
    public string DeterministicBaselineVersion { get; private set; } = string.Empty;
    public decimal Confidence { get; private set; }
    public string Explanation { get; private set; } = string.Empty;
    public string ActionJson { get; private set; } = string.Empty;
    public string NumericFeaturesJson { get; private set; } = string.Empty;
    public decimal ExpectedImpactLow { get; private set; }
    public decimal ExpectedImpactHigh { get; private set; }
    public DateTimeOffset GeneratedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool ShadowMode { get; private set; }
    public string? ShadowComparison { get; private set; }
    public int Status { get; private set; }
    public bool RequiresRevalidation { get; private set; }
    public bool CanMutateInventory { get; private set; }
    public string? LastDispositionComment { get; private set; }
    public string? ExecutionReference { get; private set; }
    public string? LastExecutionErrorCode { get; private set; }
    public int ExecutionAttempts { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public long Revision { get; private set; }

    public void Transition(
        int status,
        string? comment,
        DateTimeOffset nowUtc,
        string? executionReference = null,
        string? executionErrorCode = null)
    {
        if (status < 0 || status > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        LastDispositionComment = Optional(comment, 1_000);
        ExecutionReference = Optional(executionReference, 250) ?? ExecutionReference;
        LastExecutionErrorCode = Optional(executionErrorCode, 100);
        if (status is 5 or 7)
        {
            ExecutionAttempts++;
        }

        Revision++;
        SetUpdatedAt(nowUtc.UtcDateTime);
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
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }
}

/// <summary>Immutable review, decision, and execution history.</summary>
public sealed class GovernedRecommendationEvent : Entity
{
    private GovernedRecommendationEvent()
    {
    }

    public GovernedRecommendationEvent(
        int recommendationId,
        string idempotencyKey,
        string eventType,
        string actorUserId,
        DateTimeOffset occurredAtUtc,
        long resultingRevision,
        string? comment = null,
        string? stateFingerprint = null,
        string? commandReference = null,
        string? outcomeCode = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recommendationId);
        RecommendationId = recommendationId;
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        EventType = Required(eventType, 40, nameof(eventType));
        ActorUserId = Required(actorUserId, 450, nameof(actorUserId));
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
        ResultingRevision = resultingRevision;
        Comment = Optional(comment, 1_000);
        StateFingerprint = Optional(stateFingerprint, 128);
        CommandReference = Optional(commandReference, 250);
        OutcomeCode = Optional(outcomeCode, 100);
    }

    public int RecommendationId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public long ResultingRevision { get; private set; }
    public string? Comment { get; private set; }
    public string? StateFingerprint { get; private set; }
    public string? CommandReference { get; private set; }
    public string? OutcomeCode { get; private set; }
    public GovernedRecommendation Recommendation { get; private set; } = null!;

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }
}
