using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// One execution lease per approval request. The unique request key makes an
/// approved protected command exactly-once at the shared gate.
/// </summary>
public sealed class ApprovalExecution : Entity
{
    private ApprovalExecution()
    {
    }

    public ApprovalExecution(
        int approvalRequestId,
        string idempotencyKey,
        string currentStateHash,
        string startedByUserId,
        DateTimeOffset startedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(approvalRequestId);

        ApprovalRequestId = approvalRequestId;
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        CurrentStateHash = Required(currentStateHash, 128, nameof(currentStateHash));
        StartedByUserId = Required(startedByUserId, 450, nameof(startedByUserId));
        StartedAtUtc = startedAtUtc.ToUniversalTime();
        Status = ApprovalExecutionStatus.Started;
    }

    public int ApprovalRequestId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CurrentStateHash { get; private set; } = string.Empty;
    public string StartedByUserId { get; private set; } = string.Empty;
    public ApprovalExecutionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? ResultReference { get; private set; }

    public void Complete(string? resultReference, DateTimeOffset completedAtUtc)
    {
        if (Status != ApprovalExecutionStatus.Started)
        {
            throw new InvalidOperationException("Only a started approval execution can be completed.");
        }

        Status = ApprovalExecutionStatus.Completed;
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        ResultReference = Optional(resultReference, 250);
        SetUpdatedAt(completedAtUtc.UtcDateTime);
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
