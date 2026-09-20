namespace Wms.Domain.Enums;

public enum ReceiptDiscrepancyKind
{
    Over = 1,
    Shortfall = 2,
    Damaged = 3,
    Rejected = 4,
    Quarantined = 5,
    UnexpectedItem = 6,
    IdentityMismatch = 7,
    Other = 8
}
