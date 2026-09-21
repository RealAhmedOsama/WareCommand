namespace Wms.Domain.Enums;

public enum WaveStatus
{
    Draft = 1,
    Planned = 2,
    Processing = 3,
    PartiallyProcessed = 4,
    Released = 5,
    Completed = 6,
    Cancelled = 7,
    Failed = 8
}

public enum WaveTriggerType
{
    Manual = 1,
    Scheduled = 2,
    RuleBased = 3
}

public enum WaveLineStatus
{
    Selected = 1,
    Processing = 2,
    Allocated = 3,
    Released = 4,
    Shortage = 5,
    Failed = 6,
    Removed = 7,
    Cancelled = 8
}

public enum WaveStepType
{
    SelectDemand = 1,
    Allocate = 2,
    Replenishment = 3,
    CreateWork = 4,
    Validate = 5,
    Release = 6,
    Complete = 7,
    Cancel = 8
}

public enum WaveStepStatus
{
    Started = 1,
    Succeeded = 2,
    PartiallySucceeded = 3,
    Failed = 4,
    Skipped = 5
}
