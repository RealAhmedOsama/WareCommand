using Wms.Application.Common;

namespace Wms.Application.Customers;

public enum CustomerSortField
{
    Code,
    LegalName,
    Priority,
    UpdatedAt,
    CreatedAt
}

public sealed record CustomerListQuery(
    string? SearchTerm = null,
    bool IncludeInactive = true,
    CustomerSortField SortBy = CustomerSortField.Code,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

public sealed record CustomerShipToAddressInput(
    int? Id,
    string Code,
    string RecipientName,
    string? Phone = null,
    string? CountryCode = null,
    string? Region = null,
    string? City = null,
    string? PostalCode = null,
    string AddressLine1 = "",
    string? AddressLine2 = null,
    string? DeliveryInstructions = null,
    TimeSpan? DeliveryWindowStart = null,
    TimeSpan? DeliveryWindowEnd = null,
    bool IsDefault = false,
    bool IsActive = true);

public sealed record CustomerItemReferenceInput(
    int? Id,
    int ItemId,
    string CustomerSku,
    string? CustomerBarcode = null,
    string? CustomerDescription = null,
    bool IsActive = true);

public sealed record CustomerInput(
    string Code,
    string LegalName,
    string? LocalizedName = null,
    string? TaxRegistrationNumber = null,
    string? ExternalErpIdentifier = null,
    string? ExternalChannelIdentifier = null,
    string? ContactName = null,
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? BillingAddressLine1 = null,
    string? BillingAddressLine2 = null,
    string? BillingCity = null,
    string? BillingRegion = null,
    string? BillingPostalCode = null,
    string? BillingCountryCode = null,
    string? DefaultCarrierCode = null,
    string? DefaultCarrierServiceCode = null,
    int Priority = 100,
    string? PackagingProfile = null,
    string? LabelProfile = null,
    bool AllowPartialShipment = false,
    string? Notes = null,
    bool IsActive = true,
    IReadOnlyList<CustomerShipToAddressInput>? ShipToAddresses = null,
    IReadOnlyList<CustomerItemReferenceInput>? ItemReferences = null);

public sealed record CustomerShipToAddressDto(
    int Id,
    string Code,
    string RecipientName,
    string? Phone,
    string? CountryCode,
    string? Region,
    string? City,
    string? PostalCode,
    string AddressLine1,
    string? AddressLine2,
    string? DeliveryInstructions,
    TimeSpan? DeliveryWindowStart,
    TimeSpan? DeliveryWindowEnd,
    bool IsDefault,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CustomerItemReferenceDto(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    string CustomerSku,
    string? CustomerBarcode,
    string? CustomerDescription,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CustomerDto(
    int Id,
    string Code,
    string LegalName,
    string? LocalizedName,
    string? TaxRegistrationNumber,
    string? ExternalErpIdentifier,
    string? ExternalChannelIdentifier,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingCity,
    string? BillingRegion,
    string? BillingPostalCode,
    string? BillingCountryCode,
    string? DefaultCarrierCode,
    string? DefaultCarrierServiceCode,
    int Priority,
    string? PackagingProfile,
    string? LabelProfile,
    bool AllowPartialShipment,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<CustomerShipToAddressDto> ShipToAddresses,
    IReadOnlyList<CustomerItemReferenceDto> ItemReferences,
    bool CanDelete);

public sealed record CustomerPageDto(
    IReadOnlyList<CustomerDto> Customers,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Values copied to a confirmed outbound document. This is intentionally a
/// scalar DTO so callers cannot accidentally retain a live customer entity.
/// </summary>
public sealed record CustomerDocumentSnapshot(
    int CustomerId,
    string CustomerCode,
    string CustomerLegalName,
    string? CustomerLocalizedName,
    string? CustomerContactName,
    string? CustomerContactEmail,
    string? CustomerContactPhone,
    int? ShipToAddressId,
    string? ShipToCode,
    string? ShipToRecipientName,
    string? ShipToPhone,
    string? ShipToCountryCode,
    string? ShipToRegion,
    string? ShipToCity,
    string? ShipToPostalCode,
    string? ShipToAddressLine1,
    string? ShipToAddressLine2,
    string? ShipToDeliveryInstructions,
    TimeSpan? ShipToDeliveryWindowStart,
    TimeSpan? ShipToDeliveryWindowEnd,
    string? DefaultCarrierCode,
    string? DefaultCarrierServiceCode,
    int Priority,
    string? PackagingProfile,
    string? LabelProfile,
    bool AllowPartialShipment);

public sealed record CustomerDocumentSnapshotQuery(
    int CustomerId,
    int? ShipToAddressId = null,
    string? ShipToCode = null);

public sealed record CustomerItemReferenceResolutionQuery(
    int CustomerId,
    string? CustomerSku = null,
    string? CustomerBarcode = null,
    bool IncludeInactive = false);

public sealed record CustomerItemReferenceResolutionDto(
    int CustomerId,
    string CustomerCode,
    int ItemId,
    string ItemSku,
    string ItemName,
    string CustomerSku,
    string? CustomerBarcode,
    string? CustomerDescription,
    bool IsActive);

public sealed record CustomerImportError(int Row, string Message);

public sealed record CustomerImportResult(
    int ImportedCustomerCount,
    int ImportedShipToCount,
    IReadOnlyList<CustomerImportError> Errors);

public interface ICustomerManagementService
{
    Task<Result<CustomerPageDto>> ListAsync(
        CustomerListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> CreateAsync(
        CustomerInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerDto>> UpdateAsync(
        int id,
        CustomerInput input,
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

    Task<Result<CustomerImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        CustomerListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerDocumentSnapshot>> GetDocumentSnapshotAsync(
        CustomerDocumentSnapshotQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<CustomerItemReferenceResolutionDto>> ResolveItemReferenceAsync(
        CustomerItemReferenceResolutionQuery query,
        CancellationToken cancellationToken = default);
}
