namespace Wms.Domain.Enums;

public enum SupplierReturnStatus
{
    Draft = 1,
    Approved = 2,
    Picking = 3,
    Packed = 4,
    Shipped = 5,
    Acknowledged = 6,
    Closed = 7,
    Cancelled = 8,
    Exception = 9
}
