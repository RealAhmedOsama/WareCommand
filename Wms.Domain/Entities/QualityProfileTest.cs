using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class QualityProfileTest : Entity
{
    private QualityProfileTest()
    {
    }

    public QualityProfileTest(
        int sequence,
        string code,
        string name,
        string localizedName,
        QualityMeasurementType measurementType,
        bool isRequired = true,
        decimal? minimumValue = null,
        decimal? maximumValue = null,
        string? allowedValues = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);

        if (minimumValue.HasValue && maximumValue.HasValue && minimumValue > maximumValue)
        {
            throw new ArgumentException("A quality test minimum cannot exceed its maximum.");
        }

        Sequence = sequence;
        Code = Required(code, 50, nameof(code), uppercase: true);
        Name = Required(name, 200, nameof(name));
        LocalizedName = Required(localizedName, 200, nameof(localizedName));
        MeasurementType = measurementType;
        IsRequired = isRequired;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        AllowedValues = Optional(allowedValues, 2_000);
    }

    public int QualityProfileId { get; private set; }
    public int Sequence { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public QualityMeasurementType MeasurementType { get; private set; }
    public bool IsRequired { get; private set; }
    public decimal? MinimumValue { get; private set; }
    public decimal? MaximumValue { get; private set; }
    public string? AllowedValues { get; private set; }

    public QualityProfile Profile { get; private set; } = null!;

    private static string Required(string value, int maximumLength, string parameterName, bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        return uppercase ? normalized.ToUpperInvariant() : normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }
}
