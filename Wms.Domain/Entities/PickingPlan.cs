using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PickingPlan : Entity
{
    private readonly List<PickingPlanLine> _lines = [];
    private readonly List<PickingPlanContainer> _containers = [];
    private readonly List<PickingPlanHandoff> _handoffs = [];

    private PickingPlan()
    {
    }

    public PickingPlan(
        int warehouseId,
        string planNumber,
        string creationKey,
        PickingStrategyKind strategy,
        int? waveId,
        int? policyId,
        int maxOrders,
        int maxContainers,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        string createdByUserId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        WarehouseId = warehouseId;
        PlanNumber = Required(planNumber, 80, nameof(planNumber));
        CreationKey = Required(creationKey, 250, nameof(creationKey));
        Strategy = strategy;
        WaveId = ValidateOptionalId(waveId, nameof(waveId));
        PolicyId = ValidateOptionalId(policyId, nameof(policyId));
        MaxOrders = ValidatePositive(maxOrders, nameof(maxOrders));
        MaxContainers = ValidatePositive(maxContainers, nameof(maxContainers));
        MaxWeightKg = ValidateNonNegative(maxWeightKg, nameof(maxWeightKg));
        MaxVolumeCubicMeters = ValidateNonNegative(maxVolumeCubicMeters, nameof(maxVolumeCubicMeters));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Status = PickingPlanStatus.Planned;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string PlanNumber { get; private set; } = string.Empty;
    public string CreationKey { get; private set; } = string.Empty;
    public PickingStrategyKind Strategy { get; private set; }
    public int? WaveId { get; private set; }
    public int? PolicyId { get; private set; }
    public int MaxOrders { get; private set; }
    public int MaxContainers { get; private set; }
    public decimal? MaxWeightKg { get; private set; }
    public decimal? MaxVolumeCubicMeters { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public PickingPlanStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Wave? Wave { get; private set; }
    public PickingStrategyPolicy? Policy { get; private set; }
    public IReadOnlyList<PickingPlanLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<PickingPlanContainer> Containers => _containers.AsReadOnly();
    public IReadOnlyList<PickingPlanHandoff> Handoffs => _handoffs.AsReadOnly();
    public bool IsTerminal => Status is PickingPlanStatus.Completed or PickingPlanStatus.Cancelled;

    public void AddLine(PickingPlanLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureNotTerminal();
        if (_lines.Any(existing => existing.WarehouseWorkLineId == line.WarehouseWorkLineId))
        {
            throw new InvalidOperationException("A warehouse-work line cannot be added to a picking plan twice.");
        }

        _lines.Add(line);
        Touch();
    }

    public void AddContainer(PickingPlanContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        EnsureNotTerminal();
        if (_containers.Any(existing =>
                string.Equals(existing.ContainerKey, container.ContainerKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A picking plan container key must be unique.");
        }

        _containers.Add(container);
        Touch();
    }

    public void AddHandoff(PickingPlanHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        EnsureNotTerminal();
        if (_handoffs.Any(existing => existing.Sequence == handoff.Sequence))
        {
            throw new InvalidOperationException("Picking handoff sequence values must be unique.");
        }

        _handoffs.Add(handoff);
        Touch();
    }

    public void SetStatus(PickingPlanStatus status, string? error = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (IsTerminal && status != Status)
        {
            throw new InvalidOperationException($"Picking plan in {Status} cannot transition.");
        }

        Status = status;
        LastError = string.IsNullOrWhiteSpace(error) ? null : Required(error, 2_000, nameof(error));
        Touch();
    }

    public void Cancel(string reason)
    {
        EnsureNotTerminal();
        Status = PickingPlanStatus.Cancelled;
        LastError = Required(reason, 2_000, nameof(reason));
        foreach (var line in _lines.Where(line => line.Status is not PickingPlanLineStatus.Picked))
        {
            line.Cancel(reason);
        }

        foreach (var container in _containers.Where(container => container.Status is not PickingContainerStatus.Completed))
        {
            container.Cancel();
        }

        foreach (var handoff in _handoffs.Where(handoff => handoff.Status != PickingHandoffStatus.Completed))
        {
            handoff.Cancel();
        }

        Touch();
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"Picking plan in {Status} cannot be changed.");
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static int? ValidateOptionalId(int? value, string parameterName) =>
        value is <= 0
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private static int ValidatePositive(int value, string parameterName) =>
        value is < 1 or > 100_000
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private static decimal? ValidateNonNegative(decimal? value, string parameterName) =>
        value is < 0m
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

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
