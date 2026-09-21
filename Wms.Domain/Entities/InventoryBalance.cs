using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

/// <summary>
/// Materialized current inventory for one complete inventory dimension.
/// Only the inventory ledger service may apply deltas to this entity.
/// </summary>
public sealed class InventoryBalance : Entity
{
    private InventoryBalance()
    {
    }

    public InventoryBalance(InventoryBalanceKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        WarehouseId = key.WarehouseId;
        LocationId = key.LocationId;
        ItemId = key.ItemId;
        LotId = key.LotId;
        SerialNumberId = key.SerialNumberId;
        SerialNumber = key.SerialNumber;
        LicensePlateId = key.LicensePlateId;
        InventoryStatusId = key.InventoryStatusId;
        BaseUnitOfMeasure = key.BaseUnitOfMeasure;
        OwnerKind = key.OwnerKind;
        InventoryOwnerId = key.InventoryOwnerId;
        OwnerCodeSnapshot = key.OwnerCodeSnapshot;
    }

    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public int ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public decimal OnHandQuantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public long Revision { get; private set; }
    public decimal AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? InventoryOwner { get; private set; }

    public InventoryBalanceKey GetKey() => new(
        WarehouseId,
        LocationId,
        ItemId,
        LotId,
        SerialNumberId,
        SerialNumber,
        LicensePlateId,
        InventoryStatusId,
        BaseUnitOfMeasure,
        OwnerKind,
        InventoryOwnerId,
        OwnerCodeSnapshot);

    public void Apply(
        decimal quantityDelta,
        decimal reservedQuantityDelta,
        bool allowNegativeStock)
    {
        var nextOnHand = OnHandQuantity + quantityDelta;
        var nextReserved = ReservedQuantity + reservedQuantityDelta;

        if (!allowNegativeStock && nextOnHand < 0m)
        {
            throw new InvalidOperationException(
                "Inventory balance cannot become negative unless the warehouse explicitly permits negative stock.");
        }

        if (nextReserved < 0m)
        {
            throw new InvalidOperationException("Inventory reservations cannot become negative.");
        }

        if (nextOnHand >= 0m && nextReserved > nextOnHand)
        {
            throw new InvalidOperationException("Inventory reservations cannot exceed physical on-hand quantity.");
        }

        OnHandQuantity = nextOnHand;
        ReservedQuantity = nextReserved;
        Revision++;
        SetUpdatedAt();
    }
}
