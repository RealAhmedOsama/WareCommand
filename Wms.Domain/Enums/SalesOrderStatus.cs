namespace Wms.Domain.Enums;

public enum SalesOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Allocating = 3,
    PartiallyAllocated = 4,
    Released = 5,
    Picking = 6,
    Packing = 7,
    PartiallyShipped = 8,
    Shipped = 9,
    Closed = 10,
    Cancelled = 11,
    Exception = 12,
    Held = 13
}
