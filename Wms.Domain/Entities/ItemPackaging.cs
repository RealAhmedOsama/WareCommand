using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ItemPackaging : Entity
{
    private ItemPackaging()
    {
    }

    public ItemPackaging(
        string code,
        string unitOfMeasure,
        decimal unitsPerPackage,
        string? barcode = null,
        decimal? grossWeightKg = null,
        decimal? lengthCm = null,
        decimal? widthCm = null,
        decimal? heightCm = null,
        bool isDefault = false)
    {
        Code = NormalizeRequired(code, 40, nameof(code));
        UnitOfMeasure = NormalizeRequired(unitOfMeasure, 20, nameof(unitOfMeasure));
        if (unitsPerPackage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerPackage), "Units per package must be positive.");
        }

        UnitsPerPackage = unitsPerPackage;
        Barcode = NormalizeOptional(barcode, 50);
        GrossWeightKg = ValidateNonNegative(grossWeightKg, nameof(grossWeightKg));
        LengthCm = ValidateNonNegative(lengthCm, nameof(lengthCm));
        WidthCm = ValidateNonNegative(widthCm, nameof(widthCm));
        HeightCm = ValidateNonNegative(heightCm, nameof(heightCm));
        IsDefault = isDefault;
    }

    public int ItemId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public decimal UnitsPerPackage { get; private set; }
    public string? Barcode { get; private set; }
    public decimal? GrossWeightKg { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public bool IsDefault { get; private set; }

    public Item Item { get; private set; } = null!;

    public void Update(
        string code,
        string unitOfMeasure,
        decimal unitsPerPackage,
        string? barcode,
        decimal? grossWeightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm,
        bool isDefault)
    {
        Code = NormalizeRequired(code, 40, nameof(code));
        UnitOfMeasure = NormalizeRequired(unitOfMeasure, 20, nameof(unitOfMeasure));
        if (unitsPerPackage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerPackage), "Units per package must be positive.");
        }

        UnitsPerPackage = unitsPerPackage;
        Barcode = NormalizeOptional(barcode, 50);
        GrossWeightKg = ValidateNonNegative(grossWeightKg, nameof(grossWeightKg));
        LengthCm = ValidateNonNegative(lengthCm, nameof(lengthCm));
        WidthCm = ValidateNonNegative(widthCm, nameof(widthCm));
        HeightCm = ValidateNonNegative(heightCm, nameof(heightCm));
        IsDefault = isDefault;
        SetUpdatedAt();
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        SetUpdatedAt();
    }

    private static decimal? ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length < 3)
        {
            throw new ArgumentException("Barcode must contain at least 3 characters.", nameof(value));
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
