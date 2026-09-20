using Wms.Domain.Enums;

namespace Wms.Domain.ValueObjects;

/// <summary>
/// Immutable packaging master-data evidence copied to a quantity conversion.
/// A movement stores this snapshot so later packaging edits cannot rewrite history.
/// </summary>
public sealed record PackagingConversionSnapshot
{
    public PackagingConversionSnapshot(
        int packagingId,
        int version,
        string code,
        string name,
        string localizedName,
        PackagingType type,
        string unitOfMeasure,
        decimal unitsPerPackage,
        PackagingPartialPolicy partialPackagePolicy,
        decimal? grossWeightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm,
        decimal? volumeCubicMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packagingId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        PackagingId = packagingId;
        Version = version;
        Code = Normalize(code, 40, nameof(code), uppercase: true);
        Name = Normalize(name, 200, nameof(name));
        LocalizedName = Normalize(localizedName, 200, nameof(localizedName));
        Type = type;
        UnitOfMeasure = Normalize(unitOfMeasure, 20, nameof(unitOfMeasure), uppercase: true);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerPackage);

        UnitsPerPackage = unitsPerPackage;
        PartialPackagePolicy = partialPackagePolicy;
        GrossWeightKg = ValidateNonNegative(grossWeightKg, nameof(grossWeightKg));
        LengthCm = ValidateNonNegative(lengthCm, nameof(lengthCm));
        WidthCm = ValidateNonNegative(widthCm, nameof(widthCm));
        HeightCm = ValidateNonNegative(heightCm, nameof(heightCm));
        VolumeCubicMeters = ValidateNonNegative(volumeCubicMeters, nameof(volumeCubicMeters));
    }

    public int PackagingId { get; }
    public int Version { get; }
    public string Code { get; }
    public string Name { get; }
    public string LocalizedName { get; }
    public PackagingType Type { get; }
    public string UnitOfMeasure { get; }
    public decimal UnitsPerPackage { get; }
    public PackagingPartialPolicy PartialPackagePolicy { get; }
    public decimal? GrossWeightKg { get; }
    public decimal? LengthCm { get; }
    public decimal? WidthCm { get; }
    public decimal? HeightCm { get; }
    public decimal? VolumeCubicMeters { get; }

    private static string Normalize(
        string value,
        int maximumLength,
        string parameterName,
        bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (uppercase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static decimal? ValidateNonNegative(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
    }
}
