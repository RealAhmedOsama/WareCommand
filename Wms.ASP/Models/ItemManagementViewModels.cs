using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Wms.Application.DTOs;
using Wms.Application.Items;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class ItemListViewModel
{
    public IReadOnlyList<ItemDto> Items { get; init; } = [];
    public string? SearchTerm { get; init; }
    public ItemType? Type { get; init; }
    public ItemLifecycleStatus? LifecycleStatus { get; init; }
    public string? Category { get; init; }
    public bool IncludeInactive { get; init; } = true;
    public ItemSortField SortBy { get; init; } = ItemSortField.Sku;
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class ItemFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? LocalizedName { get; set; }

    [StringLength(1_000)]
    public string? Description { get; set; }

    [StringLength(1_000)]
    public string? LocalizedDescription { get; set; }

    [StringLength(100)]
    public string? Category { get; set; }

    [StringLength(100)]
    public string? Brand { get; set; }

    [EnumDataType(typeof(ItemType))]
    public ItemType Type { get; set; } = ItemType.Stock;

    [EnumDataType(typeof(ItemLifecycleStatus))]
    public ItemLifecycleStatus LifecycleStatus { get; set; } = ItemLifecycleStatus.Active;

    [StringLength(500)]
    public string? ImageReference { get; set; }

    [StringLength(500)]
    public string? DocumentReference { get; set; }

    [Required, StringLength(20)]
    public string BaseUnit { get; set; } = "EA";

    [StringLength(20)]
    public string? PurchaseUnit { get; set; }

    [StringLength(20)]
    public string? SalesUnit { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? NetWeightKg { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? LengthCm { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? WidthCm { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? HeightCm { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? VolumeCubicMeters { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? StandardCost { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? SalesPrice { get; set; }

    [StringLength(100)]
    public string? CountryOfOrigin { get; set; }

    [StringLength(100)]
    public string? CustomsCode { get; set; }

    public bool RequiresLot { get; set; }
    public bool RequiresSerial { get; set; }
    public bool RequiresExpiry { get; set; }

    [Range(0, 36_500)]
    public int ShelfLifeDays { get; set; }

    public bool UseFefo { get; set; }
    public bool QualityInspectionRequired { get; set; }
    public bool IsHazardous { get; set; }
    public bool TemperatureControlled { get; set; }
    public bool SpecialHandlingRequired { get; set; }
    public decimal? MinimumTemperatureCelsius { get; set; }
    public decimal? MaximumTemperatureCelsius { get; set; }

    [StringLength(100)]
    public string? StorageProfile { get; set; }

    [StringLength(100)]
    public string? PutawayProfile { get; set; }

    [StringLength(100)]
    public string? DefaultSupplierCode { get; set; }

    [EnumDataType(typeof(ItemReorderPolicy))]
    public ItemReorderPolicy ReorderPolicy { get; set; } = ItemReorderPolicy.None;

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? MinimumStock { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? MaximumStock { get; set; }

    [Range(typeof(decimal), "0", "1000000000000")]
    public decimal? SafetyStock { get; set; }

    [Range(0, 36_500)]
    public int? LeadTimeDays { get; set; }

    [StringLength(10_000)]
    public string? BarcodesText { get; set; }

    [StringLength(20_000)]
    public string? PackagingsText { get; set; }

    public static ItemFormViewModel From(ItemDto item) => new()
    {
        Id = item.Id,
        Sku = item.Sku,
        Name = item.Name,
        LocalizedName = item.LocalizedName,
        Description = item.Description,
        LocalizedDescription = item.LocalizedDescription,
        Category = item.Category,
        Brand = item.Brand,
        Type = item.Type,
        LifecycleStatus = item.LifecycleStatus,
        ImageReference = item.ImageReference,
        DocumentReference = item.DocumentReference,
        BaseUnit = item.UnitOfMeasure,
        PurchaseUnit = item.PurchaseUnit,
        SalesUnit = item.SalesUnit,
        NetWeightKg = item.NetWeightKg,
        LengthCm = item.LengthCm,
        WidthCm = item.WidthCm,
        HeightCm = item.HeightCm,
        VolumeCubicMeters = item.VolumeCubicMeters,
        StandardCost = item.StandardCost,
        SalesPrice = item.SalesPrice,
        CountryOfOrigin = item.CountryOfOrigin,
        CustomsCode = item.CustomsCode,
        RequiresLot = item.RequiresLot,
        RequiresSerial = item.RequiresSerial,
        RequiresExpiry = item.RequiresExpiry,
        ShelfLifeDays = item.ShelfLifeDays,
        UseFefo = item.UseFefo,
        QualityInspectionRequired = item.QualityInspectionRequired,
        IsHazardous = item.IsHazardous,
        TemperatureControlled = item.TemperatureControlled,
        SpecialHandlingRequired = item.SpecialHandlingRequired,
        MinimumTemperatureCelsius = item.MinimumTemperatureCelsius,
        MaximumTemperatureCelsius = item.MaximumTemperatureCelsius,
        StorageProfile = item.StorageProfile,
        PutawayProfile = item.PutawayProfile,
        DefaultSupplierCode = item.DefaultSupplierCode,
        ReorderPolicy = item.ReorderPolicy,
        MinimumStock = item.MinimumStock,
        MaximumStock = item.MaximumStock,
        SafetyStock = item.SafetyStock,
        LeadTimeDays = item.LeadTimeDays,
        BarcodesText = string.Join(Environment.NewLine, item.Barcodes),
        PackagingsText = string.Join(
            Environment.NewLine,
            item.Packagings.Select(packaging => string.Join(
                "|",
                packaging.Code,
                packaging.UnitOfMeasure,
                packaging.UnitsPerPackage.ToString(CultureInfo.InvariantCulture),
                packaging.Barcode,
                packaging.GrossWeightKg?.ToString(CultureInfo.InvariantCulture),
                packaging.LengthCm?.ToString(CultureInfo.InvariantCulture),
                packaging.WidthCm?.ToString(CultureInfo.InvariantCulture),
                packaging.HeightCm?.ToString(CultureInfo.InvariantCulture),
                packaging.IsDefault ? "true" : "false")))
    };
}

public sealed class ItemImportViewModel
{
    public IFormFile? File { get; set; }

    [StringLength(2_000_000)]
    public string? Csv { get; set; }
}

public sealed class ItemDuplicateViewModel
{
    [Range(1, int.MaxValue)]
    public int SourceItemId { get; set; }

    [Required, StringLength(50)]
    public string Sku { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Name { get; set; }

    public bool CopyPackagings { get; set; } = true;

    public ItemDto? SourceItem { get; set; }
}
