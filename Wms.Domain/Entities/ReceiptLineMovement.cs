using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReceiptLineMovement : Entity
{
    private ReceiptLineMovement()
    {
    }

    public ReceiptLineMovement(
        ReceiptLine receiptLine,
        Movement movement,
        decimal baseQuantity,
        ReceiptMovementKind kind,
        string userId,
        DateTime occurredAtUtc,
        int? relatedMovementId = null,
        string? reason = null,
        decimal? acceptedBaseQuantity = null,
        decimal? rejectedBaseQuantity = null,
        decimal? damagedBaseQuantity = null,
        decimal? quarantinedBaseQuantity = null)
    {
        ArgumentNullException.ThrowIfNull(receiptLine);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseQuantity);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        ReceiptLine = receiptLine;
        Movement = movement;
        BaseQuantity = baseQuantity;
        AcceptedBaseQuantity = acceptedBaseQuantity ?? (kind == ReceiptMovementKind.Receipt ? baseQuantity : 0m);
        RejectedBaseQuantity = rejectedBaseQuantity ?? 0m;
        DamagedBaseQuantity = damagedBaseQuantity ?? 0m;
        QuarantinedBaseQuantity = quarantinedBaseQuantity ?? 0m;
        if (AcceptedBaseQuantity + RejectedBaseQuantity + DamagedBaseQuantity + QuarantinedBaseQuantity != baseQuantity)
        {
            throw new InvalidOperationException("Receipt movement quantities must balance.");
        }
        Kind = kind;
        UserId = userId.Trim();
        OccurredAtUtc = NormalizeUtc(occurredAtUtc);
        RelatedMovementId = relatedMovementId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public int ReceiptLineId { get; private set; }
    public int MovementId { get; private set; }
    public decimal BaseQuantity { get; private set; }
    public decimal AcceptedBaseQuantity { get; private set; }
    public decimal RejectedBaseQuantity { get; private set; }
    public decimal DamagedBaseQuantity { get; private set; }
    public decimal QuarantinedBaseQuantity { get; private set; }
    public ReceiptMovementKind Kind { get; private set; }
    public int? RelatedMovementId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public string? Reason { get; private set; }

    public ReceiptLine ReceiptLine { get; private set; } = null!;
    public Movement Movement { get; private set; } = null!;
    public Movement? RelatedMovement { get; private set; }
}
