using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PickingPlanContainer : Entity
{
    private PickingPlanContainer()
    {
    }

    public PickingPlanContainer(
        int planId,
        int sequence,
        string containerKey,
        string expectedScanCode,
        bool targetScanRequired,
        int? salesOrderId = null,
        int? zoneLocationId = null,
        int? targetLicensePlateId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ValidateOptionalId(salesOrderId, nameof(salesOrderId));
        ValidateOptionalId(zoneLocationId, nameof(zoneLocationId));
        ValidateOptionalId(targetLicensePlateId, nameof(targetLicensePlateId));
        PickingPlanId = planId;
        Sequence = sequence;
        ContainerKey = RequiredUpper(containerKey, 120, nameof(containerKey));
        ExpectedScanCode = RequiredUpper(expectedScanCode, 120, nameof(expectedScanCode));
        TargetScanRequired = targetScanRequired;
        SalesOrderId = salesOrderId;
        ZoneLocationId = zoneLocationId;
        TargetLicensePlateId = targetLicensePlateId;
        Status = PickingContainerStatus.Open;
        Revision = 1;
    }

    public int PickingPlanId { get; private set; }
    public int Sequence { get; private set; }
    public string ContainerKey { get; private set; } = string.Empty;
    public string ExpectedScanCode { get; private set; } = string.Empty;
    public bool TargetScanRequired { get; private set; }
    public int? SalesOrderId { get; private set; }
    public int? ZoneLocationId { get; private set; }
    public int? TargetLicensePlateId { get; private set; }
    public PickingContainerStatus Status { get; private set; }
    public string? ScannedByUserId { get; private set; }
    public DateTime? ScannedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public PickingPlan Plan { get; private set; } = null!;
    public SalesOrder? SalesOrder { get; private set; }
    public Location? ZoneLocation { get; private set; }
    public LicensePlate? TargetLicensePlate { get; private set; }
    public ICollection<PickingPlanLine> Lines { get; private set; } = new List<PickingPlanLine>();
    public ICollection<PickingPlanHandoff> Handoffs { get; private set; } = new List<PickingPlanHandoff>();

    public void Scan(string scanCode, string userId, DateTime scannedAtUtc)
    {
        if (Status is PickingContainerStatus.Cancelled or PickingContainerStatus.Completed)
        {
            throw new InvalidOperationException($"Container '{ContainerKey}' is {Status}.");
        }

        var normalized = RequiredUpper(scanCode, 120, nameof(scanCode));
        if (!string.Equals(normalized, ExpectedScanCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The scanned target container does not match the planned container.");
        }

        Status = PickingContainerStatus.Scanned;
        ScannedByUserId = Required(userId, 450, nameof(userId));
        ScannedAtUtc = DateTime.SpecifyKind(scannedAtUtc, DateTimeKind.Utc);
        Touch();
    }

    public void MarkHandedOff()
    {
        if (Status is not PickingContainerStatus.Scanned and not PickingContainerStatus.HandedOff)
        {
            throw new InvalidOperationException($"Container '{ContainerKey}' must be scanned before handoff.");
        }

        Status = PickingContainerStatus.HandedOff;
        Touch();
    }

    public void Complete()
    {
        if (TargetScanRequired && Status is not (PickingContainerStatus.Scanned or PickingContainerStatus.HandedOff))
        {
            throw new InvalidOperationException($"Container '{ContainerKey}' must be scanned before completion.");
        }

        Status = PickingContainerStatus.Completed;
        Touch();
    }

    public void Cancel()
    {
        if (Status == PickingContainerStatus.Completed)
        {
            return;
        }

        Status = PickingContainerStatus.Cancelled;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
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

    private static string RequiredUpper(string value, int maximumLength, string parameterName) =>
        Required(value, maximumLength, parameterName).ToUpperInvariant();
}
