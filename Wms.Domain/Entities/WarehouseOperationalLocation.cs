using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WarehouseOperationalLocation : Entity
{
    private WarehouseOperationalLocation()
    {
    }

    public WarehouseOperationalLocation(
        int warehouseId,
        int locationId,
        WarehouseOperationalLocationRole role)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId, nameof(warehouseId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId, nameof(locationId));

        WarehouseId = warehouseId;
        LocationId = locationId;
        Role = role;
    }

    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public WarehouseOperationalLocationRole Role { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;

    public void SetLocation(int locationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId, nameof(locationId));

        LocationId = locationId;
        SetUpdatedAt();
    }
}
