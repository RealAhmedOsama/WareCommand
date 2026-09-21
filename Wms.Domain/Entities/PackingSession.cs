using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PackingSession : Entity
{
    private readonly List<ShipmentPackage> _packages = [];

    private PackingSession()
    {
    }

    public PackingSession(
        string sessionNumber,
        int warehouseId,
        int stationId,
        PackingSourceType sourceType,
        string sourceReference,
        int? salesOrderId,
        int? stagingLicensePlateId,
        string userId,
        DateTime startedAtUtc)
    {
        SessionNumber = Required(sessionNumber, 80, nameof(sessionNumber));
        SourceReference = Required(sourceReference, 200, nameof(sourceReference));
        UserId = Required(userId, 450, nameof(userId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stationId);
        if (salesOrderId is <= 0 || stagingLicensePlateId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(salesOrderId));
        }

        if (!Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        WarehouseId = warehouseId;
        PackingStationId = stationId;
        SourceType = sourceType;
        SalesOrderId = salesOrderId;
        StagingLicensePlateId = stagingLicensePlateId;
        Status = PackingSessionStatus.InProgress;
        StartedAtUtc = NormalizeUtc(startedAtUtc);
        Revision = 1;
    }

    public string SessionNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int PackingStationId { get; private set; }
    public PackingSourceType SourceType { get; private set; }
    public string SourceReference { get; private set; } = string.Empty;
    public int? SalesOrderId { get; private set; }
    public int? StagingLicensePlateId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public PackingSessionStatus Status { get; private set; } = PackingSessionStatus.InProgress;
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public PackingStation PackingStation { get; private set; } = null!;
    public SalesOrder? SalesOrder { get; private set; }
    public LicensePlate? StagingLicensePlate { get; private set; }
    public IReadOnlyList<ShipmentPackage> Packages => _packages.AsReadOnly();

    public bool IsTerminal => Status is PackingSessionStatus.Completed or PackingSessionStatus.Cancelled;

    public void AddPackage(ShipmentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (IsTerminal)
        {
            throw new InvalidOperationException("A terminal packing session cannot receive another package.");
        }

        if (package.PackingSessionId != 0 && package.PackingSessionId != Id)
        {
            throw new InvalidOperationException("The package already belongs to another packing session.");
        }

        _packages.Add(package);
        Touch();
    }

    public void Complete(string userId, DateTime completedAtUtc)
    {
        if (Status != PackingSessionStatus.InProgress)
        {
            throw new InvalidOperationException($"A packing session in {Status} cannot be completed.");
        }

        if (_packages.Any(package => package.Status == ShipmentPackageStatus.Open))
        {
            throw new InvalidOperationException("Every package must be closed before the packing session completes.");
        }

        UserId = Required(userId, 450, nameof(userId));
        Status = PackingSessionStatus.Completed;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Touch();
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A packing session in {Status} cannot be cancelled.");
        }

        UserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        Status = PackingSessionStatus.Cancelled;
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

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
}
