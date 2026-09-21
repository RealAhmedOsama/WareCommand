namespace Wms.Domain.Enums;

public enum OutboundExceptionCode
{
    AllocationShortage = 1,
    StockNotFound = 2,
    DamagedStock = 3,
    WrongLot = 4,
    WrongSerial = 5,
    WrongLicensePlate = 6,
    LocationBlocked = 7,
    OrderHold = 8,
    AddressCarrierIssue = 9,
    PackVariance = 10,
    LoadMismatch = 11,
    CancelledDemand = 12,
    MissedCutoff = 13
}

public enum OutboundExceptionSeverity
{
    Info = 1,
    Warning = 2,
    Major = 3,
    Critical = 4
}

public enum OutboundExceptionStatus
{
    Open = 1,
    UnderReview = 2,
    Held = 3,
    Resolved = 4,
    Cancelled = 5
}

public enum OutboundExceptionResolution
{
    Reallocate = 1,
    Substitute = 2,
    PartialBackorder = 3,
    ReleaseReservation = 4,
    CancelLine = 5,
    Repick = 6,
    Repack = 7,
    HoldOrder = 8,
    ChangeCarrier = 9,
    Unload = 10,
    SupervisorOverride = 11,
    CancelOrder = 12
}
