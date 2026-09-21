using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Returns;

public sealed record ReturnLineInput(
    int ItemId,
    decimal ExpectedQuantity,
    int? SalesOrderLineId = null,
    int? ShipmentLineId = null,
    int? ExpectedLotId = null,
    int? ExpectedSerialNumberId = null);

public sealed record ReturnAuthorizationInput(
    string RmaNumber,
    int WarehouseId,
    IReadOnlyList<ReturnLineInput> Lines,
    int? CustomerId = null,
    int? SalesOrderId = null,
    int? ShipmentId = null,
    int? PackageId = null,
    int ReturnLocationId = 0,
    bool Unplanned = false,
    string Reason = "",
    string IdempotencyKey = "");

public sealed record ReturnReceiptInput(
    int ReturnAuthorizationId,
    int ReturnLineId,
    decimal Quantity,
    int InventoryStatusId = InventoryStatusSystemIds.ReturnPending,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    string IdempotencyKey = "");

public sealed record ReturnDispositionInput(
    int ReturnAuthorizationId,
    int ReturnReceiptId,
    ReturnDispositionKind Kind,
    decimal Quantity,
    int? DestinationLocationId = null,
    int? DestinationInventoryStatusId = null,
    string Reason = "",
    string IdempotencyKey = "");

public sealed record ReturnCommandInput(
    int ReturnAuthorizationId,
    string IdempotencyKey,
    string? Reason = null);

public sealed record ReturnLineDto(
    int Id,
    int ItemId,
    decimal ExpectedQuantity,
    decimal ReceivedQuantity,
    decimal DisposedQuantity,
    decimal RemainingToReceive,
    decimal RemainingToDispose,
    int? SalesOrderLineId,
    int? ShipmentLineId,
    int? ExpectedLotId,
    int? ExpectedSerialNumberId,
    long Revision);

public sealed record ReturnReceiptDto(
    int Id,
    int ReturnLineId,
    int ItemId,
    decimal Quantity,
    decimal DisposedQuantity,
    decimal RemainingToDispose,
    int ReturnLocationId,
    int InventoryStatusId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    ReturnReceiptStatus Status,
    DateTime ReceivedAtUtc,
    long Revision);

public sealed record ReturnDispositionDto(
    int Id,
    int ReturnReceiptId,
    ReturnDispositionKind Kind,
    decimal Quantity,
    int? DestinationLocationId,
    int? DestinationInventoryStatusId,
    string Reason,
    string UserId,
    DateTime DisposedAtUtc);

public sealed record ReturnAuthorizationDto(
    int Id,
    string RmaNumber,
    int WarehouseId,
    int? CustomerId,
    int? SalesOrderId,
    int? ShipmentId,
    int? PackageId,
    int ReturnLocationId,
    bool Unplanned,
    string Reason,
    ReturnStatus Status,
    decimal ExpectedQuantity,
    decimal ReceivedQuantity,
    decimal DisposedQuantity,
    IReadOnlyList<ReturnLineDto> Lines,
    IReadOnlyList<ReturnReceiptDto> Receipts,
    IReadOnlyList<ReturnDispositionDto> Dispositions,
    DateTime CreatedAtUtc,
    DateTime? AuthorizedAtUtc,
    DateTime? ReceivedAtUtc,
    DateTime? ClosedAtUtc,
    long Revision);

public interface IReturnService
{
    Task<Result<ReturnAuthorizationDto>> CreateAsync(
        ReturnAuthorizationInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> GetAsync(
        int returnAuthorizationId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> AuthorizeAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> InspectAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> ReceiveAsync(
        ReturnReceiptInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> DisposeAsync(
        ReturnDispositionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> CloseAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReturnAuthorizationDto>> CancelAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
