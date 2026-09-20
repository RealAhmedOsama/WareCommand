using Wms.Application.Common;

namespace Wms.Application.Suppliers;

public enum SupplierSortField
{
    Code,
    LegalName,
    UpdatedAt,
    CreatedAt
}

public sealed record SupplierListQuery(
    string? SearchTerm = null,
    bool IncludeInactive = true,
    int? PreferredWarehouseId = null,
    SupplierSortField SortBy = SupplierSortField.Code,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

public sealed record SupplierReceivingDefaultsInput(
    int? PreferredWarehouseId = null,
    int? PreferredDockLocationId = null,
    int? DefaultLeadTimeDays = null,
    decimal? OverDeliveryTolerancePercent = null,
    decimal? UnderDeliveryTolerancePercent = null,
    bool RequiresLot = false,
    bool RequiresExpiry = false,
    string? QualityProfile = null,
    string? LabelRule = null,
    string? DefaultCurrencyCode = null);

public sealed record SupplierItemReferenceInput(
    int? Id,
    int ItemId,
    string VendorSku,
    string? VendorBarcode = null,
    int? ItemPackagingId = null,
    string? VendorPackaging = null,
    decimal? UnitsPerPurchasePackage = null,
    decimal MinimumOrderQuantity = 1,
    int? LeadTimeDays = null,
    bool IsActive = true);

public sealed record SupplierInput(
    string Code,
    string LegalName,
    string? LocalizedName = null,
    string? TaxRegistrationNumber = null,
    string? ExternalErpIdentifier = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? StateOrProvince = null,
    string? PostalCode = null,
    string? CountryCode = null,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    SupplierReceivingDefaultsInput? ReceivingDefaults = null,
    string? Notes = null,
    bool IsActive = true,
    IReadOnlyList<SupplierItemReferenceInput>? ItemReferences = null);

public sealed record SupplierItemReferenceDto(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    int? ItemPackagingId,
    string? ItemPackagingCode,
    string VendorSku,
    string? VendorBarcode,
    string? VendorPackaging,
    decimal? UnitsPerPurchasePackage,
    decimal MinimumOrderQuantity,
    int? LeadTimeDays,
    bool IsActive);

public sealed record SupplierDto(
    int Id,
    string Code,
    string LegalName,
    string? LocalizedName,
    string? TaxRegistrationNumber,
    string? ExternalErpIdentifier,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateOrProvince,
    string? PostalCode,
    string? CountryCode,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    int? PreferredWarehouseId,
    string? PreferredWarehouseCode,
    int? PreferredDockLocationId,
    string? PreferredDockCode,
    int? DefaultLeadTimeDays,
    decimal? OverDeliveryTolerancePercent,
    decimal? UnderDeliveryTolerancePercent,
    bool RequiresLot,
    bool RequiresExpiry,
    string? QualityProfile,
    string? LabelRule,
    string? DefaultCurrencyCode,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<SupplierItemReferenceDto> ItemReferences,
    bool CanDelete);

public sealed record SupplierPageDto(
    IReadOnlyList<SupplierDto> Suppliers,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record SupplierItemReferenceResolutionQuery(
    int SupplierId,
    string? VendorSku = null,
    string? VendorBarcode = null,
    bool IncludeInactive = false);

public sealed record SupplierItemReferenceResolutionDto(
    int SupplierId,
    string SupplierCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    int? ItemPackagingId,
    string? ItemPackagingCode,
    string VendorSku,
    string? VendorBarcode,
    string? VendorPackaging,
    decimal? UnitsPerPurchasePackage,
    decimal MinimumOrderQuantity,
    int? LeadTimeDays,
    int? SupplierLeadTimeDays,
    bool SupplierRequiresLot,
    bool SupplierRequiresExpiry,
    string? DefaultCurrencyCode);

public sealed record SupplierImportError(int Row, string Message);

public sealed record SupplierImportResult(
    int ImportedSupplierCount,
    int ImportedReferenceCount,
    IReadOnlyList<SupplierImportError> Errors);

public interface ISupplierManagementService
{
    Task<Result<SupplierPageDto>> ListAsync(
        SupplierListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> CreateAsync(
        SupplierInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierDto>> UpdateAsync(
        int id,
        SupplierInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        SupplierListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierItemReferenceResolutionDto>> ResolveItemReferenceAsync(
        SupplierItemReferenceResolutionQuery query,
        CancellationToken cancellationToken = default);
}
