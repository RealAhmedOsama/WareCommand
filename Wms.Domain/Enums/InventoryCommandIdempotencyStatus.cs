namespace Wms.Domain.Enums;

public enum InventoryCommandIdempotencyStatus
{
    InProgress = 1,
    Succeeded = 2,
    Failed = 3,
    Expired = 4
}
