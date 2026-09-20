using System.ComponentModel.DataAnnotations;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.UseCases.Reports;
using Wms.Application.Warehouses;
using Wms.Domain.Enums;

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
    public List<InventoryReplenishmentSignalDto> LowStockSignals { get; set; } = new();
    public DateTime LastRefresh { get; set; }
    public string DisplayTimeZone { get; set; } = "UTC";
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
    public int StockPage { get; set; } = 1;
    public int StockPageSize { get; set; } = 50;
    public int StockTotalCount { get; set; }
    public int StockTotalPages { get; set; }
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
    public int? WarehouseId { get; set; }
    public LocationType? Type { get; set; }
    public bool IncludeInactive { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}

public class StockAdjustmentViewModel
{
    [Required]
    [StringLength(50)]
    public string ItemSku { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string LocationCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? LotNumber { get; set; }

    public int? LicensePlateId { get; set; }

    public decimal CurrentQuantity { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal NewQuantity { get; set; }

    [StringLength(20)]
    public string? UnitOfMeasure { get; set; }

    [StringLength(40)]
    public string? PackagingCode { get; set; }

    [Required]
    [StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class WarehouseManagementViewModel
{
    public IReadOnlyList<WarehouseSummaryDto> Warehouses { get; set; } = [];
    public bool IncludeInactive { get; set; } = true;
}

public class CreateWarehouseViewModel
{
    [Required, StringLength(20, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? ArabicName { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(200)]
    public string? ContactName { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    [StringLength(320), EmailAddress]
    public string? ContactEmail { get; set; }

    [Required, StringLength(100)]
    public string TimeZone { get; set; } = "UTC";

    public bool AllowNegativeStock { get; set; }
    public bool RequireLocationForAdjustment { get; set; } = true;
    public bool BlockExpiredReceipt { get; set; } = true;

    [Range(0, 3650)]
    public int ExpiryWarningDays { get; set; } = 30;

    [Range(1, long.MaxValue)]
    public long NextReceiptNumber { get; set; } = 1;

    [Range(1, long.MaxValue)]
    public long NextOrderNumber { get; set; } = 1;

    [Range(1, long.MaxValue)]
    public long NextWorkNumber { get; set; } = 1;

    [Range(1, long.MaxValue)]
    public long NextShipmentNumber { get; set; } = 1;

    [Range(1, long.MaxValue)]
    public long NextTransferNumber { get; set; } = 1;

    [Range(1, long.MaxValue)]
    public long NextCountNumber { get; set; } = 1;
}

public sealed class EditWarehouseViewModel : CreateWarehouseViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

}

public sealed class ConfigureWarehouseLocationsViewModel
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ReceivingLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? StagingLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? StorageLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? PackingLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ShippingLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? QuarantineLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? DamagedLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ReturnsLocationId { get; set; }

    [Range(1, int.MaxValue)]
    public int? TransitLocationId { get; set; }

    public IReadOnlyList<WarehouseLocationOptionViewModel> Locations { get; set; } = [];
    public WarehouseDto? Warehouse { get; set; }

    public IReadOnlyList<WarehouseOperationalLocationSelection> ToSelections() =>
    [
        new(WarehouseOperationalLocationRole.Receiving, ReceivingLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Staging, StagingLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Storage, StorageLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Packing, PackingLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Shipping, ShippingLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Quarantine, QuarantineLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Damaged, DamagedLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Returns, ReturnsLocationId ?? 0),
        new(WarehouseOperationalLocationRole.Transit, TransitLocationId ?? 0)
    ];

    public static ConfigureWarehouseLocationsViewModel From(WarehouseDto warehouse) =>
        new()
        {
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            ReceivingLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Receiving),
            StagingLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Staging),
            StorageLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Storage),
            PackingLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Packing),
            ShippingLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Shipping),
            QuarantineLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Quarantine),
            DamagedLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Damaged),
            ReturnsLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Returns),
            TransitLocationId = GetLocationId(warehouse, WarehouseOperationalLocationRole.Transit)
        };

    private static int? GetLocationId(
        WarehouseDto warehouse,
        WarehouseOperationalLocationRole role) =>
        warehouse.OperationalLocations
            .Where(reference => reference.Role == role)
            .Select(reference => (int?)reference.LocationId)
            .FirstOrDefault();
}

public sealed record WarehouseLocationOptionViewModel(
    int Id,
    string Code,
    string Name,
    bool IsActive);

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

    [StringLength(20)]
    public string? UnitOfMeasure { get; set; }

    [StringLength(40)]
    public string? PackagingCode { get; set; }

    [StringLength(100)]
    public string? LotNumber { get; set; }

    public int? LicensePlateId { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ManufacturedDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ExpiryDate { get; set; }

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

    [StringLength(20)]
    public string? UnitOfMeasure { get; set; }

    [StringLength(40)]
    public string? PackagingCode { get; set; }

    [StringLength(100)]
    public string OrderNumber { get; set; } = string.Empty;

    [StringLength(100)]
    public string? LotNumber { get; set; }

    public int? LicensePlateId { get; set; }

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

    [StringLength(20)]
    public string? UnitOfMeasure { get; set; }

    [StringLength(40)]
    public string? PackagingCode { get; set; }

    [StringLength(100)]
    public string? LotNumber { get; set; }

    public int? LicensePlateId { get; set; }

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

    [Range(1, int.MaxValue)]
    public int? ParentLocationId { get; set; }

    [EnumDataType(typeof(LocationType))]
    public LocationType Type { get; set; } = LocationType.Storage;

    [StringLength(100)]
    public string? Barcode { get; set; }

    [Range(0, int.MaxValue)]
    public int Priority { get; set; }

    public bool IsPickable { get; set; } = true;
    public bool IsReceivable { get; set; } = true;
    public bool IsCountable { get; set; } = true;
    public bool AllowMixedItems { get; set; } = true;
    public bool AllowMixedLots { get; set; } = true;

    [Range(0, int.MaxValue)]
    public decimal? MaxUnits { get; set; } = 1000;

    [Range(0, double.MaxValue)]
    public decimal? MaxWeightKg { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? MaxVolumeCubicMeters { get; set; }

    [Range(0, int.MaxValue)]
    public int? MaxPallets { get; set; }

    [Range(0, int.MaxValue)]
    public int? MaxLpns { get; set; }

    [StringLength(100)]
    public string? StorageProfile { get; set; }

    public decimal? MinimumTemperatureCelsius { get; set; }
    public decimal? MaximumTemperatureCelsius { get; set; }

    [StringLength(100)]
    public string? HazardClass { get; set; }

    [StringLength(200)]
    public string? AccessRestriction { get; set; }

    [StringLength(4000)]
    public string? ConstraintAttributesJson { get; set; }

    public IReadOnlyList<LocationDto> ParentLocations { get; set; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}

public sealed class EditLocationViewModel : CreateLocationViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }
}

public sealed class GenerateLocationsViewModel
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [Required, StringLength(40)]
    public string CodePrefix { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string NamePrefix { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int StartNumber { get; set; }

    [Range(1, 1000)]
    public int Count { get; set; } = 1;

    [Range(1, 20)]
    public int NumberWidth { get; set; } = 3;

    [Range(1, int.MaxValue)]
    public int? ParentLocationId { get; set; }

    [EnumDataType(typeof(LocationType))]
    public LocationType Type { get; set; } = LocationType.Bin;

    public bool IsPickable { get; set; } = true;
    public bool IsReceivable { get; set; }
    public bool IsCountable { get; set; } = true;
    public bool AllowMixedItems { get; set; } = true;
    public bool AllowMixedLots { get; set; } = true;

    [Range(0, double.MaxValue)]
    public decimal? MaxUnits { get; set; } = 1000;

    [StringLength(100)]
    public string? StorageProfile { get; set; }

    public IReadOnlyList<LocationDto> ParentLocations { get; set; } = [];
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}

public sealed class ImportLocationsViewModel
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }

    [StringLength(2_000_000)]
    public string? Csv { get; set; }

    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}

public sealed class LocationLabelsViewModel
{
    [Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    public int[] LocationIds { get; set; } = [];
    public IReadOnlyList<LocationDto> Locations { get; set; } = [];
    public IReadOnlyList<Wms.Application.Locations.LocationLabelDto> Labels { get; set; } = [];
}

public sealed class LocationChildrenViewModel
{
    public int WarehouseId { get; set; }
    public int? ParentLocationId { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<LocationDto> Locations { get; set; } = [];
}
