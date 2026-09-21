namespace Wms.Domain.Enums;

public enum PutawayRuleStrategy
{
    FixedLocation = 1,
    SameItemLot = 2,
    EmptyLocation = 3,
    PickFaceFirst = 4,
    BulkStorage = 5,
    NearestSequence = 6,
    CapacityAware = 7
}
