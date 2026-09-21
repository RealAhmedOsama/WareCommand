namespace Wms.Domain.Enums;

public enum CrossDockMode
{
    Planned = 1,
    Opportunistic = 2
}

public enum CrossDockPlanStatus
{
    Proposed = 1,
    Matched = 2,
    ReservationPending = 3,
    WorkPending = 4,
    PartiallyCompleted = 5,
    Completed = 6,
    Fallback = 7,
    Released = 8,
    Cancelled = 9,
    Exception = 10
}

public enum CrossDockPlanLineStatus
{
    Matched = 1,
    ReservationPending = 2,
    WorkPending = 3,
    Completed = 4,
    ShortPick = 5,
    Released = 6,
    Cancelled = 7,
    Exception = 8
}
