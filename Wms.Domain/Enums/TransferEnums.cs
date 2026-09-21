namespace Wms.Domain.Enums;

public enum TransferOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Allocated = 3,
    Released = 4,
    Picking = 5,
    Shipped = 6,
    InTransit = 7,
    PartiallyReceived = 8,
    Received = 9,
    Closed = 10,
    Cancelled = 11,
    Exception = 12
}

public enum TransferLineStatus
{
    Open = 1,
    PartiallyShipped = 2,
    Shipped = 3,
    PartiallyReceived = 4,
    Received = 5,
    Cancelled = 6,
    Exception = 7
}

public enum InternalMovementStatus
{
    Requested = 1,
    Completed = 2,
    Cancelled = 3,
    Exception = 4
}
