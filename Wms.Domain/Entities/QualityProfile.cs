using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class QualityProfile : Entity
{
    private readonly List<QualityProfileTest> _tests = [];

    private QualityProfile()
    {
    }

    public QualityProfile(
        string code,
        string name,
        string localizedName,
        QualityRiskLevel riskLevel,
        QualitySamplingMethod samplingMethod,
        decimal samplingValue,
        bool isRequired = true,
        int? warehouseId = null,
        int? supplierId = null,
        int? itemId = null,
        string? itemCategory = null,
        string? receiptSourceType = null)
    {
        Code = Required(code, 50, nameof(code), uppercase: true);
        Name = Required(name, 200, nameof(name));
        LocalizedName = Required(localizedName, 200, nameof(localizedName));
        ValidateScope(warehouseId, supplierId, itemId);
        ValidateSampling(samplingMethod, samplingValue);

        WarehouseId = warehouseId;
        SupplierId = supplierId;
        ItemId = itemId;
        ItemCategory = Optional(itemCategory, 100);
        ReceiptSourceType = OptionalUpper(receiptSourceType, 30);
        RiskLevel = riskLevel;
        SamplingMethod = samplingMethod;
        SamplingValue = samplingValue;
        IsRequired = isRequired;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public int? WarehouseId { get; private set; }
    public int? SupplierId { get; private set; }
    public int? ItemId { get; private set; }
    public string? ItemCategory { get; private set; }
    public string? ReceiptSourceType { get; private set; }
    public QualityRiskLevel RiskLevel { get; private set; }
    public QualitySamplingMethod SamplingMethod { get; private set; }
    public decimal SamplingValue { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Revision { get; private set; }

    public Warehouse? Warehouse { get; private set; }
    public Supplier? Supplier { get; private set; }
    public Item? Item { get; private set; }
    public IReadOnlyList<QualityProfileTest> Tests => _tests.AsReadOnly();

    public void Update(
        string name,
        string localizedName,
        QualityRiskLevel riskLevel,
        QualitySamplingMethod samplingMethod,
        decimal samplingValue,
        bool isRequired,
        string? itemCategory,
        string? receiptSourceType)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("An inactive quality profile cannot be edited.");
        }

        ValidateSampling(samplingMethod, samplingValue);
        Name = Required(name, 200, nameof(name));
        LocalizedName = Required(localizedName, 200, nameof(localizedName));
        ItemCategory = Optional(itemCategory, 100);
        ReceiptSourceType = OptionalUpper(receiptSourceType, 30);
        RiskLevel = riskLevel;
        SamplingMethod = samplingMethod;
        SamplingValue = samplingValue;
        IsRequired = isRequired;
        Revision++;
        SetUpdatedAt();
    }

    public void AddTest(QualityProfileTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        if (_tests.Any(existing => string.Equals(existing.Code, test.Code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Quality test code '{test.Code}' is already defined in this profile.");
        }

        _tests.Add(test);
        Revision++;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        Revision++;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        Revision++;
        SetUpdatedAt();
    }

    public decimal CalculateSampleQuantity(decimal receivedQuantity, bool hasLicensePlate, int? licensePlateSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receivedQuantity);

        var sample = SamplingMethod switch
        {
            QualitySamplingMethod.FixedQuantity => SamplingValue,
            QualitySamplingMethod.Percentage => Math.Ceiling(receivedQuantity * SamplingValue / 100m),
            QualitySamplingMethod.FullInspection => receivedQuantity,
            QualitySamplingMethod.EveryNthLicensePlate =>
                hasLicensePlate && licensePlateSequence.HasValue &&
                licensePlateSequence.Value % (int)SamplingValue == 0
                    ? receivedQuantity
                    : 0m,
            _ => throw new InvalidOperationException("Unsupported quality sampling method.")
        };

        return Math.Min(receivedQuantity, Math.Max(0m, sample));
    }

    private static void ValidateScope(int? warehouseId, int? supplierId, int? itemId)
    {
        if (warehouseId is <= 0 || supplierId is <= 0 || itemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId), "Quality profile scope identifiers must be positive when provided.");
        }
    }

    private static void ValidateSampling(QualitySamplingMethod method, decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        if (method is QualitySamplingMethod.FixedQuantity && value <= 0m)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        }

        if (method is QualitySamplingMethod.Percentage && value <= 0m)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        }

        if (method is QualitySamplingMethod.Percentage && value > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A quality percentage must be between 0 and 100.");
        }

        if (method is QualitySamplingMethod.EveryNthLicensePlate && value <= 0m)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        }

        if (method is QualitySamplingMethod.EveryNthLicensePlate && value != decimal.Truncate(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Every-Nth license-plate sampling requires a positive whole number.");
        }
    }

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

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
