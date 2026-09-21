namespace Wms.Domain.Enums;

public enum QualityInspectionStatus
{
    Open = 1,
    InProgress = 2,
    Passed = 3,
    PartiallyPassed = 4,
    Failed = 5,
    Retest = 6,
    Held = 7,
    Closed = 8,
    Cancelled = 9
}

public enum QualitySamplingMethod
{
    FixedQuantity = 1,
    Percentage = 2,
    EveryNthLicensePlate = 3,
    FullInspection = 4
}

public enum QualityRiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum QualityMeasurementType
{
    Numeric = 1,
    Text = 2,
    Boolean = 3,
    Choice = 4
}

public enum QualityDispositionType
{
    Pass = 1,
    PartialPass = 2,
    Fail = 3,
    Retest = 4,
    Hold = 5,
    ReturnToVendor = 6,
    Rework = 7,
    Damage = 8,
    Scrap = 9
}
