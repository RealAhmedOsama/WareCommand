using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class QualityInspectionTestResult : Entity
{
    private QualityInspectionTestResult()
    {
    }

    public QualityInspectionTestResult(
        int qualityProfileTestId,
        string testCodeSnapshot,
        string testNameSnapshot,
        QualityMeasurementType measurementType,
        decimal inspectedQuantity,
        string? recordedValue,
        decimal? numericValue,
        bool? booleanValue,
        bool passed,
        string? notes,
        string? attachmentReferences,
        string userId,
        DateTime recordedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qualityProfileTestId);

        QualityProfileTestId = qualityProfileTestId;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inspectedQuantity);

        InspectedQuantity = inspectedQuantity;
        TestCodeSnapshot = Required(testCodeSnapshot, 50, nameof(testCodeSnapshot));
        TestNameSnapshot = Required(testNameSnapshot, 200, nameof(testNameSnapshot));
        MeasurementType = measurementType;
        RecordedValue = Optional(recordedValue, 500);
        NumericValue = numericValue;
        BooleanValue = booleanValue;
        Passed = passed;
        Notes = Optional(notes, 1_000);
        AttachmentReferences = Optional(attachmentReferences, 2_000);
        RecordedByUserId = Required(userId, 450, nameof(userId));
        RecordedAtUtc = NormalizeUtc(recordedAtUtc);
    }

    public int QualityInspectionId { get; private set; }
    public int QualityProfileTestId { get; private set; }
    public string TestCodeSnapshot { get; private set; } = string.Empty;
    public string TestNameSnapshot { get; private set; } = string.Empty;
    public QualityMeasurementType MeasurementType { get; private set; }
    public decimal InspectedQuantity { get; private set; }
    public string? RecordedValue { get; private set; }
    public decimal? NumericValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public bool Passed { get; private set; }
    public string? Notes { get; private set; }
    public string? AttachmentReferences { get; private set; }
    public string RecordedByUserId { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    public QualityInspection Inspection { get; private set; } = null!;
    public QualityProfileTest ProfileTest { get; private set; } = null!;

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
