using Wms.Application.Common;
using Wms.Application.Lots;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.SerialNumbers;

public sealed record SerialDto(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    string Number,
    int? LotId,
    string? LotNumber,
    int? CurrentWarehouseId,
    string? CurrentWarehouseCode,
    int? CurrentLocationId,
    string? CurrentLocationCode,
    string? CurrentLicensePlate,
    SerialStatus Status,
    string? StatusReason,
    string? ReceiptReference,
    string? ShipmentReference,
    bool HasMigrationConflict,
    string? ConflictReason,
    DateTime? LastMovedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record SerialMovementDto(
    int MovementId,
    MovementType Type,
    decimal Quantity,
    int? FromLocationId,
    int? ToLocationId,
    string? FromLocationCode,
    string? ToLocationCode,
    string UserId,
    string? ReferenceNumber,
    string? Notes,
    DateTime Timestamp);

public sealed record SerialTraceabilityDto(
    SerialDto Serial,
    IReadOnlyList<LotStockDto> Stock,
    IReadOnlyList<SerialMovementDto> Movements);

public sealed record SerialSearchQuery(
    string? ItemSku = null,
    string? Number = null,
    SerialStatus? Status = null,
    bool IncludeMigrationConflicts = false);

public interface ISerialNumberService
{
    Task<Result<SerialNumber>> ResolveForReceiptAsync(
        Item item,
        string number,
        int? lotId,
        CancellationToken cancellationToken = default);

    Task<Result<SerialNumber>> ResolveForMovementAsync(
        Item item,
        string number,
        int? lotId,
        int? expectedLocationId,
        bool requireAllocationEligibility,
        CancellationToken cancellationToken = default);

    Task<Result<SerialTraceabilityDto>> GetTraceabilityAsync(
        int serialId,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<SerialDto>>> SearchAsync(
        SerialSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<SerialDto>> ChangeStatusAsync(
        int serialId,
        SerialStatus status,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);
}
