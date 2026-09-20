namespace Wms.Domain.Enums;

public enum InventoryReservationAllocationStatus
{
    Active = 1,
    PartiallyConsumed = 2,
    Consumed = 3,
    Released = 4,
    Expired = 5,
    Cancelled = 6
}
