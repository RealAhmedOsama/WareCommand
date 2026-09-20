using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Wms.Application.Units;
using Wms.Domain.Enums;

namespace Wms.ASP.Models;

public sealed class UnitOfMeasureListViewModel
{
    public IReadOnlyList<UnitOfMeasureDto> Items { get; init; } = [];
    public string? SearchTerm { get; init; }
    public UnitOfMeasureCategory? Category { get; init; }
    public bool IncludeInactive { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class UnitOfMeasureFormViewModel
{
    [Required, StringLength(20)]
    [RegularExpression("^[A-Za-z0-9_.-]+$")]
    public string Code { get; set; } = string.Empty;

    [EnumDataType(typeof(UnitOfMeasureCategory))]
    public UnitOfMeasureCategory Category { get; set; } = UnitOfMeasureCategory.Count;

    [Range(0, 12)]
    public int Precision { get; set; } = 4;

    [Required, StringLength(20)]
    public string Symbol { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? LocalizedName { get; set; }

    public UnitOfMeasureRequest ToRequest() =>
        new(Code, Category, Precision, Symbol, Name, LocalizedName);

    public static UnitOfMeasureFormViewModel From(UnitOfMeasureDto unit) => new()
    {
        Code = unit.Code,
        Category = unit.Category,
        Precision = unit.Precision,
        Symbol = unit.Symbol,
        Name = unit.Name,
        LocalizedName = unit.LocalizedName
    };
}

public sealed class ItemUnitAssignmentViewModel
{
    [Range(1, int.MaxValue)]
    public int ItemId { get; set; }

    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string BaseUnitOfMeasure { get; set; } = string.Empty;

    [StringLength(20)]
    public string? PurchaseUnitOfMeasure { get; set; }

    [StringLength(20)]
    public string? SalesUnitOfMeasure { get; set; }

    public bool AllowFractionalQuantity { get; set; } = true;

    [StringLength(20_000)]
    public string? ConversionsText { get; set; }

    public IReadOnlyList<ItemUnitConversionDto> ExistingConversions { get; set; } = [];

    public static ItemUnitAssignmentViewModel From(ItemUnitAssignmentDto assignment) => new()
    {
        ItemId = assignment.ItemId,
        Sku = assignment.Sku,
        BaseUnitOfMeasure = assignment.BaseUnitOfMeasure,
        PurchaseUnitOfMeasure = assignment.PurchaseUnitOfMeasure,
        SalesUnitOfMeasure = assignment.SalesUnitOfMeasure,
        AllowFractionalQuantity = assignment.AllowFractionalQuantity,
        ExistingConversions = assignment.Conversions,
        ConversionsText = string.Join(
            Environment.NewLine,
            assignment.Conversions
                .Where(conversion => conversion.IsActive)
                .Select(conversion => string.Join(
                    "|",
                    conversion.FromUnitOfMeasure,
                    conversion.ToUnitOfMeasure,
                    conversion.ConversionFactor.ToString(CultureInfo.InvariantCulture),
                    conversion.ResultPrecision.ToString(CultureInfo.InvariantCulture),
                    conversion.RoundingMode.ToString())))
    };
}
