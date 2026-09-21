namespace Wms.Domain.Enums;

public enum ReturnStatus
{
    Requested = 1,
    Authorized = 2,
    InTransit = 3,
    Received = 4,
    Inspecting = 5,
    Disposed = 6,
    Closed = 7,
    Rejected = 8,
    Cancelled = 9
}

public enum ReturnDispositionKind
{
    RestockAvailable = 1,
    RestockDamaged = 2,
    Repair = 3,
    Scrap = 4,
    ReturnToVendor = 5,
    RejectToCustomer = 6
}

public enum ReturnReceiptStatus
{
    PendingDisposition = 1,
    PartiallyDisposed = 2,
    Disposed = 3
}
