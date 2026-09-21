using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PickingPlanHandoff : Entity
{
    private PickingPlanHandoff()
    {
    }

    public PickingPlanHandoff(
        int planId,
        int sequence,
        int containerId,
        int fromZoneLocationId,
        int? toZoneLocationId,
        string expectedContainerScanCode,
        PickingHandoffStatus status = PickingHandoffStatus.Pending)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(containerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromZoneLocationId);
        ValidateOptionalId(toZoneLocationId, nameof(toZoneLocationId));
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        PickingPlanId = planId;
        Sequence = sequence;
        PickingPlanContainerId = containerId;
        FromZoneLocationId = fromZoneLocationId;
        ToZoneLocationId = toZoneLocationId;
        ExpectedContainerScanCode = RequiredUpper(expectedContainerScanCode, 120, nameof(expectedContainerScanCode));
        Status = status;
        Revision = 1;
    }

    public int PickingPlanId { get; private set; }
    public int Sequence { get; private set; }
    public int PickingPlanContainerId { get; private set; }
    public int FromZoneLocationId { get; private set; }
    public int? ToZoneLocationId { get; private set; }
    public string ExpectedContainerScanCode { get; private set; } = string.Empty;
    public PickingHandoffStatus Status { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public PickingPlan Plan { get; private set; } = null!;
    public PickingPlanContainer Container { get; private set; } = null!;
    public Location FromZoneLocation { get; private set; } = null!;
    public Location? ToZoneLocation { get; private set; }

    public void MakeReady()
    {
        if (Status != PickingHandoffStatus.Pending)
        {
            return;
        }

        Status = PickingHandoffStatus.Ready;
        Touch();
    }

    public void Complete(string scanCode, string userId, DateTime completedAtUtc)
    {
        if (Status == PickingHandoffStatus.Completed)
        {
            return;
        }

        if (Status != PickingHandoffStatus.Ready)
        {
            throw new InvalidOperationException($"Handoff sequence {Sequence} is not ready.");
        }

        var normalized = RequiredUpper(scanCode, 120, nameof(scanCode));
        if (!string.Equals(normalized, ExpectedContainerScanCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The handoff container scan does not match the planned container.");
        }

        Status = PickingHandoffStatus.Completed;
        CompletedByUserId = Required(userId, 450, nameof(userId));
        CompletedAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc);
        Touch();
    }

    public void Block()
    {
        if (Status == PickingHandoffStatus.Completed)
        {
            return;
        }

        Status = PickingHandoffStatus.Blocked;
        Touch();
    }

    public void Cancel()
    {
        if (Status == PickingHandoffStatus.Completed)
        {
            return;
        }

        Status = PickingHandoffStatus.Cancelled;
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
