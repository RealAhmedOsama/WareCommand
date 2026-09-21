namespace Wms.Domain.Enums;

public enum ReasonCodeCategory
{
    Adjustment = 1,
    Override = 2,
    Cancellation = 3,
    Hold = 4,
    StatusChange = 5,
    Discrepancy = 6,
    ShortPick = 7,
    Damage = 8,
    Reprint = 9,
    Return = 10,
    Scrap = 11,
    Recount = 12,
    Other = 99
}
