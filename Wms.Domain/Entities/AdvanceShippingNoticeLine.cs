using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class AdvanceShippingNoticeLine : Entity
{
    private AdvanceShippingNoticeLine()
    {
    }

    public AdvanceShippingNoticeLine(
        int lineNumber,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        string enteredUnitOfMeasure,
        decimal expectedQuantity,
        string baseUnitOfMeasure,
        decimal expectedBaseQuantity,
        decimal conversionFactorToBase,
        int conversionPrecision,
        QuantityRoundingMode conversionRoundingMode,
        decimal conversionRoundingDelta,
        string conversionPath,
        string conversionRuleIds,
        decimal overDeliveryTolerancePercent = 0,
        decimal underDeliveryTolerancePercent = 0,
        int? itemPackagingId = null,
        string? itemPackagingCodeSnapshot = null,
        string? itemPackagingNameSnapshot = null,
        string? itemPackagingUnitOfMeasureSnapshot = null,
        decimal? itemPackagingUnitsPerPackageSnapshot = null,
        int? purchaseOrderId = null,
        int? purchaseOrderLineId = null,
        string? preAdvisedLotNumber = null,
        DateTime? preAdvisedExpiryDate = null,
        string? preAdvisedSerialNumber = null,
        string? expectedLicensePlateNumber = null,
        bool expectedLicensePlateIsSscc = false,
        string? notes = null)
    {
        ValidateIdentity(lineNumber, itemId);
        if (purchaseOrderLineId.HasValue != purchaseOrderId.HasValue)
        {
            throw new ArgumentException("Purchase order and purchase-order line must be supplied together.");
        }

        LineNumber = lineNumber;
        ItemId = itemId;
        ItemSkuSnapshot = NormalizeRequired(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = NormalizeRequired(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        EnteredUnitOfMeasure = NormalizeUnit(enteredUnitOfMeasure, nameof(enteredUnitOfMeasure));
        ExpectedQuantity = ValidatePositive(expectedQuantity, nameof(expectedQuantity));
        BaseUnitOfMeasure = NormalizeUnit(baseUnitOfMeasure, nameof(baseUnitOfMeasure));
        ExpectedBaseQuantity = ValidatePositive(expectedBaseQuantity, nameof(expectedBaseQuantity));
        ConversionFactorToBase = ValidatePositive(conversionFactorToBase, nameof(conversionFactorToBase));
        ConversionPrecision = ValidatePrecision(conversionPrecision);
        ConversionRoundingMode = conversionRoundingMode;
        ConversionRoundingDelta = ValidateNonNegative(conversionRoundingDelta, nameof(conversionRoundingDelta));
        ConversionPath = NormalizeRequired(conversionPath, 500, nameof(conversionPath));
        ConversionRuleIds = NormalizeOptional(conversionRuleIds, 500) ?? string.Empty;
        OverDeliveryTolerancePercent = ValidatePercentage(overDeliveryTolerancePercent, nameof(overDeliveryTolerancePercent));
        UnderDeliveryTolerancePercent = ValidatePercentage(underDeliveryTolerancePercent, nameof(underDeliveryTolerancePercent));
        ItemPackagingId = ValidateOptionalId(itemPackagingId, nameof(itemPackagingId));
        ItemPackagingCodeSnapshot = NormalizeOptionalUpper(itemPackagingCodeSnapshot, 40);
        ItemPackagingNameSnapshot = NormalizeOptional(itemPackagingNameSnapshot, 200);
        ItemPackagingUnitOfMeasureSnapshot = NormalizeOptionalUpper(itemPackagingUnitOfMeasureSnapshot, 20);
        ItemPackagingUnitsPerPackageSnapshot = itemPackagingUnitsPerPackageSnapshot is null
            ? null
            : ValidatePositive(itemPackagingUnitsPerPackageSnapshot.Value, nameof(itemPackagingUnitsPerPackageSnapshot));
        PurchaseOrderId = purchaseOrderId;
        PurchaseOrderLineId = purchaseOrderLineId;
        PreAdvisedLotNumber = NormalizeOptionalUpper(preAdvisedLotNumber, 100);
        PreAdvisedExpiryDate = NormalizeDateOnly(preAdvisedExpiryDate);
        PreAdvisedSerialNumber = NormalizeOptionalUpper(preAdvisedSerialNumber, 100);
        ExpectedLicensePlateNumber = NormalizeOptionalUpper(expectedLicensePlateNumber, 100);
        ExpectedLicensePlateIsSscc = expectedLicensePlateIsSscc;
        Notes = NormalizeOptional(notes, 1_000);
    }

    public int AdvanceShippingNoticeId { get; private set; }
    public int LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string EnteredUnitOfMeasure { get; private set; } = string.Empty;
    public decimal ExpectedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal ExpectedBaseQuantity { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal ConversionFactorToBase { get; private set; }
    public int ConversionPrecision { get; private set; }
    public QuantityRoundingMode ConversionRoundingMode { get; private set; }
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = string.Empty;
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public decimal OverDeliveryTolerancePercent { get; private set; }
    public decimal UnderDeliveryTolerancePercent { get; private set; }
    public int? ItemPackagingId { get; private set; }
    public string? ItemPackagingCodeSnapshot { get; private set; }
    public string? ItemPackagingNameSnapshot { get; private set; }
    public string? ItemPackagingUnitOfMeasureSnapshot { get; private set; }
    public decimal? ItemPackagingUnitsPerPackageSnapshot { get; private set; }
    public int? PurchaseOrderId { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }
    public string? PreAdvisedLotNumber { get; private set; }
    public DateTime? PreAdvisedExpiryDate { get; private set; }
    public string? PreAdvisedSerialNumber { get; private set; }
    public string? ExpectedLicensePlateNumber { get; private set; }
    public bool ExpectedLicensePlateIsSscc { get; private set; }
    public string? Notes { get; private set; }
    public bool IsClosed { get; private set; }
    public long Revision { get; private set; } = 1;

    public AdvanceShippingNotice AdvanceShippingNotice { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public ItemPackaging? ItemPackaging { get; private set; }
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; private set; }

    public decimal ReceivedQuantity => ConversionFactorToBase == 0m
        ? 0m
        : ReceivedBaseQuantity / ConversionFactorToBase;
    public decimal RemainingBaseQuantity => Math.Max(0m, ExpectedBaseQuantity - ReceivedBaseQuantity);
    public decimal MinimumCloseBaseQuantity => ExpectedBaseQuantity * (1m - UnderDeliveryTolerancePercent / 100m);
    public decimal MaximumReceivableBaseQuantity => ExpectedBaseQuantity * (1m + OverDeliveryTolerancePercent / 100m);
    public bool IsFullyReceived => ReceivedBaseQuantity >= ExpectedBaseQuantity;
    public bool HasReceiptHistory => ReceivedBaseQuantity > 0m;

    public void RecordReceipt(decimal baseQuantity)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("A closed ASN line cannot receive more quantity.");
        }

        if (baseQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(baseQuantity), "Receipt quantity must be positive.");
        }

        var next = ReceivedBaseQuantity + baseQuantity;
        if (next > MaximumReceivableBaseQuantity)
        {
            throw new InvalidOperationException("The receipt exceeds the ASN line over-delivery tolerance.");
        }

        ReceivedBaseQuantity = next;
        Revision++;
        SetUpdatedAt();
    }

    public void Close()
    {
        if (IsClosed)
        {
            return;
        }

        if (ReceivedBaseQuantity < MinimumCloseBaseQuantity)
        {
            throw new InvalidOperationException("The ASN line cannot close below its under-delivery tolerance.");
        }

        IsClosed = true;
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateIdentity(int lineNumber, int itemId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
    }

    private static decimal ValidatePositive(decimal value, string parameterName) =>
        value > 0m
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "The value must be positive.");

    private static decimal ValidateNonNegative(decimal value, string parameterName) =>
        value >= 0m
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");

    private static decimal ValidatePercentage(decimal value, string parameterName) =>
        value is >= 0m and <= 100m
            ? value
            : throw new ArgumentOutOfRangeException(parameterName, "Tolerance must be between 0 and 100 percent.");

    private static int ValidatePrecision(int value) =>
        value is >= 0 and <= 12
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Precision must be between 0 and 12.");

    private static int? ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }

    private static DateTime? NormalizeDateOnly(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified) : null;

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
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

    private static string NormalizeUnit(string value, string parameterName) =>
        NormalizeRequired(value, 20, parameterName).ToUpperInvariant();

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, nameof(value));

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();
}
