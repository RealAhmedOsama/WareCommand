using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Transfers;

public sealed record TransferLineInput(
    int ItemId,
    decimal RequestedQuantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int DestinationLocationId,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int SourceInventoryStatusId = InventoryStatusSystemIds.Available,
    int DestinationInventoryStatusId = InventoryStatusSystemIds.Available,
    string? Notes = null);

public sealed record TransferOrderInput(
    string TransferNumber,
    string CreationIdempotencyKey,
    int SourceWarehouseId,
    int DestinationWarehouseId,
    int TransitLocationId,
    IReadOnlyList<TransferLineInput> Lines,
    int Priority = 50,
    string? ExternalReference = null,
    string? Notes = null);

public sealed record TransferCommandInput(
    int TransferOrderId,
    string IdempotencyKey,
    string? Reason = null);

public sealed record TransferQuantityCommandInput(
    int TransferOrderId,
    int TransferLineId,
    decimal Quantity,
    string IdempotencyKey,
    string? Reason = null);

public sealed record InternalMovementInput(
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int DestinationLocationId,
    string IdempotencyKey,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int InventoryStatusId = InventoryStatusSystemIds.Available,
    string? Reason = null);

public sealed record TransferQuery(
    int? SourceWarehouseId = null,
    int? DestinationWarehouseId = null,
    TransferOrderStatus? Status = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 50);

public sealed record TransferOrderLineDto(
    int Id,
    int Sequence,
    int ItemId,
    decimal RequestedQuantity,
    decimal ShippedQuantity,
    decimal ReceivedQuantity,
    decimal RemainingToShip,
    decimal RemainingToReceive,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int DestinationLocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int SourceInventoryStatusId,
    int DestinationInventoryStatusId,
    TransferLineStatus Status,
    long Revision);

public sealed record TransferOrderDto(
    int Id,
    string TransferNumber,
    int SourceWarehouseId,
    int DestinationWarehouseId,
    int TransitLocationId,
    int Priority,
    string? ExternalReference,
    string? Notes,
    TransferOrderStatus Status,
    decimal RequestedQuantity,
    decimal ShippedQuantity,
    decimal ReceivedQuantity,
    IReadOnlyList<TransferOrderLineDto> Lines,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? ReleasedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? ReceivedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? CancelledAtUtc,
    long Revision);

public sealed record TransferOrderPageDto(
    IReadOnlyList<TransferOrderDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record InternalMovementDto(
    int Id,
    string IdempotencyKey,
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int DestinationLocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    InternalMovementStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    long Revision);

public interface ITransferService
{
    Task<Result<TransferOrderPageDto>> ListAsync(
        TransferQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> GetAsync(
        int transferOrderId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> CreateAsync(
        TransferOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> ConfirmAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> ReleaseAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> ShipAsync(
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> ReceiveAsync(
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> CloseAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<TransferOrderDto>> CancelAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InternalMovementDto>> MoveAsync(
        InternalMovementInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
