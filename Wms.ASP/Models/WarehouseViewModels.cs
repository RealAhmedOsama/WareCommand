using System.ComponentModel.DataAnnotations;
using Wms.Application.DTOs;
using Wms.Application.Identity;
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
    public decimal LowStockThreshold { get; set; }
    public int LowStockAlertLimit { get; set; }
    public int RecentMovementPeriodDays { get; set; }
    public int DashboardRefreshIntervalSeconds { get; set; }
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
    [Required]
    [StringLength(50)]
    public string ItemSku { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string LocationCode { get; set; } = string.Empty;

    public decimal CurrentQuantity { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal NewQuantity { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public class ReceivingViewModel
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string LocationCode { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal Quantity { get; set; }

    [StringLength(100)]
    public string? LotNumber { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [StringLength(100)]
    public string? ReferenceNumber { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class PickingViewModel
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string LocationCode { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal Quantity { get; set; }

    [StringLength(100)]
    public string OrderNumber { get; set; } = string.Empty;

    [StringLength(100)]
    public string? LotNumber { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class PutawayViewModel
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string FromLocationCode { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ToLocationCode { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.0001", "1000000000")]
    public decimal Quantity { get; set; }

    [StringLength(100)]
    public string? LotNumber { get; set; }

    [StringLength(100)]
    public string? SerialNumber { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class CreateItemViewModel
{
    [Required]
    [StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required]
    [StringLength(20)]
    public string? UnitOfMeasure { get; set; } = "EA";

    public bool RequiresLot { get; set; }
    public bool RequiresSerial { get; set; }

    [StringLength(50)]
    public string? Barcode { get; set; }
}

public class EditItemViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; set; } = "EA";

    public bool RequiresLot { get; set; }
    public bool RequiresSerial { get; set; }

    [StringLength(50)]
    public string? Barcode { get; set; }
}

public class CreateLocationViewModel
{
    [Required]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    public bool IsPickable { get; set; } = true;
    public bool IsReceivable { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int Capacity { get; set; }
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}
