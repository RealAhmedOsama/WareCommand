namespace Wms.Domain.Enums;

public enum WarehouseWorkAssignmentStrategy
{
    Manual = 1,
    SelfClaim = 2,
    TeamQueue = 3,
    AutomaticSuggestion = 4
}

public enum WarehouseWorkActivityCategory
{
    Travel = 1,
    Exception = 2,
    Wait = 3,
    Other = 4
}
