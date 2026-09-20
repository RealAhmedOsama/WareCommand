using Wms.Domain.Entities;

namespace Wms.Infrastructure.Identity;

public sealed class WmsUserWarehouseAssignment
{
    public string UserId { get; set; } = string.Empty;

    public int WarehouseId { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UnixEpoch;

    public WmsUser User { get; set; } = null!;

    public Warehouse Warehouse { get; set; } = null!;
}
