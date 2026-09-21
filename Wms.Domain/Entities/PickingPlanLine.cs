using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PickingPlanLine : Entity
{
    private PickingPlanLine()
    {
    }

    public PickingPlanLine(
        int planId,
        int sequence,
        int warehouseWorkId,
        int warehouseWorkLineId,
        int salesOrderId,
        int salesOrderLineId,
        string orderNumberSnapshot,
        int itemId,
        string itemSkuSnapshot,
        decimal plannedQuantity,
        string baseUnitOfMeasure,
        int? sourceLocationId,
        int? zoneLocationId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? sourceLicensePlateId,
        int? inventoryStatusId,
        int? reservationId,
        int? reservationAllocationId,
        decimal? unitWeightKg,
        decimal? unitVolumeCubicMeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseWorkId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseWorkLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedQuantity);
        ValidateOptionalId(sourceLocationId, nameof(sourceLocationId));
        ValidateOptionalId(zoneLocationId, nameof(zoneLocationId));
        ValidateOptionalId(lotId, nameof(lotId));
        ValidateOptionalId(serialNumberId, nameof(serialNumberId));
        ValidateOptionalId(sourceLicensePlateId, nameof(sourceLicensePlateId));
        ValidateOptionalId(inventoryStatusId, nameof(inventoryStatusId));
        ValidateOptionalId(reservationId, nameof(reservationId));
        ValidateOptionalId(reservationAllocationId, nameof(reservationAllocationId));

        PickingPlanId = planId;
        Sequence = sequence;
        WarehouseWorkId = warehouseWorkId;
        WarehouseWorkLineId = warehouseWorkLineId;
        SalesOrderId = salesOrderId;
        SalesOrderLineId = salesOrderLineId;
        OrderNumberSnapshot = Required(orderNumberSnapshot, 80, nameof(orderNumberSnapshot));
        ItemId = itemId;
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        PlannedQuantity = plannedQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        ZoneLocationId = zoneLocationId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        SourceLicensePlateId = sourceLicensePlateId;
        InventoryStatusId = inventoryStatusId;
        ReservationId = reservationId;
        ReservationAllocationId = reservationAllocationId;
        UnitWeightKg = ValidateNonNegative(unitWeightKg, nameof(unitWeightKg));
        UnitVolumeCubicMeters = ValidateNonNegative(unitVolumeCubicMeters, nameof(unitVolumeCubicMeters));
        Status = PickingPlanLineStatus.Planned;
        Revision = 1;
    }

    public int PickingPlanId { get; private set; }
    public int Sequence { get; private set; }
    public int WarehouseWorkId { get; private set; }
    public int WarehouseWorkLineId { get; private set; }
    public int SalesOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public string OrderNumberSnapshot { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public decimal PlannedQuantity { get; private set; }
    public decimal PickedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int? SourceLocationId { get; private set; }
    public int? ZoneLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? SourceLicensePlateId { get; private set; }
    public int? InventoryStatusId { get; private set; }
    public int? ReservationId { get; private set; }
    public int? ReservationAllocationId { get; private set; }
    public decimal? UnitWeightKg { get; private set; }
    public decimal? UnitVolumeCubicMeters { get; private set; }
    public string? BatchKey { get; private set; }
    public int? PickingPlanContainerId { get; private set; }
    public int? PickingPlanHandoffId { get; private set; }
    public int ZoneSequence { get; private set; }
    public int ContainerSequence { get; private set; }
    public PickingPlanLineStatus Status { get; private set; }
    public string? LastError { get; private set; }
    public long Revision { get; private set; }

    public PickingPlan Plan { get; private set; } = null!;
    public WarehouseWork Work { get; private set; } = null!;
    public WarehouseWorkLine WorkLine { get; private set; } = null!;
    public SalesOrder SalesOrder { get; private set; } = null!;
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location? SourceLocation { get; private set; }
    public Location? ZoneLocation { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? SourceLicensePlate { get; private set; }
    public InventoryStatus? InventoryStatus { get; private set; }
    public InventoryReservation? Reservation { get; private set; }
    public InventoryReservationAllocation? ReservationAllocation { get; private set; }
    public PickingPlanContainer? Container { get; private set; }
    public PickingPlanHandoff? Handoff { get; private set; }

    public void AssignRouting(
        string batchKey,
        int containerId,
        int containerSequence,
        int zoneSequence,
        int? handoffId = null)
    {
        BatchKey = Required(batchKey, 160, nameof(batchKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(containerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(containerSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoneSequence);
        ValidateOptionalId(handoffId, nameof(handoffId));
        PickingPlanContainerId = containerId;
        ContainerSequence = containerSequence;
        ZoneSequence = zoneSequence;
        PickingPlanHandoffId = handoffId;
        Touch();
    }

    public void SyncFromWork(WarehouseWorkStatus workStatus, decimal actualQuantity)
    {
        if (actualQuantity < 0m || actualQuantity > PlannedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(actualQuantity));
        }

        PickedQuantity = actualQuantity;
        Status = workStatus switch
        {
            WarehouseWorkStatus.Completed when actualQuantity >= PlannedQuantity => PickingPlanLineStatus.Picked,
            WarehouseWorkStatus.Completed => PickingPlanLineStatus.ShortPick,
            WarehouseWorkStatus.Cancelled => PickingPlanLineStatus.Cancelled,
            WarehouseWorkStatus.Exception => PickingPlanLineStatus.Exception,
            WarehouseWorkStatus.InProgress or WarehouseWorkStatus.Assigned or WarehouseWorkStatus.Paused =>
                PickingPlanLineStatus.InProgress,
            _ => PickingPlanLineStatus.Planned
        };
        LastError = Status switch
        {
            PickingPlanLineStatus.ShortPick => "The underlying warehouse work completed short.",
            PickingPlanLineStatus.Exception => "The underlying warehouse work is waiting on an exception.",
            _ => null
        };
        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status == PickingPlanLineStatus.Picked)
        {
            return;
        }

        Status = PickingPlanLineStatus.Cancelled;
        LastError = Required(reason, 2_000, nameof(reason));
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));
}
