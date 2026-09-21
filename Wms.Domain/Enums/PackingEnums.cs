namespace Wms.Domain.Enums;

public enum PackingStationStatus
{
    Active = 1,
    Maintenance = 2,
    Disabled = 3
}

public enum PackingSessionStatus
{
    Open = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    Exception = 5
}

public enum PackingSourceType
{
    SalesOrder = 1,
    Shipment = 2,
    Wave = 3,
    StagingLicensePlate = 4
}

public enum ShipmentPackageStatus
{
    Open = 1,
    Closed = 2,
    Voided = 3,
    Shipped = 4
}

public enum PackingPackageType
{
    Carton = 1,
    Tote = 2,
    Pallet = 3,
    Bag = 4,
    Custom = 99
}
