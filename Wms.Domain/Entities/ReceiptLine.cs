using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReceiptLine : Entity
{
    private readonly List<ReceiptLineMovement> _movements = [];
    private readonly List<ReceiptLineLink> _links = [];

    private ReceiptLine()
    {
    }

    public ReceiptLine(
        int lineNumber,
        int warehouseId,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        string enteredUnitOfMeasure,
        decimal enteredQuantity,
        string baseUnitOfMeasure,
        decimal expectedBaseQuantity,
        decimal conversionFactorToBase,
        int conversionPrecision,
        QuantityRoundingMode conversionRoundingMode,
        decimal conversionRoundingDelta,
        string conversionPath,
        string conversionRuleIds,
        int? packagingId = null,
        int? packagingVersion = null,
        string? packagingCode = null,
        string? packagingName = null,
        string? packagingLocalizedName = null,
        PackagingType? packagingType = null,
        string? packagingUnitOfMeasure = null,
        decimal? packagingUnitsPerPackage = null,
        PackagingPartialPolicy? packagingPartialPackagePolicy = null,
        decimal? packagingGrossWeightKg = null,
        decimal? packagingLengthCm = null,
        decimal? packagingWidthCm = null,
        decimal? packagingHeightCm = null,
        decimal? packagingVolumeCubicMeters = null,
        int? purchaseOrderId = null,
        int? purchaseOrderLineId = null,
        int? advanceShippingNoticeId = null,
        int? advanceShippingNoticeLineId = null,
        int? receivingLocationId = null,
        string? lotNumberSnapshot = null,
        DateTime? expiryDateSnapshot = null,
        string? serialNumberSnapshot = null,
        int? licensePlateId = null,
        string? licensePlateNumberSnapshot = null,
        bool licensePlateIsSscc = false,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        string? inventoryStatusCodeSnapshot = null,
        string? inventoryStatusNameSnapshot = null,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedBaseQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(conversionFactorToBase);
        if (purchaseOrderId is <= 0 || purchaseOrderLineId is <= 0 ||
            advanceShippingNoticeId is <= 0 || advanceShippingNoticeLineId is <= 0 ||
            receivingLocationId is <= 0 || licensePlateId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchaseOrderId), "Optional identifiers must be positive when supplied.");
        }

        LineNumber = lineNumber;
        WarehouseId = warehouseId;
        ItemId = itemId;
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = Required(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        EnteredUnitOfMeasure = Unit(enteredUnitOfMeasure, nameof(enteredUnitOfMeasure));
        EnteredQuantity = NonNegative(enteredQuantity, nameof(enteredQuantity));
        BaseUnitOfMeasure = Unit(baseUnitOfMeasure, nameof(baseUnitOfMeasure));
        ExpectedBaseQuantity = expectedBaseQuantity;
        ConversionFactorToBase = conversionFactorToBase;
        ConversionPrecision = conversionPrecision is >= 0 and <= 12
            ? conversionPrecision
            : throw new ArgumentOutOfRangeException(nameof(conversionPrecision));
        ConversionRoundingMode = conversionRoundingMode;
        ConversionRoundingDelta = NonNegative(conversionRoundingDelta, nameof(conversionRoundingDelta));
        ConversionPath = Required(conversionPath, 500, nameof(conversionPath));
        ConversionRuleIds = Optional(conversionRuleIds, 500) ?? string.Empty;
        PackagingId = packagingId;
        PackagingVersion = packagingVersion;
        PackagingCode = OptionalUpper(packagingCode, 40);
        PackagingName = Optional(packagingName, 200);
        PackagingLocalizedName = Optional(packagingLocalizedName, 200);
        PackagingType = packagingType;
        PackagingUnitOfMeasure = OptionalUpper(packagingUnitOfMeasure, 20);
        PackagingUnitsPerPackage = NonNegative(packagingUnitsPerPackage, nameof(packagingUnitsPerPackage));
        PackagingPartialPackagePolicy = packagingPartialPackagePolicy;
        PackagingGrossWeightKg = NonNegative(packagingGrossWeightKg, nameof(packagingGrossWeightKg));
        PackagingLengthCm = NonNegative(packagingLengthCm, nameof(packagingLengthCm));
        PackagingWidthCm = NonNegative(packagingWidthCm, nameof(packagingWidthCm));
        PackagingHeightCm = NonNegative(packagingHeightCm, nameof(packagingHeightCm));
        PackagingVolumeCubicMeters = NonNegative(packagingVolumeCubicMeters, nameof(packagingVolumeCubicMeters));
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        AdvanceShippingNoticeId = advanceShippingNoticeId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
        ReceivingLocationId = receivingLocationId;
        LotNumberSnapshot = Optional(lotNumberSnapshot, 100);
        ExpiryDateSnapshot = expiryDateSnapshot.HasValue ? NormalizeUtc(expiryDateSnapshot.Value) : null;
        SerialNumberSnapshot = Optional(serialNumberSnapshot, 100);
        LicensePlateId = licensePlateId;
        LicensePlateNumberSnapshot = Optional(licensePlateNumberSnapshot, 100);
        LicensePlateIsSscc = licensePlateIsSscc;
        InventoryStatusId = inventoryStatusId;
        InventoryStatusCodeSnapshot = OptionalUpper(inventoryStatusCodeSnapshot, 50);
        InventoryStatusNameSnapshot = Optional(inventoryStatusNameSnapshot, 200);
        Notes = Optional(notes, 1_000);
    }

    public int ReceiptId { get; private set; }
    public int LineNumber { get; private set; }
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string EnteredUnitOfMeasure { get; private set; } = string.Empty;
    public decimal EnteredQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal ExpectedBaseQuantity { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal AcceptedBaseQuantity { get; private set; }
    public decimal RejectedBaseQuantity { get; private set; }
    public decimal DamagedBaseQuantity { get; private set; }
    public decimal QuarantinedBaseQuantity { get; private set; }
    public decimal ConversionFactorToBase { get; private set; }
    public int ConversionPrecision { get; private set; }
    public QuantityRoundingMode ConversionRoundingMode { get; private set; }
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = string.Empty;
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
    public int? PurchaseOrderId { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public int? ReceivingLocationId { get; private set; }
    public string? LotNumberSnapshot { get; private set; }
    public DateTime? ExpiryDateSnapshot { get; private set; }
    public string? SerialNumberSnapshot { get; private set; }
    public int? LicensePlateId { get; private set; }
    public string? LicensePlateNumberSnapshot { get; private set; }
    public bool LicensePlateIsSscc { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string? InventoryStatusCodeSnapshot { get; private set; }
    public string? InventoryStatusNameSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; } = 1;

    public Receipt Receipt { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; private set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }
    public Location? ReceivingLocation { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public LicensePlate? LicensePlate { get; private set; }
    public IReadOnlyList<ReceiptLineMovement> Movements => _movements.AsReadOnly();
    public IReadOnlyList<ReceiptLineLink> Links => _links.AsReadOnly();

    public decimal RemainingBaseQuantity => Math.Max(0m, ExpectedBaseQuantity - ReceivedBaseQuantity);
    public bool IsFullyReceived => ReceivedBaseQuantity >= ExpectedBaseQuantity;
    public bool HasReceiptHistory => ReceivedBaseQuantity > 0m;
    public bool HasMovementHistory => Movements.Count > 0;

    public void SetInventoryStatusSnapshot(int inventoryStatusId, string? code, string? name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        InventoryStatusId = inventoryStatusId;
        InventoryStatusCodeSnapshot = OptionalUpper(code, 50);
        InventoryStatusNameSnapshot = Optional(name, 200);
        Revision++;
        SetUpdatedAt();
    }

    public void RecordPhysicalReceipt(
        decimal receivedBaseQuantity,
        decimal acceptedBaseQuantity,
        decimal rejectedBaseQuantity,
        decimal damagedBaseQuantity,
        decimal quarantinedBaseQuantity)
    {
        var received = NonNegative(receivedBaseQuantity, nameof(receivedBaseQuantity));
        var accepted = NonNegative(acceptedBaseQuantity, nameof(acceptedBaseQuantity));
        var rejected = NonNegative(rejectedBaseQuantity, nameof(rejectedBaseQuantity));
        var damaged = NonNegative(damagedBaseQuantity, nameof(damagedBaseQuantity));
        var quarantined = NonNegative(quarantinedBaseQuantity, nameof(quarantinedBaseQuantity));
        if (received <= 0m || accepted + rejected + damaged + quarantined != received)
        {
            throw new InvalidOperationException("Receipt line quantities must balance received quantity.");
        }

        ReceivedBaseQuantity += received;
        AcceptedBaseQuantity += accepted;
        RejectedBaseQuantity += rejected;
        DamagedBaseQuantity += damaged;
        QuarantinedBaseQuantity += quarantined;
        Revision++;
        SetUpdatedAt();
    }

    public void ReversePhysicalReceipt(
        decimal receivedBaseQuantity,
        decimal acceptedBaseQuantity,
        decimal rejectedBaseQuantity,
        decimal damagedBaseQuantity,
        decimal quarantinedBaseQuantity)
    {
        var received = NonNegative(receivedBaseQuantity, nameof(receivedBaseQuantity));
        var accepted = NonNegative(acceptedBaseQuantity, nameof(acceptedBaseQuantity));
        var rejected = NonNegative(rejectedBaseQuantity, nameof(rejectedBaseQuantity));
        var damaged = NonNegative(damagedBaseQuantity, nameof(damagedBaseQuantity));
        var quarantined = NonNegative(quarantinedBaseQuantity, nameof(quarantinedBaseQuantity));
        if (accepted + rejected + damaged + quarantined != received ||
            received > ReceivedBaseQuantity || accepted > AcceptedBaseQuantity ||
            rejected > RejectedBaseQuantity || damaged > DamagedBaseQuantity ||
            quarantined > QuarantinedBaseQuantity)
        {
            throw new InvalidOperationException("The receipt reversal exceeds the line history.");
        }

        ReceivedBaseQuantity -= received;
        AcceptedBaseQuantity -= accepted;
        RejectedBaseQuantity -= rejected;
        DamagedBaseQuantity -= damaged;
        QuarantinedBaseQuantity -= quarantined;
        Revision++;
        SetUpdatedAt();
    }

    private static decimal NonNegative(decimal value, string parameterName) =>
        value >= 0m
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);

    private static decimal? NonNegative(decimal? value, string parameterName) =>
        value.HasValue
            ? NonNegative(value.Value, parameterName)
            : null;

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

    private static string Unit(string value, string parameterName) =>
        Required(value, 20, parameterName).ToUpperInvariant();

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
