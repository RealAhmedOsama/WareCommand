// Wms.Domain/Entities/Movement.cs

using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public class Movement : Entity
{
    // EF Constructor
    private Movement()
    {
    }

    public Movement(MovementType type, int itemId, Quantity quantity, string userId,
        int? fromLocationId = null, int? toLocationId = null, int? lotId = null,
        string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? fromInventoryStatusId = null,
        int? toInventoryStatusId = null,
        InventoryStatusMovementLeg? statusChangeLeg = null,
        int? licensePlateId = null,
        int? fromLicensePlateId = null,
        int? toLicensePlateId = null,
        decimal? adjustmentBeforeQuantity = null,
        decimal? adjustmentDelta = null,
        decimal? adjustmentAfterQuantity = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required", nameof(userId));

        Type = type;
        ItemId = itemId;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        LotId = lotId;
        SerialNumber = serialNumber?.Trim();
        SerialNumberId = serialNumberId;
        InventoryStatusId = inventoryStatusId;
        FromInventoryStatusId = fromInventoryStatusId;
        ToInventoryStatusId = toInventoryStatusId;
        StatusChangeLeg = statusChangeLeg;
        LicensePlateId = licensePlateId;
        FromLicensePlateId = fromLicensePlateId;
        ToLicensePlateId = toLicensePlateId;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        AdjustmentBeforeQuantity = adjustmentBeforeQuantity;
        AdjustmentDelta = adjustmentDelta;
        AdjustmentAfterQuantity = adjustmentAfterQuantity;
        Quantity = quantity;
        UserId = userId.Trim();
        ReferenceNumber = referenceNumber?.Trim();
        Notes = notes?.Trim();
        Timestamp = timestampUtc.HasValue
            ? NormalizeUtc(timestampUtc.Value)
            : DateTime.UnixEpoch;

        var conversion = quantity.ConversionSnapshot;
        EnteredQuantity = conversion?.EnteredQuantity ?? quantity.Value;
        EnteredUnitOfMeasure = conversion?.EnteredUnitOfMeasure ?? "BASE";
        BaseUnitOfMeasure = conversion?.BaseUnitOfMeasure ?? "BASE";
        ConversionFactorToBase = conversion?.ConversionFactorToBase ?? 1m;
        ConversionPrecision = conversion?.ResultPrecision ?? 4;
        ConversionRoundingMode = conversion?.RoundingMode ?? QuantityRoundingMode.Reject;
        ConversionRoundingDelta = conversion?.RoundingDelta ?? 0m;
        ConversionPath = conversion?.ConversionPath ?? "BASE";
        ConversionRuleIds = conversion?.ConversionRuleIds ?? string.Empty;

        var packaging = conversion?.PackagingSnapshot;
        PackagingId = packaging?.PackagingId;
        PackagingVersion = packaging?.Version;
        PackagingCode = packaging?.Code;
        PackagingName = packaging?.Name;
        PackagingLocalizedName = packaging?.LocalizedName;
        PackagingType = packaging?.Type;
        PackagingUnitOfMeasure = packaging?.UnitOfMeasure;
        PackagingUnitsPerPackage = packaging?.UnitsPerPackage;
        PackagingPartialPackagePolicy = packaging?.PartialPackagePolicy;
        PackagingGrossWeightKg = packaging?.GrossWeightKg;
        PackagingLengthCm = packaging?.LengthCm;
        PackagingWidthCm = packaging?.WidthCm;
        PackagingHeightCm = packaging?.HeightCm;
        PackagingVolumeCubicMeters = packaging?.VolumeCubicMeters;
    }

    public MovementType Type { get; private set; }
    public int ItemId { get; private set; }
    public int? FromLocationId { get; private set; }
    public int? ToLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public int? FromInventoryStatusId { get; private set; }
    public int? ToInventoryStatusId { get; private set; }
    public InventoryStatusMovementLeg? StatusChangeLeg { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int? FromLicensePlateId { get; private set; }
    public int? ToLicensePlateId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public decimal? AdjustmentBeforeQuantity { get; private set; }
    public decimal? AdjustmentDelta { get; private set; }
    public decimal? AdjustmentAfterQuantity { get; private set; }
    public string? SerialNumber { get; private set; }
    public Quantity Quantity { get; private set; } = Quantity.Zero;
    public decimal EnteredQuantity { get; private set; }
    public string EnteredUnitOfMeasure { get; private set; } = "BASE";
    public string BaseUnitOfMeasure { get; private set; } = "BASE";
    public decimal ConversionFactorToBase { get; private set; } = 1m;
    public int ConversionPrecision { get; private set; } = 4;
    public QuantityRoundingMode ConversionRoundingMode { get; private set; } = QuantityRoundingMode.Reject;
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = "BASE";
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public int? PackagingId { get; private set; }
    public int? PackagingVersion { get; private set; }
    public string? PackagingCode { get; private set; }
    public string? PackagingName { get; private set; }
    public string? PackagingLocalizedName { get; private set; }
    public PackagingType? PackagingType { get; private set; }
    public string? PackagingUnitOfMeasure { get; private set; }
    public decimal? PackagingUnitsPerPackage { get; private set; }
    public PackagingPartialPolicy? PackagingPartialPackagePolicy { get; private set; }
    public decimal? PackagingGrossWeightKg { get; private set; }
    public decimal? PackagingLengthCm { get; private set; }
    public decimal? PackagingWidthCm { get; private set; }
    public decimal? PackagingHeightCm { get; private set; }
    public decimal? PackagingVolumeCubicMeters { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UnixEpoch;
    public int? PurchaseOrderId { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public int? ReceiptId { get; private set; }
    public int? ReceiptLineId { get; private set; }
    public ReceiptMovementKind? ReceiptMovementKind { get; private set; }
    public int? RelatedMovementId { get; private set; }

    // Navigation properties
    public Item Item { get; private set; } = null!;
    public Location? FromLocation { get; private set; }
    public Location? ToLocation { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public InventoryStatus? InventoryStatus { get; private set; }
    public InventoryStatus? FromInventoryStatus { get; private set; }
    public InventoryStatus? ToInventoryStatus { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public LicensePlate? FromLicensePlate { get; private set; }
    public LicensePlate? ToLicensePlate { get; private set; }
    public InventoryOwner? InventoryOwner { get; private set; }
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; private set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }
    public Receipt? Receipt { get; private set; }
    public ReceiptLine? ReceiptLine { get; private set; }
    public Movement? RelatedMovement { get; private set; }

    public void LinkPurchaseOrder(int purchaseOrderId, int purchaseOrderLineId)
    {
        if (Type != MovementType.Receipt)
        {
            throw new InvalidOperationException("Only receipt movements can be linked to a purchase order.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchaseOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(purchaseOrderLineId);
        if (PurchaseOrderId.HasValue &&
            (PurchaseOrderId.Value != purchaseOrderId || PurchaseOrderLineId != purchaseOrderLineId))
        {
            throw new InvalidOperationException("The receipt movement is already linked to another purchase order.");
        }

        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
    }

    public void LinkAdvanceShippingNotice(int advanceShippingNoticeId, int advanceShippingNoticeLineId)
    {
        if (Type != MovementType.Receipt)
        {
            throw new InvalidOperationException("Only receipt movements can link to an ASN.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(advanceShippingNoticeId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(advanceShippingNoticeLineId);
        if (AdvanceShippingNoticeId.HasValue || AdvanceShippingNoticeLineId.HasValue)
        {
            if (AdvanceShippingNoticeId == advanceShippingNoticeId &&
                AdvanceShippingNoticeLineId == advanceShippingNoticeLineId)
            {
                return;
            }

            throw new InvalidOperationException("The receipt movement is already linked to another ASN.");
        }

        AdvanceShippingNoticeId = advanceShippingNoticeId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
    }

    public void LinkReceipt(
        int receiptId,
        int receiptLineId,
        ReceiptMovementKind kind = Wms.Domain.Enums.ReceiptMovementKind.Receipt,
        int? relatedMovementId = null)
    {
        if (Type is not (MovementType.Receipt or MovementType.Adjustment))
        {
            throw new InvalidOperationException("Only receipt or compensating adjustment movements can link to a receipt.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiptId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receiptLineId);
        if (relatedMovementId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relatedMovementId));
        }

        if (ReceiptId.HasValue || ReceiptLineId.HasValue || ReceiptMovementKind.HasValue)
        {
            if (ReceiptId == receiptId && ReceiptLineId == receiptLineId &&
                ReceiptMovementKind == kind && RelatedMovementId == relatedMovementId)
            {
                return;
            }

            throw new InvalidOperationException("The movement is already linked to another receipt.");
        }

        ReceiptId = receiptId;
        ReceiptLineId = receiptLineId;
        ReceiptMovementKind = kind;
        RelatedMovementId = relatedMovementId;
    }

    public static Movement CreateReceipt(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? toLicensePlateId = null)
    {
        return new Movement(MovementType.Receipt, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes, timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: toLicensePlateId,
            toLicensePlateId: toLicensePlateId);
    }

    public static Movement CreatePutaway(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null, DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? fromLicensePlateId = null,
        int? toLicensePlateId = null)
    {
        return new Movement(MovementType.Putaway, itemId, quantity, userId,
            fromLocationId: fromLocationId,
            toLocationId: toLocationId,
            lotId: lotId,
            serialNumber: serialNumber,
            referenceNumber: referenceNumber,
            notes: notes,
            timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: toLicensePlateId ?? fromLicensePlateId,
            fromLicensePlateId: fromLicensePlateId,
            toLicensePlateId: toLicensePlateId);
    }

    public static Movement CreatePick(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? fromLicensePlateId = null)
    {
        return new Movement(MovementType.Pick, itemId, quantity, userId,
            fromLocationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes, timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: fromLicensePlateId,
            fromLicensePlateId: fromLicensePlateId);
    }

    public void LinkPickDestination(int destinationLocationId)
    {
        if (Type != MovementType.Pick)
        {
            throw new InvalidOperationException(
                "Only pick movements can be linked to a staging destination.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(destinationLocationId);
        if (ToLocationId.HasValue && ToLocationId != destinationLocationId)
        {
            throw new InvalidOperationException(
                "The pick movement is already linked to another destination.");
        }

        ToLocationId = destinationLocationId;
    }

    public void SetOwnership(
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string? ownerCodeSnapshot = null)
    {
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
    }

    public static Movement CreateShip(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? fromLicensePlateId = null)
    {
        return new Movement(
            MovementType.Ship,
            itemId,
            quantity,
            userId,
            fromLocationId: fromLocationId,
            lotId: lotId,
            serialNumber: serialNumber,
            referenceNumber: referenceNumber,
            notes: notes,
            timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: fromLicensePlateId,
            fromLicensePlateId: fromLicensePlateId);
    }

    public static Movement CreateAdjustment(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? licensePlateId = null,
        decimal? adjustmentBeforeQuantity = null,
        decimal? adjustmentDelta = null,
        decimal? adjustmentAfterQuantity = null)
    {
        return new Movement(MovementType.Adjustment, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber,
            notes: notes,
            timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: licensePlateId,
            toLicensePlateId: licensePlateId,
            adjustmentBeforeQuantity: adjustmentBeforeQuantity,
            adjustmentDelta: adjustmentDelta,
            adjustmentAfterQuantity: adjustmentAfterQuantity);
    }

    public static Movement CreateTransfer(
        int itemId,
        int fromLocationId,
        int toLocationId,
        Quantity quantity,
        string userId,
        int? lotId = null,
        string? serialNumber = null,
        string? referenceNumber = null,
        string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? fromLicensePlateId = null,
        int? toLicensePlateId = null)
    {
        return new Movement(
            MovementType.Transfer,
            itemId,
            quantity,
            userId,
            fromLocationId: fromLocationId,
            toLocationId: toLocationId,
            lotId: lotId,
            serialNumber: serialNumber,
            referenceNumber: referenceNumber,
            notes: notes,
            timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            licensePlateId: toLicensePlateId ?? fromLicensePlateId,
            fromLicensePlateId: fromLicensePlateId,
            toLicensePlateId: toLicensePlateId);
    }

    public static Movement CreateStatusChange(
        int itemId,
        int locationId,
        Quantity quantity,
        string userId,
        int fromInventoryStatusId,
        int toInventoryStatusId,
        InventoryStatusMovementLeg leg,
        int? lotId = null,
        string? serialNumber = null,
        string? referenceNumber = null,
        string? notes = null,
        DateTime? timestampUtc = null,
        int? serialNumberId = null,
        int? licensePlateId = null)
    {
        var inventoryStatusId = leg == InventoryStatusMovementLeg.Outbound
            ? fromInventoryStatusId
            : toInventoryStatusId;
        var fromLocationId = leg == InventoryStatusMovementLeg.Outbound ? (int?)locationId : null;
        var toLocationId = leg == InventoryStatusMovementLeg.Inbound ? (int?)locationId : null;
        return new Movement(
            MovementType.StatusChange,
            itemId,
            quantity,
            userId,
            fromLocationId: fromLocationId,
            toLocationId: toLocationId,
            lotId: lotId,
            serialNumber: serialNumber,
            referenceNumber: referenceNumber,
            notes: notes,
            timestampUtc: timestampUtc,
            serialNumberId: serialNumberId,
            inventoryStatusId: inventoryStatusId,
            fromInventoryStatusId: fromInventoryStatusId,
            toInventoryStatusId: toInventoryStatusId,
            statusChangeLeg: leg,
            licensePlateId: licensePlateId);
    }
}
