using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// An item-specific directed conversion edge. One unit of
/// <see cref="FromUnitOfMeasure"/> equals <see cref="ConversionFactor"/> units
/// of <see cref="ToUnitOfMeasure"/>.
/// </summary>
public sealed class ItemUnitConversion : Entity
{
    private ItemUnitConversion()
    {
    }

    public ItemUnitConversion(
        int itemId,
        string fromUnitOfMeasure,
        string toUnitOfMeasure,
        decimal conversionFactor,
        int resultPrecision,
        QuantityRoundingMode roundingMode = QuantityRoundingMode.Reject,
        int version = 1,
        bool isActive = true)
    {
        ItemId = itemId;
        FromUnitOfMeasure = NormalizeUnit(fromUnitOfMeasure, nameof(fromUnitOfMeasure));
        ToUnitOfMeasure = NormalizeUnit(toUnitOfMeasure, nameof(toUnitOfMeasure));
        if (string.Equals(FromUnitOfMeasure, ToUnitOfMeasure, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A unit cannot be converted to itself.",
                nameof(toUnitOfMeasure));
        }

        ConversionFactor = ValidateFactor(conversionFactor);
        ResultPrecision = ValidatePrecision(resultPrecision);
        RoundingMode = roundingMode;
        Version = version > 0
            ? version
            : throw new ArgumentOutOfRangeException(nameof(version), "Version must be positive.");
        IsActive = isActive;
    }

    public int ItemId { get; private set; }
    public string FromUnitOfMeasure { get; private set; } = string.Empty;
    public string ToUnitOfMeasure { get; private set; } = string.Empty;
    public decimal ConversionFactor { get; private set; }
    public int ResultPrecision { get; private set; }
    public QuantityRoundingMode RoundingMode { get; private set; }
    public int Version { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Item Item { get; private set; } = null!;

    public void Update(
        decimal conversionFactor,
        int resultPrecision,
        QuantityRoundingMode roundingMode)
    {
        ConversionFactor = ValidateFactor(conversionFactor);
        ResultPrecision = ValidatePrecision(resultPrecision);
        RoundingMode = roundingMode;
        SetUpdatedAt();
    }

    public ItemUnitConversion CreateNextVersion(
        decimal conversionFactor,
        int resultPrecision,
        QuantityRoundingMode roundingMode)
    {
        return new ItemUnitConversion(
            ItemId,
            FromUnitOfMeasure,
            ToUnitOfMeasure,
            conversionFactor,
            resultPrecision,
            roundingMode,
            Version + 1);
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    private static string NormalizeUnit(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A unit of measure is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 20)
        {
            throw new ArgumentException(
                "A unit of measure cannot exceed 20 characters.",
                parameterName);
        }

        return normalized;
    }

    private static decimal ValidateFactor(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The conversion factor must be positive.");
        }

        return value;
    }

    private static int ValidatePrecision(int value)
    {
        if (value is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Result precision must be between 0 and 12 decimal places.");
        }

        return value;
    }
}
