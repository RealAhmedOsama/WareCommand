using Wms.Application.DTOs;
using Wms.Application.UseCases.Reports;

namespace Wms.ASP.Models;

public class DashboardViewModel
{
    public int TotalItems { get; set; }
    public int ActiveItems { get; set; }
    public int TotalSKUs { get; set; }
    public decimal TotalStockValue { get; set; }
    public int StockLocations { get; set; }
    public List<MovementReportDto> RecentMovements { get; set; } = new();
    public List<StockDto> LowStockItems { get; set; } = new();
    public DateTime LastRefresh { get; set; }
}

public class InventoryViewModel
{
    public List<StockDto> StockItems { get; set; } = new();
    public List<StockSummaryDto> StockSummary { get; set; } = new();
    public string? SearchTerm { get; set; }
    public bool ShowSummary { get; set; }
}

public class ItemManagementViewModel
{
    public List<ItemDto> Items { get; set; } = new();
    public string? SearchTerm { get; set; }
}

public class LocationManagementViewModel
{
    public List<LocationDto> Locations { get; set; } = new();
    public string? SearchTerm { get; set; }
}

public class StockAdjustmentViewModel
{
    public string ItemSku { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public decimal CurrentQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ReceivingViewModel
{
    public string ItemSku { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public class PickingViewModel
{
    public string ItemSku { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Notes { get; set; }
}

public class PutawayViewModel
{
    public string ItemSku { get; set; } = string.Empty;
    public string FromLocationCode { get; set; } = string.Empty;
    public string ToLocationCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Notes { get; set; }
}

public class CreateItemViewModel
{
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UnitOfMeasure { get; set; } = "EA";
    public bool RequiresLot { get; set; }
    public bool RequiresSerial { get; set; }
    public string? Barcode { get; set; }
}

public class EditItemViewModel
{
    public int Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UnitOfMeasure { get; set; } = "EA";
    public bool RequiresLot { get; set; }
    public bool RequiresSerial { get; set; }
    public string? Barcode { get; set; }
}

public class CreateLocationViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int WarehouseId { get; set; } = 1;
    public bool IsPickable { get; set; } = true;
    public bool IsReceivable { get; set; } = true;
    public int Capacity { get; set; }
}
