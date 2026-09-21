using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Outbound demand quantities and the master/UOM/packaging snapshots captured
/// for the order line. Allocation and warehouse execution update only the
/// quantity ledger fields; snapshots never follow later master edits.
/// </summary>
public sealed class SalesOrderLine : Entity
{
    private SalesOrderLine()
    {
    }

    public SalesOrderLine(
        int lineNumber,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        string? itemLocalizedNameSnapshot,
        string? customerItemSkuSnapshot,
        string orderedUnitOfMeasure,
        decimal orderedQuantity,
        string baseUnitOfMeasure,
        decimal orderedBaseQuantity,
        decimal conversionFactorToBase,
        int conversionPrecision,
        string conversionPath,
        string conversionRuleIds,
        string? packagingCodeSnapshot = null,
        int? packagingVersionSnapshot = null,
        decimal? packagingUnitsPerPackageSnapshot = null,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        LineNumber = lineNumber;
        ItemId = itemId;
        ItemSkuSnapshot = NormalizeRequired(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = NormalizeRequired(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        ItemLocalizedNameSnapshot = NormalizeOptional(itemLocalizedNameSnapshot, 200);
        CustomerItemSkuSnapshot = NormalizeOptionalUpper(customerItemSkuSnapshot, 100);
        OrderedUnitOfMeasure = NormalizeUnit(orderedUnitOfMeasure, nameof(orderedUnitOfMeasure));
        OrderedQuantity = ValidatePositive(orderedQuantity, nameof(orderedQuantity));
        BaseUnitOfMeasure = NormalizeUnit(baseUnitOfMeasure, nameof(baseUnitOfMeasure));
        OrderedBaseQuantity = ValidatePositive(orderedBaseQuantity, nameof(orderedBaseQuantity));
        ConversionFactorToBase = ValidatePositive(conversionFactorToBase, nameof(conversionFactorToBase));
        ConversionPrecision = conversionPrecision is >= 0 and <= 12
            ? conversionPrecision
            : throw new ArgumentOutOfRangeException(nameof(conversionPrecision));
        ConversionPath = NormalizeRequired(conversionPath, 500, nameof(conversionPath));
        ConversionRuleIds = NormalizeOptional(conversionRuleIds, 500) ?? string.Empty;
        PackagingCodeSnapshot = NormalizeOptionalUpper(packagingCodeSnapshot, 100);
        PackagingVersionSnapshot = packagingVersionSnapshot is <= 0
            ? throw new ArgumentOutOfRangeException(nameof(packagingVersionSnapshot))
            : packagingVersionSnapshot;
        PackagingUnitsPerPackageSnapshot = packagingUnitsPerPackageSnapshot is <= 0
            ? throw new ArgumentOutOfRangeException(nameof(packagingUnitsPerPackageSnapshot))
            : packagingUnitsPerPackageSnapshot;
        Notes = NormalizeOptional(notes, 1_000);
    }

    public int SalesOrderId { get; private set; }
    public int LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string? ItemLocalizedNameSnapshot { get; private set; }
    public string? CustomerItemSkuSnapshot { get; private set; }
    public string OrderedUnitOfMeasure { get; private set; } = string.Empty;
    public decimal OrderedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal OrderedBaseQuantity { get; private set; }
    public decimal AllocatedBaseQuantity { get; private set; }
    public decimal PickedBaseQuantity { get; private set; }
    public decimal PackedBaseQuantity { get; private set; }
    public decimal ShippedBaseQuantity { get; private set; }
    public decimal CancelledBaseQuantity { get; private set; }
    public decimal ConversionFactorToBase { get; private set; }
    public int ConversionPrecision { get; private set; }
    public string ConversionPath { get; private set; } = string.Empty;
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public string? PackagingCodeSnapshot { get; private set; }
    public int? PackagingVersionSnapshot { get; private set; }
    public decimal? PackagingUnitsPerPackageSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; } = 1;

    public SalesOrder SalesOrder { get; private set; } = null!;
    public Item Item { get; private set; } = null!;

    public decimal BackorderBaseQuantity => Math.Max(
        0m,
        OrderedBaseQuantity -
        AllocatedBaseQuantity -
        CancelledBaseQuantity);

    public decimal RemainingToPickBaseQuantity => Math.Max(
        0m,
        AllocatedBaseQuantity - PickedBaseQuantity);

    public decimal RemainingToPackBaseQuantity => Math.Max(
        0m,
        PickedBaseQuantity - PackedBaseQuantity);

    public decimal RemainingToShipBaseQuantity => Math.Max(
        0m,
        PackedBaseQuantity - ShippedBaseQuantity);

    public void RecordAllocation(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (AllocatedBaseQuantity + baseQuantity > OrderedBaseQuantity - CancelledBaseQuantity)
        {
            throw new InvalidOperationException("Allocation cannot exceed uncancelled ordered quantity.");
        }

        AllocatedBaseQuantity += baseQuantity;
        Touch();
    }

    public void ReleaseAllocation(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (baseQuantity > AllocatedBaseQuantity - PickedBaseQuantity)
        {
            throw new InvalidOperationException("Released allocation exceeds the unpicked allocation quantity.");
        }

        AllocatedBaseQuantity -= baseQuantity;
        Touch();
    }

    public void RecordPicked(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (PickedBaseQuantity + baseQuantity > AllocatedBaseQuantity)
        {
            throw new InvalidOperationException("Picked quantity cannot exceed allocated quantity.");
        }

        PickedBaseQuantity += baseQuantity;
        Touch();
    }

    public void RecordPacked(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (PackedBaseQuantity + baseQuantity > PickedBaseQuantity)
        {
            throw new InvalidOperationException("Packed quantity cannot exceed picked quantity.");
        }

        PackedBaseQuantity += baseQuantity;
        Touch();
    }

    public void ReversePacked(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (baseQuantity > PackedBaseQuantity - ShippedBaseQuantity)
        {
            throw new InvalidOperationException(
                "Packed quantity cannot be reversed after it has been shipped.");
        }

        PackedBaseQuantity -= baseQuantity;
        Touch();
    }

    public void RecordShipped(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        if (ShippedBaseQuantity + baseQuantity > PackedBaseQuantity)
        {
            throw new InvalidOperationException("Shipped quantity cannot exceed packed quantity.");
        }

        ShippedBaseQuantity += baseQuantity;
        Touch();
    }

    public void CancelQuantity(decimal baseQuantity)
    {
        ValidateIncrement(baseQuantity, nameof(baseQuantity));
        var remaining = OrderedBaseQuantity - CancelledBaseQuantity - ShippedBaseQuantity;
        if (baseQuantity > remaining || baseQuantity > OrderedBaseQuantity - AllocatedBaseQuantity)
        {
            throw new InvalidOperationException("Cancelled quantity must be unallocated and not already shipped.");
        }

        CancelledBaseQuantity += baseQuantity;
        Touch();
    }

    public void Substitute(Item substituteItem, string reason)
    {
        ArgumentNullException.ThrowIfNull(substituteItem);
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A substitution reason is required.", nameof(reason));
        }

        if (PickedBaseQuantity > 0m || PackedBaseQuantity > 0m || ShippedBaseQuantity > 0m)
        {
            throw new InvalidOperationException(
                "An order line cannot be substituted after picking or packing has started.");
        }

        ItemId = substituteItem.Id;
        ItemSkuSnapshot = NormalizeRequired(substituteItem.Sku, 50, nameof(substituteItem.Sku));
        ItemNameSnapshot = NormalizeRequired(substituteItem.Name, 200, nameof(substituteItem.Name));
        ItemLocalizedNameSnapshot = NormalizeOptional(substituteItem.LocalizedName, 200);
        Notes = NormalizeOptional($"Substituted: {reason}", 1_000);
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateIncrement(decimal value, string parameterName)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quantity must be positive.");
        }
    }

    private static decimal ValidatePositive(decimal value, string parameterName) =>
        value > 0m ? value : throw new ArgumentOutOfRangeException(parameterName, "Quantity must be positive.");

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

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();

    private static string NormalizeUnit(string value, string parameterName) =>
        NormalizeRequired(value, 20, parameterName).ToUpperInvariant();
}
