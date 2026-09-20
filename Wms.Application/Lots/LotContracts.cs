using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Lots;

public sealed record LotDetailsRequest(
    DateTime? ExpiryDate = null,
    DateTime? ManufacturedDate = null,
    DateTime? RetestDate = null,
    DateTime? HoldUntil = null,
    string? SupplierLotNumber = null,
    string? Notes = null);

public sealed record LotDto(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    string Number,
    DateTime? ExpiryDate,
    DateTime? ManufacturedDate,
    DateTime? RetestDate,
    DateTime? HoldUntil,
    string? SupplierLotNumber,
    string? Notes,
    LotStatus Status,
    bool IsActive,
    string? RecallReason,
    DateTime? RecalledAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record LotStockDto(
    int StockId,
    int LocationId,
    string LocationCode,
    string LocationName,
    decimal QuantityAvailable,
    decimal QuantityReserved,
    decimal QuantityAllocatable,
    string? SerialNumber);

public sealed record LotMovementDto(
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

public sealed record LotTraceabilityDto(
    LotDto Lot,
    IReadOnlyList<LotStockDto> Stock,
    IReadOnlyList<LotMovementDto> Movements);

public sealed record LotSearchQuery(
    string? ItemSku = null,
    string? Number = null,
    LotStatus? Status = null,
    bool IncludeClosed = false);

public sealed record LotExpiryAlertDto(
    LotDto Lot,
    DateOnly ExpiryDate,
    bool IsExpired,
    int DaysFromBusinessDate);

public interface ILotService
{
    Task<Result<Lot>> ResolveForReceiptAsync(
        Item item,
        string lotNumber,
        LotDetailsRequest details,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<Lot?>> ResolveForMovementAsync(
        Item item,
        string? lotNumber,
        bool requireAllocationEligibility,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);

    Task<Result<LotTraceabilityDto>> GetTraceabilityAsync(
        int lotId,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<LotDto>>> SearchAsync(
        LotSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<LotExpiryAlertDto>>> GetExpiryAlertsAsync(
        DateOnly businessDate,
        int warningDays,
        CancellationToken cancellationToken = default);

    Task<Result<LotDto>> UpdateAsync(
        int lotId,
        LotDetailsRequest details,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LotDto>> ChangeStatusAsync(
        int lotId,
        LotStatus status,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);
}
