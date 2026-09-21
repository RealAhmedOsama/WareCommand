namespace Wms.Application.Inventory;

/// <summary>
/// Server-computed inventory facts used by the operational dashboard.
/// Quantities are canonical base-unit quantities; no currency value is
/// implied when an item cost is not available.
/// </summary>
public sealed record InventoryDashboardMetricsDto(
    int StockKeepingUnits,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal HeldQuantity,
    decimal DamagedQuantity,
    decimal ExpiredQuantity,
    decimal ExpiringQuantity,
    int StockLocations);
