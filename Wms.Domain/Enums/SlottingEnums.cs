namespace Wms.Domain.Enums;

public enum SlottingRecommendationKind
{
    PickFaceAssignment = 1,
    BulkLocation = 2,
    Consolidation = 3,
    Relocation = 4
}

public enum SlottingRecommendationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Expired = 4,
    WorkCreated = 5,
    Superseded = 6
}
