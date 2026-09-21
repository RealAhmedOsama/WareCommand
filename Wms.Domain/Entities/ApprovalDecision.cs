using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Immutable approval history. Updates and deletes are rejected by the
/// persistence boundary; a correction is represented by a later decision.
/// </summary>
public sealed class ApprovalDecision : Entity
{
    private ApprovalDecision()
    {
    }

    public ApprovalDecision(
        int approvalRequestId,
        int level,
        ApprovalDecisionType type,
        string idempotencyKey,
        string actorUserId,
        string actorRoleSnapshotJson,
        string? comment,
        DateTimeOffset occurredAtUtc)
    {
        if (approvalRequestId <= 0 || level <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvalRequestId));
        }

        ApprovalRequestId = approvalRequestId;
        Level = level;
        Type = type;
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        ActorUserId = Required(actorUserId, 450, nameof(actorUserId));
        ActorRoleSnapshotJson = Required(actorRoleSnapshotJson, 4_000, nameof(actorRoleSnapshotJson));
        Comment = Optional(comment, 2_000);
        OccurredAtUtc = occurredAtUtc.ToUniversalTime();
    }

    public int ApprovalRequestId { get; private set; }
    public int Level { get; private set; }
    public ApprovalDecisionType Type { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ActorUserId { get; private set; } = string.Empty;
    public string ActorRoleSnapshotJson { get; private set; } = string.Empty;
    public string? Comment { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

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
