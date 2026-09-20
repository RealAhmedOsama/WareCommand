using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Immutable reservation history. Operational rows may change status, but
/// each allocation/release/consume decision remains auditable here.
/// </summary>
public sealed class InventoryReservationEvent : Entity
{
    private InventoryReservationEvent()
    {
    }

    public InventoryReservationEvent(
        int reservationId,
        InventoryReservationEventType type,
        decimal quantity,
        string actorUserId,
        DateTime occurredAtUtc,
        string correlationId,
        int? allocationId = null,
        string? reason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservationId);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        ReservationId = reservationId;
        AllocationId = allocationId;
        Type = type;
        Quantity = quantity;
        ActorUserId = NormalizeRequired(actorUserId, 450, nameof(actorUserId));
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        CorrelationId = NormalizeRequired(correlationId, 100, nameof(correlationId));
        Reason = NormalizeOptional(reason, 1_000);
    }

    public int ReservationId { get; private set; }
    public int? AllocationId { get; private set; }
    public InventoryReservationEventType Type { get; private set; }
    public decimal Quantity { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; } = DateTime.UnixEpoch;
    public string CorrelationId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }

    public InventoryReservation Reservation { get; private set; } = null!;
    public InventoryReservationAllocation? Allocation { get; private set; }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
