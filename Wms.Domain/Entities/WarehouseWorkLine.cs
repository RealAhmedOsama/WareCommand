using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class WarehouseWorkLine : Entity
{
    private WarehouseWorkLine()
    {
    }

    public WarehouseWorkLine(
        int sequence,
        int warehouseId,
        int itemId,
        decimal plannedQuantity,
        string baseUnitOfMeasure,
        int? sourceLocationId = null,
        int? destinationLocationId = null,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        int? inventoryStatusId = null,
        string? sourceReference = null,
        string? dimensionsSnapshot = null,
        int? reservationId = null,
        int? reservationAllocationId = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(plannedQuantity);
        if (sourceLocationId is <= 0 || destinationLocationId is <= 0 || lotId is <= 0 || serialNumberId is <= 0 || licensePlateId is <= 0 || inventoryStatusId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLocationId));
        }

        if (reservationId is <= 0 || reservationAllocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservationId));
        }

        Sequence = sequence;
        WarehouseId = warehouseId;
        ItemId = itemId;
        PlannedQuantity = plannedQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        InventoryStatusId = inventoryStatusId;
        SourceReference = Optional(sourceReference, 200);
        DimensionsSnapshot = Optional(dimensionsSnapshot, 2_000);
        ReservationId = reservationId;
        ReservationAllocationId = reservationAllocationId;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        Revision = 1;
    }

    public int WarehouseWorkId { get; private set; }
    public int Sequence { get; private set; }
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public decimal PlannedQuantity { get; private set; }
    public decimal ActualQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int? SourceLocationId { get; private set; }
    public int? DestinationLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int? InventoryStatusId { get; private set; }
    public string? SourceReference { get; private set; }
    public string? DimensionsSnapshot { get; private set; }
    public int? ReservationId { get; private set; }
    public int? ReservationAllocationId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public long Revision { get; private set; }

    public WarehouseWork Work { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location? SourceLocation { get; private set; }
    public Location? DestinationLocation { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus? InventoryStatus { get; private set; }
    public InventoryOwner? InventoryOwner { get; private set; }

    public void RecordActualQuantity(decimal actualQuantity, bool allowCountVariance = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actualQuantity);
        if (!allowCountVariance && actualQuantity > PlannedQuantity)
        {
            throw new InvalidOperationException("Actual work quantity cannot exceed planned quantity.");
        }

        ActualQuantity = actualQuantity;
        Revision++;
        SetUpdatedAt();
    }

    public void AddActualQuantity(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        RecordActualQuantity(ActualQuantity + quantity);
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
