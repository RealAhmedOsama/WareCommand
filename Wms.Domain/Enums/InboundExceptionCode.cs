namespace Wms.Domain.Enums;

public enum InboundExceptionCode
{
    UnknownItem = 1,
    MissingPurchaseOrder = 2,
    MissingAdvanceShippingNotice = 3,
    OverReceipt = 4,
    ShortReceipt = 5,
    DamagedPackage = 6,
    InvalidLot = 7,
    InvalidExpiry = 8,
    InvalidSerial = 9,
    DuplicateLicensePlate = 10,
    CapacityNoLocation = 11,
    RejectedQuality = 12,
    DocumentMismatch = 13
}
