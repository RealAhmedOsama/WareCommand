namespace Wms.Domain.Enums;

public enum InventoryReservationEventType
{
    Created = 1,
    AllocationAdded = 2,
    Released = 3,
    Consumed = 4,
    Reallocated = 5,
    Expired = 6,
    Cancelled = 7
}
