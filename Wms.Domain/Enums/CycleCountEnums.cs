namespace Wms.Domain.Enums;

public enum CycleCountTaskStatus
{
    Planned = 1,
    InProgress = 2,
    AwaitingApproval = 3,
    RecountRequired = 4,
    Approved = 5,
    Completed = 6,
    Cancelled = 7,
    Exception = 8
}

public enum CycleCountLineStatus
{
    Pending = 1,
    Counted = 2,
    Variance = 3,
    Approved = 4,
    RecountRequired = 5,
    Completed = 6,
    Cancelled = 7
}

public enum CycleCountFreezePolicy
{
    SnapshotAndReconcile = 1,
    BlockSelectedDimensions = 2
}
