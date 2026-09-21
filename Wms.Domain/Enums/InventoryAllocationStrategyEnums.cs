namespace Wms.Domain.Enums;

public enum InventoryAllocationStrategyKind
{
    Fifo = 1,
    Fefo = 2,
    Lifo = 3,
    FixedLocation = 4,
    LocationPriority = 5,
    Nearest = 6,
    RoutePriority = 7
}

public enum InventoryAllocationMissingExpiryFallback
{
    Last = 1,
    ReceiptDate = 2
}
