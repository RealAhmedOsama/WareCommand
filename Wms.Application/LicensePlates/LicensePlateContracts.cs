using Wms.Application.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.LicensePlates;

public sealed record LicensePlateContentDto(
    int Id,
    int LicensePlateId,
    int ItemId,
    string? ItemSku,
    string? ItemName,
    int? LotId,
    string? LotNumber,
    int? SerialNumberId,
    string? SerialNumber,
    int InventoryStatusId,
    string? InventoryStatusCode,
    int? ItemPackagingId,
    string? ItemPackagingCode,
    decimal Quantity,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record LicensePlateDto(
    int Id,
    string Number,
    bool IsSscc,
    LicensePlateType Type,
    int WarehouseId,
    string? WarehouseCode,
    int? CurrentLocationId,
    string? CurrentLocationCode,
    LicensePlateStatus Status,
    bool IsActive,
    int? ParentLicensePlateId,
    string? ParentLicensePlateNumber,
    decimal? GrossWeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    string? SourceReference,
    string? Notes,
    IReadOnlyList<LicensePlateContentDto> Contents);

public sealed record LicensePlateHistoryDto(
    int Id,
    int LicensePlateId,
    LicensePlateHistoryAction Action,
    string UserId,
    DateTime OccurredAtUtc,
    int? ItemId,
    string? ItemSku,
    int? LotId,
    string? LotNumber,
    int? SerialNumberId,
    string? SerialNumber,
    decimal? Quantity,
    int? FromLocationId,
    int? ToLocationId,
    int? FromLicensePlateId,
    int? ToLicensePlateId,
    string? ReferenceNumber,
    string? Reason);

public sealed record LicensePlateCreateInput(
    int WarehouseId,
    int? CurrentLocationId,
    string? Number,
    bool IsSscc,
    LicensePlateType Type,
    decimal? GrossWeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    string? SourceReference = null,
    string? Notes = null);

public sealed record LicensePlateContentInput(
    int ItemId,
    decimal Quantity,
    int? LotId = null,
    int? SerialNumberId = null,
    int InventoryStatusId = 1,
    int? ItemPackagingId = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record LicensePlateSearchQuery(
    int? WarehouseId = null,
    string? Number = null,
    LicensePlateStatus? Status = null,
    bool IncludeVoided = false,
    int Page = 1,
    int PageSize = 50);

public sealed record LicensePlateNumberingInput(
    int WarehouseId,
    string Prefix,
    int Padding,
    long NextNumber);

public sealed record LicensePlateNumberingDto(
    int WarehouseId,
    string Prefix,
    int Padding,
    long NextNumber,
    long Revision);

public interface ILicensePlateService
{
    Task<Result<IReadOnlyList<LicensePlateDto>>> SearchAsync(
        LicensePlateSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> GetAsync(
        int licensePlateId,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> CreateAsync(
        LicensePlateCreateInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> AddContentAsync(
        int licensePlateId,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> MoveContentAsync(
        int sourceLicensePlateId,
        int targetLicensePlateId,
        LicensePlateContentInput input,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> SplitContentAsync(
        int sourceLicensePlateId,
        LicensePlateContentInput input,
        LicensePlateCreateInput destination,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> MergeAsync(
        int sourceLicensePlateId,
        int targetLicensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> MoveAsync(
        int licensePlateId,
        int targetLocationId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> NestAsync(
        int childLicensePlateId,
        int parentLicensePlateId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> UnnestAsync(
        int childLicensePlateId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> PackAsync(
        int licensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> ReopenAsync(
        int licensePlateId,
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> ShipAsync(
        int licensePlateId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> ReturnAsync(
        int licensePlateId,
        int targetLocationId,
        string userId,
        string? referenceNumber = null,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateDto>> VoidAsync(
        int licensePlateId,
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LicensePlateHistoryDto>>> GetHistoryAsync(
        int licensePlateId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    Task<Result<LicensePlateNumberingDto>> ConfigureNumberingAsync(
        LicensePlateNumberingInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
