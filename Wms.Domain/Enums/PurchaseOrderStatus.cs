namespace Wms.Domain.Enums;

public enum PurchaseOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    PartiallyReceived = 3,
    Received = 4,
    Closed = 5,
    Cancelled = 6
}
