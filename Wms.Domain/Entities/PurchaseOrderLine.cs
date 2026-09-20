using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// An immutable-at-confirmation demand line. Quantities are stored in both the
/// entered order unit and the canonical item unit so later UOM master changes
/// cannot alter the meaning of the document.
/// </summary>
public sealed class PurchaseOrderLine : Entity
{
    private PurchaseOrderLine()
    {
    }

    public PurchaseOrderLine(
        int lineNumber,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        string orderedUnitOfMeasure,
        decimal orderedQuantity,
        string baseUnitOfMeasure,
        decimal orderedBaseQuantity,
        decimal conversionFactorToBase,
        int conversionPrecision,
        QuantityRoundingMode conversionRoundingMode,
        decimal conversionRoundingDelta,
        string conversionPath,
        string conversionRuleIds,
        decimal overDeliveryTolerancePercent = 0,
        decimal underDeliveryTolerancePercent = 0,
        string? supplierItemReferenceSnapshot = null,
        string? notes = null)
    {
        ValidateIdentity(lineNumber, itemId);
        LineNumber = lineNumber;
        ItemId = itemId;
        ItemSkuSnapshot = NormalizeRequired(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = NormalizeRequired(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        OrderedUnitOfMeasure = NormalizeUnit(orderedUnitOfMeasure, nameof(orderedUnitOfMeasure));
        BaseUnitOfMeasure = NormalizeUnit(baseUnitOfMeasure, nameof(baseUnitOfMeasure));
        OrderedQuantity = ValidatePositive(orderedQuantity, nameof(orderedQuantity));
        OrderedBaseQuantity = ValidatePositive(orderedBaseQuantity, nameof(orderedBaseQuantity));
        ConversionFactorToBase = ValidatePositive(conversionFactorToBase, nameof(conversionFactorToBase));
        ConversionPrecision = ValidatePrecision(conversionPrecision);
        ConversionRoundingMode = conversionRoundingMode;
        ConversionRoundingDelta = ValidateNonNegative(conversionRoundingDelta, nameof(conversionRoundingDelta));
        ConversionPath = NormalizeRequired(conversionPath, 500, nameof(conversionPath));
        ConversionRuleIds = NormalizeOptional(conversionRuleIds, 500) ?? string.Empty;
        OverDeliveryTolerancePercent = ValidatePercentage(
            overDeliveryTolerancePercent,
            nameof(overDeliveryTolerancePercent));
        UnderDeliveryTolerancePercent = ValidatePercentage(
            underDeliveryTolerancePercent,
            nameof(underDeliveryTolerancePercent));
        SupplierItemReferenceSnapshot = NormalizeOptional(supplierItemReferenceSnapshot, 100);
        Notes = NormalizeOptional(notes, 1_000);
    }

    public int PurchaseOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string OrderedUnitOfMeasure { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal OrderedBaseQuantity { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal ConversionFactorToBase { get; private set; }
    public int ConversionPrecision { get; private set; }
    public QuantityRoundingMode ConversionRoundingMode { get; private set; }
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = string.Empty;
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public decimal OverDeliveryTolerancePercent { get; private set; }
    public decimal UnderDeliveryTolerancePercent { get; private set; }
    public string? SupplierItemReferenceSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public bool IsClosed { get; private set; }
    public long Revision { get; private set; } = 1;

    public PurchaseOrder PurchaseOrder { get; private set; } = null!;
    public Item Item { get; private set; } = null!;

    public decimal ReceivedQuantity => ReceivedBaseQuantity / ConversionFactorToBase;
    public decimal RemainingBaseQuantity => Math.Max(0m, OrderedBaseQuantity - ReceivedBaseQuantity);
    public decimal MinimumCloseBaseQuantity =>
        OrderedBaseQuantity * (1m - UnderDeliveryTolerancePercent / 100m);
    public decimal MaximumReceivableBaseQuantity =>
        OrderedBaseQuantity * (1m + OverDeliveryTolerancePercent / 100m);
    public bool IsFullyReceived => ReceivedBaseQuantity >= OrderedBaseQuantity;
    public bool HasReceiptHistory => ReceivedBaseQuantity > 0m;

    public void RecordReceipt(decimal baseQuantity)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException("A closed purchase-order line cannot receive more quantity.");
        }

        if (baseQuantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(baseQuantity), "Receipt quantity must be positive.");
        }

        var next = ReceivedBaseQuantity + baseQuantity;
        if (next > MaximumReceivableBaseQuantity)
        {
            throw new InvalidOperationException(
                "The receipt exceeds the purchase-order line over-delivery tolerance.");
        }

        ReceivedBaseQuantity = next;
        Revision++;
        SetUpdatedAt();
    }

    public void ReverseReceipt(decimal baseQuantity)
    {
        if (baseQuantity <= 0m || baseQuantity > ReceivedBaseQuantity)
        {
            throw new InvalidOperationException("The purchase-order receipt reversal exceeds line history.");
        }

        ReceivedBaseQuantity -= baseQuantity;
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
            throw new InvalidOperationException(
                "The purchase-order line cannot close below its under-delivery tolerance.");
        }

        IsClosed = true;
        Revision++;
        SetUpdatedAt();
    }

    public void Reopen()
    {
        if (!IsClosed)
        {
            return;
        }

        IsClosed = false;
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

    private static int ValidatePrecision(int value) =>
        value is >= 0 and <= 12
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), "Precision must be between 0 and 12.");

    private static decimal ValidatePercentage(decimal value, string parameterName) =>
        value is >= 0m and <= 100m
            ? value
            : throw new ArgumentOutOfRangeException(
                parameterName,
                "Tolerance must be between 0 and 100 percent.");

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

    private static string? NormalizeOptional(string? value, int maximumLength)
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

    private static string NormalizeUnit(string value, string parameterName) =>
        NormalizeRequired(value, 20, parameterName).ToUpperInvariant();
}
