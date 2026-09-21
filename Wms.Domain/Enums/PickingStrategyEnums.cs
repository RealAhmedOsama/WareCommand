namespace Wms.Domain.Enums;

public enum PickingStrategyKind
{
    SingleOrder = 1,
    Batch = 2,
    Cluster = 3,
    Zone = 4,
    PickAndPass = 5
}

public enum PickingPlanStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    Exception = 5
}

public enum PickingPlanLineStatus
{
    Planned = 1,
    InProgress = 2,
    Picked = 3,
    ShortPick = 4,
    Cancelled = 5,
    Exception = 6
}

public enum PickingContainerStatus
{
    Open = 1,
    Scanned = 2,
    HandedOff = 3,
    Completed = 4,
    Cancelled = 5
}

public enum PickingHandoffStatus
{
    Pending = 1,
    Ready = 2,
    Completed = 3,
    Blocked = 4,
    Cancelled = 5
}
