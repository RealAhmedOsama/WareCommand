using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Shipping;

public sealed record CarrierInput(
    string Code,
    string Name,
    string? TrackingUrlTemplate = null);

public sealed record CarrierServiceInput(
    int CarrierId,
    string Code,
    string Name,
    bool SupportsTracking = true,
    bool SupportsLabel = false);

public sealed record CarrierDto(
    int Id,
    string Code,
    string Name,
    CarrierStatus Status,
    string? TrackingUrlTemplate,
    IReadOnlyList<CarrierServiceDto> Services,
    long Revision);

public sealed record CarrierServiceDto(
    int Id,
    int CarrierId,
    string Code,
    string Name,
    bool SupportsTracking,
    bool SupportsLabel,
    bool IsActive,
    long Revision);

public sealed record ShipmentCreateInput(
    string ShipmentNumber,
    int WarehouseId,
    IReadOnlyList<int> ShipmentPackageIds,
    int CarrierId,
    int CarrierServiceId,
    DateTime? PlannedShipAtUtc = null,
    string? ExternalReference = null,
    string IdempotencyKey = "");

public sealed record ShipmentLoadInput(
    int ShipmentId,
    int? DockLocationId,
    string? TrailerNumber,
    string? RouteReference,
    string IdempotencyKey = "");

public sealed record ShipmentPackageScanInput(
    int ShipmentId,
    int ShipmentPackageId,
    int ShipmentLoadId,
    string IdempotencyKey = "");

public sealed record ShipmentCommandInput(
    int ShipmentId,
    string IdempotencyKey,
    string? Reason = null);

public sealed record ShipmentTrackingUpdateInput(
    int ShipmentId,
    string TrackingStatus,
    string? TrackingNumber,
    string Source,
    string? ProviderReference,
    DateTime? OccurredAtUtc,
    string? PayloadReference,
    string IdempotencyKey = "");

public sealed record ShipmentLineDto(
    int Id,
    int SalesOrderLineId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    long Revision);

public sealed record ShipmentPackageLinkDto(
    int Id,
    int ShipmentPackageId,
    string PackageNumber,
    int TargetLicensePlateId,
    ShipmentPackageStatus PackageStatus,
    ShipmentPackageLinkStatus Status,
    int? ShipmentLoadId,
    string? LoadedByUserId,
    DateTime? LoadedAtUtc,
    DateTime? ShippedAtUtc,
    long Revision);

public sealed record ShipmentLoadDto(
    int Id,
    int? DockLocationId,
    string? TrailerNumber,
    string? RouteReference,
    ShipmentLoadStatus Status,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    long Revision);

public sealed record ShipmentTrackingEventDto(
    int Id,
    string Status,
    string? TrackingNumber,
    string? ProviderReference,
    string Source,
    DateTime OccurredAtUtc,
    string? PayloadReference);

public sealed record ShipmentDto(
    int Id,
    string ShipmentNumber,
    int WarehouseId,
    int? CarrierId,
    int? CarrierServiceId,
    ShipmentStatus Status,
    string? ShipToRecipientName,
    string? ShipToPhone,
    string? ShipToCountryCode,
    string? ShipToRegion,
    string? ShipToCity,
    string? ShipToPostalCode,
    string? ShipToAddressLine1,
    string? ShipToAddressLine2,
    string? ShipToDeliveryInstructions,
    DateTime? PlannedShipAtUtc,
    DateTime? ActualShipAtUtc,
    string? ExternalReference,
    string? TrackingNumber,
    string? TrackingStatus,
    IReadOnlyList<ShipmentLineDto> Lines,
    IReadOnlyList<ShipmentPackageLinkDto> Packages,
    IReadOnlyList<ShipmentLoadDto> Loads,
    IReadOnlyList<ShipmentTrackingEventDto> TrackingEvents,
    long Revision);

public sealed record CarrierBookingRequest(
    string ShipmentNumber,
    string CarrierCode,
    string ServiceCode,
    IReadOnlyList<string> PackageNumbers);

public sealed record CarrierBookingResult(
    string? TrackingNumber,
    string? LabelReference,
    string? ProviderReference);

/// <summary>
/// Provider boundary for rate, label, booking, and tracking integrations.
/// Implementations must execute outside the shipment database transaction.
/// </summary>
public interface ICarrierAdapter
{
    string CarrierCode { get; }

    Task<Result<CarrierBookingResult>> BookAsync(
        CarrierBookingRequest request,
        CancellationToken cancellationToken = default);
}

public interface IShipmentService
{
    Task<Result<CarrierDto>> CreateCarrierAsync(
        CarrierInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<CarrierServiceDto>> CreateCarrierServiceAsync(
        CarrierServiceInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> CreateShipmentAsync(
        ShipmentCreateInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> GetShipmentAsync(
        int shipmentId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> OpenLoadAsync(
        ShipmentLoadInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> LoadPackageAsync(
        ShipmentPackageScanInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> UnloadPackageAsync(
        ShipmentPackageScanInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> ConfirmShipmentAsync(
        ShipmentCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> CancelShipmentAsync(
        ShipmentCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ShipmentDto>> UpdateTrackingAsync(
        ShipmentTrackingUpdateInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
