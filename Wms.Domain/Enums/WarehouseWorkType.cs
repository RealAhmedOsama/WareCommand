namespace Wms.Domain.Enums;

public enum WarehouseWorkType
{
    Putaway = 1,
    Pick = 2,
    Replenishment = 3,
    Move = 4,
    Count = 5,
    Pack = 6,
    Load = 7,
    Return = 8,
    Other = 9,
    ValueAddedService = 10
}

public enum WarehouseWorkStatus
{
    Open = 1,
    Available = 2,
    Assigned = 3,
    InProgress = 4,
    Paused = 5,
    Completed = 6,
    Cancelled = 7,
    Exception = 8
}

public enum WarehouseWorkExceptionType
{
    Shortage = 1,
    Blocked = 2,
    Damaged = 3,
    NotFound = 4,
    Other = 5
}

public enum WarehouseWorkCommandStatus
{
    Succeeded = 1,
    Rejected = 2
}
