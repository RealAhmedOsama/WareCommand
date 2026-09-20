namespace Wms.Domain.Enums;

public enum InventoryPolicyQuantityBasis
{
    OnHand = 1,
    PhysicalAvailable = 2,
    AvailableToPromise = 3
}

public enum InventoryReplenishmentSignalKind
{
    OutOfStock = 1,
    LowStock = 2,
    Overstock = 3
}
