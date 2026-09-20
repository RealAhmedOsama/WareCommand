using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Append-only allocation of one canonical receipt movement to one PO line.
/// Keeping this ledger separate from the line total preserves partial-receipt
/// history and makes a future return/reversal workflow traceable.
/// </summary>
public sealed class PurchaseOrderReceiptAllocation : Entity
{
    private PurchaseOrderReceiptAllocation()
    {
    }

    public PurchaseOrderReceiptAllocation(
        int purchaseOrderId,
        int purchaseOrderLineId,
        Movement movement,
        decimal receivedBaseQuantity,
        string receivedByUserId,
        DateTime receivedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchaseOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchaseOrderLineId);
        ArgumentNullException.ThrowIfNull(movement);
        if (receivedBaseQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(receivedBaseQuantity),
                "Receipt quantity must be positive.");
        }

        if (string.IsNullOrWhiteSpace(receivedByUserId))
        {
            throw new ArgumentException("User ID is required.", nameof(receivedByUserId));
        }

        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        Movement = movement;
        MovementId = movement.Id;
        ReceivedBaseQuantity = receivedBaseQuantity;
        EnteredQuantity = movement.EnteredQuantity;
        EnteredUnitOfMeasure = movement.EnteredUnitOfMeasure;
        BaseUnitOfMeasure = movement.BaseUnitOfMeasure;
        ConversionFactorToBase = movement.ConversionFactorToBase;
        ConversionPrecision = movement.ConversionPrecision;
        ConversionRoundingMode = movement.ConversionRoundingMode;
        ConversionRoundingDelta = movement.ConversionRoundingDelta;
        ConversionPath = movement.ConversionPath;
        ConversionRuleIds = movement.ConversionRuleIds;
        ReferenceNumber = movement.ReferenceNumber;
        ReceivedByUserId = receivedByUserId.Trim();
        ReceivedAtUtc = NormalizeUtc(receivedAtUtc);
    }

    public int PurchaseOrderId { get; private set; }
    public int PurchaseOrderLineId { get; private set; }
    public int MovementId { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal EnteredQuantity { get; private set; }
    public string EnteredUnitOfMeasure { get; private set; } = string.Empty;
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal ConversionFactorToBase { get; private set; }
    public int ConversionPrecision { get; private set; }
    public QuantityRoundingMode ConversionRoundingMode { get; private set; }
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = string.Empty;
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public string? ReferenceNumber { get; private set; }
    public string ReceivedByUserId { get; private set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; private set; }

    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public PurchaseOrderLine PurchaseOrderLine { get; private set; } = null!;
    public Movement Movement { get; private set; } = null!;
}
