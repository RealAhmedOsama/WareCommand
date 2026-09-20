// Wms.Domain/Enums/MovementType.cs

namespace Wms.Domain.Enums;

public enum MovementType
{
    Receipt = 1,
    Putaway = 2,
    Pick = 3,
    Ship = 4,
    Adjustment = 5,
    CycleCount = 6,
    Transfer = 7,
    StatusChange = 8
}
