namespace Wms.Domain.Enums;

public enum InventoryReservationStatus
{
    Pending = 1,
    PartiallyReserved = 2,
    Reserved = 3,
    PartiallyConsumed = 4,
    Consumed = 5,
    Released = 6,
    Expired = 7,
    Cancelled = 8
}
