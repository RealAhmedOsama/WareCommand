using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Packing;

public sealed record PackingStationInput(
    string Code,
    string Name,
    int WarehouseId,
    int LocationId,
    string? SupportedDevices = null,
    string? PrinterProfile = null,
    string? ScaleProfile = null,
    string? AllowedUserIds = null,
    string? PermissionProfile = null);

public sealed record PackingStationDto(
    int Id,
    string Code,
    string Name,
    int WarehouseId,
    int LocationId,
    PackingStationStatus Status,
    string? SupportedDevices,
    string? PrinterProfile,
    string? ScaleProfile,
    string? AllowedUserIds,
    string? PermissionProfile,
    long Revision);

public sealed record PackingSessionStartInput(
    string SessionNumber,
    int WarehouseId,
    int PackingStationId,
    PackingSourceType SourceType,
    string SourceReference,
    int? SalesOrderId = null,
    int? StagingLicensePlateId = null,
    string IdempotencyKey = "");

public sealed record PackingSessionDto(
    int Id,
    string SessionNumber,
    int WarehouseId,
    int PackingStationId,
    PackingSourceType SourceType,
    string SourceReference,
    int? SalesOrderId,
    int? StagingLicensePlateId,
    PackingSessionStatus Status,
    string UserId,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<ShipmentPackageDto> Packages,
    long Revision);

public sealed record PackingPackageCreateInput(
    int PackingSessionId,
    string PackageNumber,
    int TargetLicensePlateId,
    PackingPackageType PackageType,
    int? SalesOrderId = null,
    bool AllowConsolidated = false,
    decimal? ExpectedWeightKg = null,
    decimal WeightToleranceKg = 0m,
    decimal WeightTolerancePercent = 0m,
    string? LabelReference = null,
    string IdempotencyKey = "");

public sealed record PackingScanInput(
    int ShipmentPackageId,
    int SalesOrderLineId,
    int ItemId,
    int SourceLocationId,
    decimal Quantity,
    int InventoryStatusId = 1,
    int? SourceLicensePlateId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    string IdempotencyKey = "",
    string? ScanReference = null);

public sealed record PackingPackageMeasureInput(
    int ShipmentPackageId,
    string IdempotencyKey,
    decimal? ActualWeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null);

public sealed record PackingPackageCommandInput(
    int ShipmentPackageId,
    string IdempotencyKey,
    string? Reason = null);

public sealed record ShipmentPackageContentDto(
    int Id,
    int ShipmentPackageId,
    int SalesOrderLineId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int? SourceLicensePlateId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int InventoryStatusId,
    decimal? ExpectedWeightKg,
    long Revision);

public sealed record ShipmentPackageDto(
    int Id,
    string PackageNumber,
    int WarehouseId,
    int PackingSessionId,
    int TargetLicensePlateId,
    PackingPackageType PackageType,
    int? SalesOrderId,
    bool AllowConsolidated,
    ShipmentPackageStatus Status,
    decimal PackedQuantity,
    decimal? ExpectedWeightKg,
    decimal? ActualWeightKg,
    decimal WeightToleranceKg,
    decimal WeightTolerancePercent,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal? VolumeCubicMeters,
    string? LabelReference,
    string? ClosedByUserId,
    DateTime? ClosedAtUtc,
    IReadOnlyList<ShipmentPackageContentDto> Contents,
    long Revision);

public interface IPackingService
{
    Task<Result<PackingStationDto>> CreateStationAsync(
        PackingStationInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PackingStationDto>> SetStationStatusAsync(
        int stationId,
        PackingStationStatus status,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PackingSessionDto>> StartSessionAsync(
        PackingSessionStartInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PackingSessionDto>> GetSessionAsync(
        int sessionId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> CreatePackageAsync(
        PackingPackageCreateInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> GetPackageAsync(
        int packageId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> PackAsync(
        PackingScanInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> ClosePackageAsync(
        PackingPackageMeasureInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> RemoveContentAsync(
        PackingScanInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> ReopenPackageAsync(
        PackingPackageCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentPackageDto>> VoidPackageAsync(
        PackingPackageCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PackingSessionDto>> CompleteSessionAsync(
        int sessionId,
        string idempotencyKey,
        string userId,
        CancellationToken cancellationToken = default);
}
