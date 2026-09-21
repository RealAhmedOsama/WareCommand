using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ShipmentPackage : Entity
{
    private readonly List<ShipmentPackageContent> _contents = [];

    private ShipmentPackage()
    {
    }

    public ShipmentPackage(
        string packageNumber,
        int warehouseId,
        int packingSessionId,
        int targetLicensePlateId,
        PackingPackageType packageType,
        int? salesOrderId,
        bool allowConsolidated,
        decimal? expectedWeightKg,
        decimal weightToleranceKg,
        decimal weightTolerancePercent,
        string? labelReference = null)
    {
        PackageNumber = Required(packageNumber, 80, nameof(packageNumber));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(packingSessionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetLicensePlateId);
        if (salesOrderId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(salesOrderId));
        }

        if (!Enum.IsDefined(packageType))
        {
            throw new ArgumentOutOfRangeException(nameof(packageType));
        }

        ValidateTolerance(expectedWeightKg, weightToleranceKg, weightTolerancePercent);
        WarehouseId = warehouseId;
        PackingSessionId = packingSessionId;
        TargetLicensePlateId = targetLicensePlateId;
        PackageType = packageType;
        SalesOrderId = salesOrderId;
        AllowConsolidated = allowConsolidated;
        ExpectedWeightKg = expectedWeightKg;
        WeightToleranceKg = weightToleranceKg;
        WeightTolerancePercent = weightTolerancePercent;
        LabelReference = Optional(labelReference, 250);
        Status = ShipmentPackageStatus.Open;
        Revision = 1;
    }

    public string PackageNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int PackingSessionId { get; private set; }
    public int TargetLicensePlateId { get; private set; }
    public PackingPackageType PackageType { get; private set; }
    public int? SalesOrderId { get; private set; }
    public bool AllowConsolidated { get; private set; }
    public ShipmentPackageStatus Status { get; private set; } = ShipmentPackageStatus.Open;
    public decimal? ExpectedWeightKg { get; private set; }
    public decimal? ActualWeightKg { get; private set; }
    public decimal WeightToleranceKg { get; private set; }
    public decimal WeightTolerancePercent { get; private set; }
    public decimal? LengthCm { get; private set; }
    public decimal? WidthCm { get; private set; }
    public decimal? HeightCm { get; private set; }
    public decimal? VolumeCubicMeters { get; private set; }
    public string? LabelReference { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ReopenReason { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidedByUserId { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public PackingSession PackingSession { get; private set; } = null!;
    public LicensePlate TargetLicensePlate { get; private set; } = null!;
    public SalesOrder? SalesOrder { get; private set; }
    public IReadOnlyList<ShipmentPackageContent> Contents => _contents.AsReadOnly();

    public decimal PackedQuantity => _contents.Sum(content => content.Quantity);

    public void AddContent(ShipmentPackageContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureOpen();
        if (content.ShipmentPackageId != 0 && content.ShipmentPackageId != Id)
        {
            throw new InvalidOperationException("The package content already belongs to another package.");
        }

        _contents.Add(content);
        Touch();
    }

    public void RemoveContent(ShipmentPackageContent content, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureOpen();
        if (quantity <= 0m || quantity > content.Quantity)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        content.RemoveQuantity(quantity);
        if (content.Quantity == 0m)
        {
            _contents.Remove(content);
        }

        Touch();
    }

    public void Close(
        string userId,
        DateTime closedAtUtc,
        decimal? actualWeightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm)
    {
        EnsureOpen();
        if (_contents.Count == 0)
        {
            throw new InvalidOperationException("A package cannot close without verified contents.");
        }

        ValidateMeasurement(actualWeightKg, lengthCm, widthCm, heightCm);
        if (ExpectedWeightKg.HasValue && actualWeightKg.HasValue)
        {
            var variance = Math.Abs(actualWeightKg.Value - ExpectedWeightKg.Value);
            var allowed = Math.Max(
                WeightToleranceKg,
                ExpectedWeightKg.Value * WeightTolerancePercent / 100m);
            if (variance > allowed)
            {
                throw new InvalidOperationException(
                    $"Package weight variance {variance:0.####} kg exceeds the allowed tolerance {allowed:0.####} kg.");
            }
        }

        ClosedByUserId = Required(userId, 450, nameof(userId));
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        ActualWeightKg = actualWeightKg;
        LengthCm = lengthCm;
        WidthCm = widthCm;
        HeightCm = heightCm;
        VolumeCubicMeters = CalculateVolume(lengthCm, widthCm, heightCm);
        Status = ShipmentPackageStatus.Closed;
        Touch();
    }

    public void Reopen(string reason)
    {
        if (Status != ShipmentPackageStatus.Closed)
        {
            throw new InvalidOperationException("Only a closed package can be reopened.");
        }

        ReopenReason = Required(reason, 1_000, nameof(reason));
        Status = ShipmentPackageStatus.Open;
        ClosedByUserId = null;
        ClosedAtUtc = null;
        Touch();
    }

    public void Void(string userId, string reason, DateTime voidedAtUtc)
    {
        if (Status == ShipmentPackageStatus.Voided)
        {
            throw new InvalidOperationException("The package is already voided.");
        }

        VoidedByUserId = Required(userId, 450, nameof(userId));
        VoidReason = Required(reason, 1_000, nameof(reason));
        VoidedAtUtc = NormalizeUtc(voidedAtUtc);
        Status = ShipmentPackageStatus.Voided;
        Touch();
    }

    public void MarkShipped(DateTime shippedAtUtc)
    {
        if (Status != ShipmentPackageStatus.Closed)
        {
            throw new InvalidOperationException("Only a closed package can be shipped.");
        }

        Status = ShipmentPackageStatus.Shipped;
        ClosedAtUtc ??= DateTime.SpecifyKind(shippedAtUtc, DateTimeKind.Utc);
        Touch();
    }

    private void EnsureOpen()
    {
        if (Status != ShipmentPackageStatus.Open)
        {
            throw new InvalidOperationException($"A package in {Status} cannot be changed.");
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateTolerance(
        decimal? expectedWeightKg,
        decimal weightToleranceKg,
        decimal weightTolerancePercent)
    {
        if (expectedWeightKg is < 0m || weightToleranceKg < 0m || weightTolerancePercent < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedWeightKg));
        }
    }

    private static void ValidateMeasurement(
        decimal? weightKg,
        decimal? lengthCm,
        decimal? widthCm,
        decimal? heightCm)
    {
        if (weightKg is < 0m || lengthCm is < 0m || widthCm is < 0m || heightCm is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg));
        }
    }

    private static decimal? CalculateVolume(decimal? lengthCm, decimal? widthCm, decimal? heightCm) =>
        lengthCm.HasValue && widthCm.HasValue && heightCm.HasValue
            ? lengthCm.Value * widthCm.Value * heightCm.Value / 1_000_000m
            : null;

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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, nameof(value));
}
