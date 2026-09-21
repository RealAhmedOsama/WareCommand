using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class QualityInspectionDisposition : Entity
{
    private QualityInspectionDisposition()
    {
    }

    public QualityInspectionDisposition(
        QualityDispositionType type,
        decimal quantity,
        int targetInventoryStatusId,
        string reason,
        string userId,
        DateTime recordedAtUtc,
        string? referenceNumber = null,
        int? movementId = null,
        int? targetLocationId = null,
        bool supervisorOverride = false,
        string? overrideReason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetInventoryStatusId);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A disposition reason is required.", nameof(reason));
        }

        if (supervisorOverride && string.IsNullOrWhiteSpace(overrideReason))
        {
            throw new ArgumentException("A supervisor override reason is required.", nameof(overrideReason));
        }

        Type = type;
        Quantity = quantity;
        TargetInventoryStatusId = targetInventoryStatusId;
        Reason = Required(reason, 1_000, nameof(reason));
        RecordedByUserId = Required(userId, 450, nameof(userId));
        RecordedAtUtc = NormalizeUtc(recordedAtUtc);
        ReferenceNumber = Optional(referenceNumber, 100);
        MovementId = movementId;
        TargetLocationId = targetLocationId;
        SupervisorOverride = supervisorOverride;
        OverrideReason = Optional(overrideReason, 1_000);
    }

    public int QualityInspectionId { get; private set; }
    public QualityDispositionType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public int TargetInventoryStatusId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string RecordedByUserId { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public int? MovementId { get; private set; }
    public int? TargetLocationId { get; private set; }
    public bool SupervisorOverride { get; private set; }
    public string? OverrideReason { get; private set; }

    public QualityInspection Inspection { get; private set; } = null!;
    public Movement? Movement { get; private set; }
    public Location? TargetLocation { get; private set; }

    private static string Required(string value, int maximumLength, string parameterName)
    {
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
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }
}
