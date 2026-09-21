using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class CycleCountLine : Entity
{
    private CycleCountLine()
    {
    }

    public CycleCountLine(
        int taskId,
        int sequence,
        int warehouseId,
        int locationId,
        int itemId,
        decimal expectedQuantity,
        string baseUnitOfMeasure,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        int inventoryStatusId,
        bool emptyLocationCandidate,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(taskId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedQuantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        if (string.IsNullOrWhiteSpace(baseUnitOfMeasure))
        {
            throw new ArgumentException("A base unit of measure is required.", nameof(baseUnitOfMeasure));
        }

        TaskId = taskId;
        Sequence = sequence;
        WarehouseId = warehouseId;
        LocationId = locationId;
        ItemId = itemId;
        ExpectedQuantity = expectedQuantity;
        BaseUnitOfMeasure = baseUnitOfMeasure.Trim().ToUpperInvariant();
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = licensePlateId;
        InventoryStatusId = inventoryStatusId;
        EmptyLocationCandidate = emptyLocationCandidate;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        Status = CycleCountLineStatus.Pending;
        Revision = 1;
    }

    public int TaskId { get; private set; }
    public int Sequence { get; private set; }
    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public int ItemId { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal? VarianceQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public bool EmptyLocationCandidate { get; private set; }
    public bool EmptyLocationConfirmed { get; private set; }
    public CycleCountLineStatus Status { get; private set; }
    public long Revision { get; private set; }

    public CycleCountTask Task { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? InventoryOwner { get; private set; }

    public void RecordCount(decimal countedQuantity, bool emptyLocationConfirmed = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countedQuantity);
        CountedQuantity = countedQuantity;
        VarianceQuantity = countedQuantity - ExpectedQuantity;
        EmptyLocationConfirmed = emptyLocationConfirmed;
        Status = VarianceQuantity == 0m
            ? CycleCountLineStatus.Counted
            : CycleCountLineStatus.Variance;
        Revision++;
        SetUpdatedAt();
    }

    public void Approve()
    {
        if (Status != CycleCountLineStatus.Variance)
        {
            throw new InvalidOperationException("Only a variance line requires approval.");
        }

        Status = CycleCountLineStatus.Approved;
        Revision++;
        SetUpdatedAt();
    }

    public void MarkCompleted()
    {
        if (Status is not (CycleCountLineStatus.Counted or CycleCountLineStatus.Approved))
        {
            throw new InvalidOperationException("The count line is not ready to complete.");
        }

        Status = CycleCountLineStatus.Completed;
        Revision++;
        SetUpdatedAt();
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
    }
}
