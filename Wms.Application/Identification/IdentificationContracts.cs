using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Identification;

public sealed record IdentificationLookupRequest(
    string Value,
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record IdentificationRegistrationRequest(
    string Value,
    IdentificationKind Kind,
    string OwnerKey,
    BarcodeSymbology? Symbology = null,
    string? Alias = null,
    bool IsActive = true,
    DateTime? ValidFromUtc = null,
    DateTime? ValidToUtc = null);

public sealed record IdentificationGs1Dto(
    string? Gtin,
    string? Lot,
    string? Serial,
    DateOnly? ExpiryDate,
    decimal? Quantity,
    string? Sscc,
    IReadOnlyDictionary<string, string> ApplicationIdentifiers);

public sealed record IdentificationResolutionDto(
    string OriginalValue,
    string NormalizedValue,
    IdentificationKind Kind,
    BarcodeSymbology Symbology,
    string OwnerKey,
    bool IsActive,
    DateTime ValidFromUtc,
    DateTime? ValidToUtc,
    int? EntityId = null,
    string? ItemSku = null,
    string? PackagingCode = null,
    string? LocationCode = null,
    int? WarehouseId = null,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? LicensePlate = null,
    IdentificationGs1Dto? Gs1 = null);

public interface IIdentificationService
{
    Task<Result<IdentificationResolutionDto>> ResolveAsync(
        IdentificationLookupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the dedicated identifier registry synchronized while legacy fields
/// remain available for backwards-compatible reads and migration fallback.
/// </summary>
public interface IIdentificationRegistry
{
    Task<Result> SyncItemAsync(
        Item item,
        CancellationToken cancellationToken = default);

    Task<Result> SyncLocationAsync(
        Location location,
        CancellationToken cancellationToken = default);

    Task<Result<WmsIdentifier>> RegisterAsync(
        IdentificationRegistrationRequest request,
        CancellationToken cancellationToken = default);
}
