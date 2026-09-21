using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class SupplierReturnLine : Entity
{
    private SupplierReturnLine()
    {
    }

    public SupplierReturnLine(
        int lineNumber,
        int warehouseId,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        decimal requestedBaseQuantity,
        string baseUnitOfMeasure,
        int sourceLocationId,
        int inventoryStatusId,
        string reason,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        int? purchaseOrderLineId = null,
        int? advanceShippingNoticeLineId = null,
        int? receiptLineId = null,
        int? qualityInspectionId = null,
        int? qualityInspectionDispositionId = null,
        string? sourceReference = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedBaseQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        if (lotId is <= 0 || serialNumberId is <= 0 || licensePlateId is <= 0 ||
            purchaseOrderLineId is <= 0 || advanceShippingNoticeLineId is <= 0 ||
            receiptLineId is <= 0 || qualityInspectionId is <= 0 ||
            qualityInspectionDispositionId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lotId));
        }

        LineNumber = lineNumber;
        WarehouseId = warehouseId;
        ItemId = itemId;
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = Required(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        RequestedBaseQuantity = requestedBaseQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        InventoryStatusId = inventoryStatusId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = OptionalUpper(serialNumber, 100);
        LicensePlateId = licensePlateId;
        PurchaseOrderLineId = purchaseOrderLineId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
        ReceiptLineId = receiptLineId;
        QualityInspectionId = qualityInspectionId;
        QualityInspectionDispositionId = qualityInspectionDispositionId;
        Reason = Required(reason, 1_000, nameof(reason));
        SourceReference = Optional(sourceReference, 200);
        Revision = 1;
    }

    public int SupplierReturnId { get; private set; }
    public int LineNumber { get; private set; }
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public decimal RequestedBaseQuantity { get; private set; }
    public decimal ApprovedBaseQuantity { get; private set; }
    public decimal ReservedBaseQuantity { get; private set; }
    public decimal StagedBaseQuantity { get; private set; }
    public decimal ShippedBaseQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int SourceLocationId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public int? ReceiptLineId { get; private set; }
    public int? QualityInspectionId { get; private set; }
    public int? QualityInspectionDispositionId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? SourceReference { get; private set; }
    public long Revision { get; private set; }

    public SupplierReturn SupplierReturn { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; private set; }
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }
    public ReceiptLine? ReceiptLine { get; private set; }
    public QualityInspection? QualityInspection { get; private set; }
    public QualityInspectionDisposition? QualityInspectionDisposition { get; private set; }

    public decimal RemainingToStage => Math.Max(0m, ApprovedBaseQuantity - StagedBaseQuantity);
    public decimal RemainingToShip => Math.Max(0m, StagedBaseQuantity - ShippedBaseQuantity);
    public bool IsFullyStaged => RemainingToStage == 0m;
    public bool IsFullyShipped => RemainingToShip == 0m;

    public void ApproveAndReserve()
    {
        if (ApprovedBaseQuantity != 0m || ReservedBaseQuantity != 0m)
        {
            throw new InvalidOperationException("The supplier-return line has already been reserved.");
        }

        ApprovedBaseQuantity = RequestedBaseQuantity;
        ReservedBaseQuantity = RequestedBaseQuantity;
        Touch();
    }

    public void ReleaseReservation(decimal quantity)
    {
        Positive(quantity, nameof(quantity));
        if (quantity > ReservedBaseQuantity)
        {
            throw new InvalidOperationException("The supplier-return reservation release exceeds the open reservation.");
        }

        ReservedBaseQuantity -= quantity;
        Touch();
    }

    public void RecordStaged(decimal quantity)
    {
        Positive(quantity, nameof(quantity));
        if (quantity > RemainingToStage || quantity > ReservedBaseQuantity)
        {
            throw new InvalidOperationException("The staged quantity exceeds the approved or reserved supplier-return quantity.");
        }

        ReservedBaseQuantity -= quantity;
        StagedBaseQuantity += quantity;
        Touch();
    }

    public void AcceptShortPick()
    {
        if (StagedBaseQuantity <= 0m || StagedBaseQuantity >= ApprovedBaseQuantity)
        {
            throw new InvalidOperationException(
                "A short supplier-return pick must stage less than the approved quantity.");
        }

        ApprovedBaseQuantity = StagedBaseQuantity;
        ReservedBaseQuantity = 0m;
        Touch();
    }

    public void RecordShipped(decimal quantity)
    {
        Positive(quantity, nameof(quantity));
        if (quantity > RemainingToShip)
        {
            throw new InvalidOperationException("The shipped quantity exceeds the staged supplier-return quantity.");
        }

        ShippedBaseQuantity += quantity;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static void Positive(decimal value, string parameterName) =>
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);

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

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
