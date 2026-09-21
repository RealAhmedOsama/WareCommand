using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Inventory;

public sealed record InventoryOwnerQuery(
    InventoryOwnerKind? Kind = null,
    bool IncludeInactive = false,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 100);

public sealed record InventoryOwnerInput(
    string OwnerCode,
    InventoryOwnerKind Kind,
    string DisplayName,
    string? LocalizedName = null,
    int? SupplierId = null,
    int? CustomerId = null,
    string? ExternalOwnerReference = null);

public sealed record InventoryOwnerDto(
    int Id,
    string OwnerCode,
    InventoryOwnerKind Kind,
    string DisplayName,
    string? LocalizedName,
    int? SupplierId,
    int? CustomerId,
    string? ExternalOwnerReference,
    bool IsActive,
    long Revision);

public sealed record InventoryOwnershipTransferInput(
    string IdempotencyKey,
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int LocationId,
    int InventoryStatusId,
    InventoryOwnerKind SourceOwnerKind,
    int? SourceInventoryOwnerId,
    string? SourceOwnerCodeSnapshot,
    InventoryOwnerKind DestinationOwnerKind,
    int? DestinationInventoryOwnerId,
    string? DestinationOwnerCodeSnapshot,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    string? Reason = null);

public sealed record InventoryOwnershipTransferDto(
    int Id,
    string TransferNumber,
    string IdempotencyKey,
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int LocationId,
    int InventoryStatusId,
    InventoryOwnerKind SourceOwnerKind,
    int? SourceInventoryOwnerId,
    string SourceOwnerCodeSnapshot,
    InventoryOwnerKind DestinationOwnerKind,
    int? DestinationInventoryOwnerId,
    string DestinationOwnerCodeSnapshot,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    string? Reason,
    InventoryOwnershipTransferStatus Status,
    string CreatedByUserId,
    DateTime CreatedAtUtc,
    string? CompletedByUserId,
    DateTime? CompletedAtUtc,
    string? ExceptionReason,
    long Revision);

public interface IInventoryOwnershipService
{
    Task<Result<IReadOnlyList<InventoryOwnerDto>>> ListOwnersAsync(
        InventoryOwnerQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryOwnerDto>> GetOwnerAsync(
        int ownerId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryOwnerDto>> CreateOwnerAsync(
        InventoryOwnerInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryOwnerDto>> DeactivateOwnerAsync(
        int ownerId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryOwnershipTransferDto>> TransferAsync(
        InventoryOwnershipTransferInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InventoryOwnershipTransferDto>>> ListTransfersAsync(
        int? warehouseId = null,
        int? itemId = null,
        InventoryOwnershipTransferStatus? status = null,
        CancellationToken cancellationToken = default);
}
