using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable recipient-specific approval inbox item. It is intentionally separate
/// from background-job notifications so it can be acknowledged and resolved by
/// approval state.
/// </summary>
public sealed class ApprovalInboxItem : Entity
{
    private ApprovalInboxItem()
    {
    }

    public ApprovalInboxItem(
        int approvalRequestId,
        string recipientUserId,
        int level,
        string deduplicationKey,
        string title,
        string message,
        int? warehouseId,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (approvalRequestId <= 0 || level <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvalRequestId));
        }

        ApprovalRequestId = approvalRequestId;
        RecipientUserId = Required(recipientUserId, 450, nameof(recipientUserId));
        Level = level;
        DeduplicationKey = Required(deduplicationKey, 250, nameof(deduplicationKey));
        Title = Required(title, 200, nameof(title));
        Message = Required(message, 2_000, nameof(message));
        WarehouseId = warehouseId;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    public int ApprovalRequestId { get; private set; }
    public string RecipientUserId { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public string DeduplicationKey { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int? WarehouseId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public void MarkRead(DateTimeOffset readAtUtc)
    {
        ReadAtUtc ??= readAtUtc.ToUniversalTime();
        SetUpdatedAt(readAtUtc.UtcDateTime);
    }

    public void Resolve(DateTimeOffset resolvedAtUtc)
    {
        ResolvedAtUtc ??= resolvedAtUtc.ToUniversalTime();
        SetUpdatedAt(resolvedAtUtc.UtcDateTime);
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
}
