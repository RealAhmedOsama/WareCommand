namespace Wms.Domain.Enums;

public enum ReceivingSessionStatus
{
    Open = 1,
    Paused = 2,
    PartiallyCompleted = 3,
    Completed = 4,
    Cancelled = 5
}

public enum ReceivingSessionSourceType
{
    PurchaseOrder = 1,
    AdvanceShippingNotice = 2,
    DockArrival = 3,
    ExternalReference = 4,
    BlindReceipt = 5
}

public enum ReceivingScanStatus
{
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Corrected = 4
}
