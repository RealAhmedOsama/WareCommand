namespace Wms.Domain.Enums;

public enum ShipmentStatus
{
    Planned = 1,
    Released = 2,
    Packing = 3,
    Ready = 4,
    Loading = 5,
    Loaded = 6,
    Shipped = 7,
    Delivered = 8,
    Closed = 9,
    Cancelled = 10,
    Exception = 11
}

public enum ShipmentPackageLinkStatus
{
    Eligible = 1,
    Loaded = 2,
    Unloaded = 3,
    Shipped = 4,
    Cancelled = 5
}

public enum ShipmentLoadStatus
{
    Open = 1,
    Closed = 2,
    Cancelled = 3
}

public enum CarrierStatus
{
    Active = 1,
    Inactive = 2
}
