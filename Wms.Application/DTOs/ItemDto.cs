// Wms.Application/DTOs/ItemDto.cs

using Wms.Domain.Enums;

namespace Wms.Application.DTOs;

public record ItemDto(
    int Id,
    string Sku,
    string Name,
    string Description,
    string UnitOfMeasure,
    bool IsActive,
    bool RequiresLot,
    bool RequiresSerial,
    int ShelfLifeDays,
    List<string> Barcodes,
    DateTime CreatedAt,
    DateTime? UpdatedAt
)
{
    public string LocalizedName { get; init; } = string.Empty;
    public string LocalizedDescription { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Brand { get; init; }
    public ItemType Type { get; init; } = ItemType.Stock;
    public ItemLifecycleStatus LifecycleStatus { get; init; } = ItemLifecycleStatus.Active;
    public string? ImageReference { get; init; }
    public string? DocumentReference { get; init; }
    public string PurchaseUnit { get; init; } = string.Empty;
    public string SalesUnit { get; init; } = string.Empty;
    public bool RequiresExpiry { get; init; }
    public bool AllowFractionalQuantity { get; init; } = true;
    public bool UseFefo { get; init; }
    public bool QualityInspectionRequired { get; init; }
    public bool IsHazardous { get; init; }
    public bool TemperatureControlled { get; init; }
    public bool SpecialHandlingRequired { get; init; }
    public decimal? NetWeightKg { get; init; }
    public decimal? LengthCm { get; init; }
    public decimal? WidthCm { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? VolumeCubicMeters { get; init; }
    public decimal? StandardCost { get; init; }
    public decimal? SalesPrice { get; init; }
    public string? CountryOfOrigin { get; init; }
    public string? CustomsCode { get; init; }
    public decimal? MinimumTemperatureCelsius { get; init; }
    public decimal? MaximumTemperatureCelsius { get; init; }
    public string? StorageProfile { get; init; }
    public string? PutawayProfile { get; init; }
    public string? DefaultSupplierCode { get; init; }
    public ItemReorderPolicy ReorderPolicy { get; init; } = ItemReorderPolicy.None;
    public decimal? MinimumStock { get; init; }
    public decimal? MaximumStock { get; init; }
    public decimal? SafetyStock { get; init; }
    public int? LeadTimeDays { get; init; }
    public IReadOnlyList<ItemPackagingDto> Packagings { get; init; } = [];
}

public sealed record ItemPackagingDto(
    int Id,
    string Code,
    string UnitOfMeasure,
    decimal UnitsPerPackage,
    string? Barcode,
    decimal? GrossWeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    bool IsDefault);

public record CreateItemDto(
    string Sku,
    string Name,
    string Description,
    string UnitOfMeasure,
    bool RequiresLot = false,
    bool RequiresSerial = false,
    int ShelfLifeDays = 0,
    List<string> Barcodes = null!
)
{
    public List<string> Barcodes { get; init; } = Barcodes ?? new List<string>();
}

public record UpdateItemDto(
    string Name,
    string Description,
    int ShelfLifeDays = 0
);
