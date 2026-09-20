namespace Wms.Domain.Enums;

public enum ReceiptStatus
{
    Draft = 1,
    Open = 2,
    Receiving = 3,
    PartiallyCompleted = 4,
    Completed = 5,
    Reversed = 6,
    Corrected = 7,
    Cancelled = 8,
    Exception = 9
}
