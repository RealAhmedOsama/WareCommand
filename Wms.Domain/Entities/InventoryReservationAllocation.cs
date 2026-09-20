using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// One explicit inventory dimension allocated to a reservation. Quantity
/// changes are retained as consumed/released amounts instead of overwritten.
/// </summary>
public sealed class InventoryReservationAllocation : Entity
{
    private InventoryReservationAllocation()
    {
    }

    public InventoryReservationAllocation(
        int reservationId,
        int warehouseId,
        int locationId,
        int itemId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        int inventoryStatusId,
        string baseUnitOfMeasure,
        decimal allocatedQuantity,
        string? reason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(reservationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(allocatedQuantity);
        if (string.IsNullOrWhiteSpace(baseUnitOfMeasure))
        {
            throw new ArgumentException("Base unit of measure is required.", nameof(baseUnitOfMeasure));
        }

        ReservationId = reservationId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        ItemId = itemId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = NormalizeOptional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        InventoryStatusId = inventoryStatusId;
        BaseUnitOfMeasure = baseUnitOfMeasure.Trim().ToUpperInvariant();
        AllocatedQuantity = allocatedQuantity;
        Status = InventoryReservationAllocationStatus.Active;
        Reason = NormalizeOptional(reason, 1_000);
    }

    public int ReservationId { get; private set; }
    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public int ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal AllocatedQuantity { get; private set; }
    public decimal ConsumedQuantity { get; private set; }
    public decimal ReleasedQuantity { get; private set; }
    public InventoryReservationAllocationStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public long Revision { get; private set; }

    public InventoryReservation Reservation { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? SerialNumberEntity { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;

    public decimal RemainingQuantity =>
        AllocatedQuantity - ConsumedQuantity - ReleasedQuantity;

    public bool IsOpen => Status is
        InventoryReservationAllocationStatus.Active or
        InventoryReservationAllocationStatus.PartiallyConsumed;

    public void Consume(decimal quantity, string? reason = null)
    {
        EnsureOpen(quantity);
        ConsumedQuantity += quantity;
        Reason = NormalizeOptional(reason, 1_000) ?? Reason;
        Status = RemainingQuantity == 0m
            ? InventoryReservationAllocationStatus.Consumed
            : InventoryReservationAllocationStatus.PartiallyConsumed;
        Touch();
    }

    public void Release(decimal quantity, string? reason = null)
    {
        EnsureOpen(quantity);
        ReleasedQuantity += quantity;
        Reason = NormalizeOptional(reason, 1_000) ?? Reason;
        if (RemainingQuantity == 0m)
        {
            Status = InventoryReservationAllocationStatus.Released;
        }

        Touch();
    }

    public void Expire(string? reason = null)
    {
        if (!IsOpen)
        {
            return;
        }

        ReleasedQuantity += RemainingQuantity;
        Status = InventoryReservationAllocationStatus.Expired;
        Reason = NormalizeOptional(reason, 1_000) ?? Reason;
        Touch();
    }

    public void Cancel(string? reason = null)
    {
        if (!IsOpen)
        {
            return;
        }

        ReleasedQuantity += RemainingQuantity;
        Status = InventoryReservationAllocationStatus.Cancelled;
        Reason = NormalizeOptional(reason, 1_000) ?? Reason;
        Touch();
    }

    private void EnsureOpen(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (!IsOpen)
        {
            throw new InvalidOperationException(
                $"Allocation '{Id}' is not open for quantity consumption or release.");
        }

        if (quantity > RemainingQuantity)
        {
            throw new InvalidOperationException(
                "The requested quantity exceeds the allocation remainder.");
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
