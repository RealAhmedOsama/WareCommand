using Wms.Domain.Enums;

namespace Wms.Domain.ValueObjects;

/// <summary>
/// Immutable evidence of how an entered quantity became canonical stock quantity.
/// The snapshot is copied onto the movement so future rule changes cannot rewrite history.
/// </summary>
public sealed record QuantityConversionSnapshot
{
    public QuantityConversionSnapshot(
        decimal enteredQuantity,
        string enteredUnitOfMeasure,
        string baseUnitOfMeasure,
        decimal conversionFactorToBase,
        int resultPrecision,
        QuantityRoundingMode roundingMode,
        decimal roundingDelta,
        string conversionPath,
        string? conversionRuleIds = null,
        PackagingConversionSnapshot? packagingSnapshot = null)
    {
        if (enteredQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(enteredQuantity),
                "Entered quantity cannot be negative.");
        }

        EnteredQuantity = enteredQuantity;
        EnteredUnitOfMeasure = NormalizeUnit(enteredUnitOfMeasure, nameof(enteredUnitOfMeasure));
        BaseUnitOfMeasure = NormalizeUnit(baseUnitOfMeasure, nameof(baseUnitOfMeasure));
        ConversionFactorToBase = ValidateFactor(conversionFactorToBase);
        ResultPrecision = ValidatePrecision(resultPrecision);
        RoundingMode = roundingMode;
        RoundingDelta = ValidateDelta(roundingDelta);
        ConversionPath = NormalizePath(conversionPath);
        ConversionRuleIds = NormalizeRuleIds(conversionRuleIds);
        PackagingSnapshot = packagingSnapshot;
    }

    public decimal EnteredQuantity { get; }
    public string EnteredUnitOfMeasure { get; }
    public string BaseUnitOfMeasure { get; }
    public decimal ConversionFactorToBase { get; }
    public int ResultPrecision { get; }
    public QuantityRoundingMode RoundingMode { get; }
    public decimal RoundingDelta { get; }
    public string ConversionPath { get; }
    public string ConversionRuleIds { get; }
    public PackagingConversionSnapshot? PackagingSnapshot { get; }

    private static string NormalizeUnit(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A unit of measure is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 20)
        {
            throw new ArgumentException("A unit of measure cannot exceed 20 characters.", parameterName);
        }

        return normalized;
    }

    private static decimal ValidateFactor(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                "The conversion factor must be positive.");
        }

        return value;
    }

    private static int ValidatePrecision(int value)
    {
        if (value is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                "Result precision must be between 0 and 12 decimal places.");
        }

        return value;
    }

    private static decimal ValidateDelta(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value),
                "Rounding delta cannot be negative.");
        }

        return value;
    }

    private static string NormalizePath(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A conversion path is required.", nameof(value))
            : value.Trim();

    private static string NormalizeRuleIds(string? value) =>
        value?.Trim() ?? string.Empty;
}
