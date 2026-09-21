namespace Wms.Domain.Enums;

/// <summary>
/// Deterministic inputs supported by the warehouse ABC classification engine.
/// </summary>
public enum InventoryClassificationMethod
{
    ShippedQuantity = 1,
    ShippedLines = 2,
    MovementVelocity = 3,
    InventoryValue = 4,
    Criticality = 5
}

public enum InventoryClassificationClass
{
    Unclassified = 0,
    A = 1,
    B = 2,
    C = 3
}

public enum InventoryClassificationSource
{
    Automatic = 1,
    ManualOverride = 2
}
