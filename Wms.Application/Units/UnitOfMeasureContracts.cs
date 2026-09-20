using Wms.Application.Common;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Units;

public sealed record UnitOfMeasureListQuery(
    string? SearchTerm = null,
    UnitOfMeasureCategory? Category = null,
    bool IncludeInactive = true,
    int Page = 1,
    int PageSize = 50);

public sealed record UnitOfMeasureDto(
    int Id,
    string Code,
    UnitOfMeasureCategory Category,
    int Precision,
    string Symbol,
    string Name,
    string LocalizedName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record UnitOfMeasurePageDto(
    IReadOnlyList<UnitOfMeasureDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record UnitOfMeasureRequest(
    string Code,
    UnitOfMeasureCategory Category,
    int Precision,
    string Symbol,
    string Name,
    string? LocalizedName = null);

public sealed record ItemUnitConversionDto(
    int Id,
    string FromUnitOfMeasure,
    string ToUnitOfMeasure,
    decimal ConversionFactor,
    int ResultPrecision,
    QuantityRoundingMode RoundingMode,
    int Version,
    bool IsActive,
    bool IsUsedByHistory);

public sealed record ItemUnitConversionRequest(
    string FromUnitOfMeasure,
    string ToUnitOfMeasure,
    decimal ConversionFactor,
    int ResultPrecision,
    QuantityRoundingMode RoundingMode = QuantityRoundingMode.Reject);

public sealed record ItemUnitAssignmentDto(
    int ItemId,
    string Sku,
    string BaseUnitOfMeasure,
    string PurchaseUnitOfMeasure,
    string SalesUnitOfMeasure,
    bool AllowFractionalQuantity,
    IReadOnlyList<ItemUnitConversionDto> Conversions);

public sealed record ItemUnitAssignmentRequest(
    int ItemId,
    string BaseUnitOfMeasure,
    string? PurchaseUnitOfMeasure,
    string? SalesUnitOfMeasure,
    bool AllowFractionalQuantity,
    IReadOnlyList<ItemUnitConversionRequest> Conversions);

public sealed record QuantityConversionResult(
    decimal EnteredQuantity,
    string EnteredUnitOfMeasure,
    decimal BaseQuantity,
    string BaseUnitOfMeasure,
    decimal ConversionFactorToBase,
    int ResultPrecision,
    QuantityRoundingMode RoundingMode,
    decimal RoundingDelta,
    string ConversionPath,
    string ConversionRuleIds)
{
    public PackagingConversionSnapshot? PackagingSnapshot { get; init; }

    public Wms.Domain.ValueObjects.QuantityConversionSnapshot ToSnapshot() =>
        new(
            EnteredQuantity,
            EnteredUnitOfMeasure,
            BaseUnitOfMeasure,
            ConversionFactorToBase,
            ResultPrecision,
            RoundingMode,
            RoundingDelta,
            ConversionPath,
            ConversionRuleIds,
            PackagingSnapshot);
}

public interface IUnitOfMeasureManagementService
{
    Task<Result<UnitOfMeasurePageDto>> ListAsync(
        UnitOfMeasureListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<UnitOfMeasureDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<UnitOfMeasureDto>> CreateAsync(
        UnitOfMeasureRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<UnitOfMeasureDto>> UpdateAsync(
        int id,
        UnitOfMeasureRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ItemUnitAssignmentDto>> GetItemAssignmentAsync(
        int itemId,
        CancellationToken cancellationToken = default);

    Task<Result<ItemUnitAssignmentDto>> SaveItemAssignmentAsync(
        ItemUnitAssignmentRequest request,
        string userId,
        CancellationToken cancellationToken = default);
}

public interface IItemQuantityConversionService
{
    Task<Result<QuantityConversionResult>> ConvertToBaseAsync(
        int itemId,
        decimal enteredQuantity,
        string? enteredUnitOfMeasure = null,
        QuantityRoundingMode? roundingMode = null,
        CancellationToken cancellationToken = default);

    Task<Result<QuantityConversionResult>> ConvertPackagingToBaseAsync(
        int itemId,
        decimal enteredPackageQuantity,
        string packagingCode,
        QuantityRoundingMode? roundingMode = null,
        CancellationToken cancellationToken = default);

    Task<Result<decimal>> ConvertFromBaseAsync(
        int itemId,
        decimal baseQuantity,
        string displayUnitOfMeasure,
        QuantityRoundingMode roundingMode = QuantityRoundingMode.Reject,
        CancellationToken cancellationToken = default);
}
