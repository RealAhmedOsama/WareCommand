using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Identification;

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
        bool isDefault = false,
        string? name = null,
        string? localizedName = null,
        string? gtin = null,
        string? parentPackagingCode = null,
        PackagingType type = PackagingType.Other,
        PackagingPartialPolicy partialPackagePolicy = PackagingPartialPolicy.Reject,
        bool isDefaultReceiving = false,
        bool isDefaultStorage = false,
        bool isDefaultPicking = false,
        bool isDefaultShipping = false,
        bool isActive = true,
        int version = 1)
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
        VolumeCubicMeters = CalculateVolume(LengthCm, WidthCm, HeightCm);
        Name = NormalizeOptionalText(name, 200) ?? Code;
        LocalizedName = NormalizeOptionalText(localizedName, 200) ?? Name;
        Gtin = NormalizeGtin(gtin);
        ParentPackagingCode = NormalizeParentCode(parentPackagingCode, Code);
        Type = type;
        PartialPackagePolicy = partialPackagePolicy;
        IsDefault = isDefault || isDefaultStorage;
        IsDefaultReceiving = isDefaultReceiving;
        IsDefaultStorage = isDefaultStorage || isDefault;
        IsDefaultPicking = isDefaultPicking;
        IsDefaultShipping = isDefaultShipping;
        IsActive = isActive;
        Version = ValidateVersion(version);
    }

    public int ItemId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public decimal UnitsPerPackage { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public string? Barcode { get; private set; }
    public string? Gtin { get; private set; }
    public string? ParentPackagingCode { get; private set; }
    public PackagingType Type { get; private set; } = PackagingType.Other;
    public PackagingPartialPolicy PartialPackagePolicy { get; private set; }
    public decimal? GrossWeightKg { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? VolumeCubicMeters { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsDefaultReceiving { get; private set; }
    public bool IsDefaultStorage { get; private set; }
    public bool IsDefaultPicking { get; private set; }
    public bool IsDefaultShipping { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Version { get; private set; } = 1;

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
        bool isDefault,
        string? name = null,
        string? localizedName = null,
        string? gtin = null,
        string? parentPackagingCode = null,
        PackagingType type = PackagingType.Other,
        PackagingPartialPolicy partialPackagePolicy = PackagingPartialPolicy.Reject,
        bool isDefaultReceiving = false,
        bool isDefaultStorage = false,
        bool isDefaultPicking = false,
        bool isDefaultShipping = false,
        bool isActive = true)
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
        VolumeCubicMeters = CalculateVolume(LengthCm, WidthCm, HeightCm);
        Name = NormalizeOptionalText(name, 200) ?? Code;
        LocalizedName = NormalizeOptionalText(localizedName, 200) ?? Name;
        Gtin = NormalizeGtin(gtin);
        ParentPackagingCode = NormalizeParentCode(parentPackagingCode, Code);
        Type = type;
        PartialPackagePolicy = partialPackagePolicy;
        IsDefault = isDefault || isDefaultStorage;
        IsDefaultReceiving = isDefaultReceiving;
        IsDefaultStorage = isDefaultStorage || isDefault;
        IsDefaultPicking = isDefaultPicking;
        IsDefaultShipping = isDefaultShipping;
        IsActive = isActive;
        Version = checked(Version + 1);
        SetUpdatedAt();
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        IsDefaultStorage = isDefault;
        SetUpdatedAt();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        SetUpdatedAt();
    }

    public void ValidateNesting(IReadOnlyDictionary<string, ItemPackaging> packagingByCode)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = this;
        while (current.ParentPackagingCode is not null)
        {
            if (!visited.Add(current.Code))
            {
                throw new ArgumentException(
                    $"Packaging nesting contains a cycle at '{current.Code}'.");
            }

            var parentCode = current.ParentPackagingCode;
            if (!packagingByCode.TryGetValue(parentCode, out var parent))
            {
                throw new ArgumentException(
                    $"Parent packaging '{parentCode}' was not found for '{Code}'.");
            }

            current = parent;
        }
    }

    public bool HasPhysicalCapacityData =>
        GrossWeightKg.HasValue || VolumeCubicMeters.HasValue;

    private static decimal? ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    private static decimal? CalculateVolume(
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm)
    {
        if (!lengthCm.HasValue || !widthCm.HasValue || !heightCm.HasValue)
        {
            return null;
        }

        return lengthCm.Value * widthCm.Value * heightCm.Value / 1_000_000m;
    }

    private static int ValidateVersion(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Packaging version must be positive.");
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

    private static string? NormalizeOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeGtin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        try
        {
            return BarcodeParser.NormalizeProductCode(normalized);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"GTIN is invalid: {exception.Message}",
                nameof(value),
                exception);
        }
    }

    private static string? NormalizeParentCode(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 40)
        {
            throw new ArgumentException(
                "Parent packaging code cannot exceed 40 characters.",
                nameof(value));
        }

        if (string.Equals(normalized, code, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A packaging definition cannot contain itself.",
                nameof(value));
        }

        return normalized;
    }
}
