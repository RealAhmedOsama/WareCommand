namespace Wms.Domain.Enums;

public enum InventoryDispositionKind
{
    Quarantine = 1,
    Hold = 2,
    Damage = 3,
    Expire = 4,
    Recall = 5,
    Rework = 6,
    ReturnToVendor = 7,
    Donation = 8,
    Alternate = 9,
    Scrap = 10,
    Destruction = 11
}

public enum InventoryDispositionStatus
{
    PendingApproval = 1,
    Approved = 2,
    Executing = 3,
    Completed = 4,
    Rejected = 5,
    Cancelled = 6,
    Exception = 7
}

public enum InventoryRecallStatus
{
    Open = 1,
    Contained = 2,
    Closed = 3,
    Cancelled = 4
}
