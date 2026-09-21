using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Warehouses;

public sealed record WarehouseOperationalLocationDto(
    WarehouseOperationalLocationRole Role,
    int LocationId,
    string LocationCode,
    string LocationName,
    bool IsActive);

public sealed record WarehouseLocationOptionDto(
    int Id,
    string Code,
    string Name,
    bool IsActive);

public sealed record WarehouseNumberSequenceDto(
    long NextReceiptNumber,
    long NextOrderNumber,
    long NextWorkNumber,
    long NextShipmentNumber,
    long NextTransferNumber,
    long NextCountNumber,
    long Revision);

public sealed record WarehouseSummaryDto(
    int Id,
    string Code,
    string Name,
    string ArabicName,
    bool IsActive,
    bool WorkflowEnabled,
    bool WorkflowReady,
    int LocationCount,
    int ActiveLocationCount,
    int ConfiguredOperationalLocationCount,
    int AssignedUserCount);

public sealed record WarehouseListQuery(
    bool IncludeInactive = true,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 50);

public sealed record WarehousePageDto(
    IReadOnlyList<WarehouseSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record WarehouseDto(
    int Id,
    string Code,
    string Name,
    string ArabicName,
    string Address,
    string ContactName,
    string ContactPhone,
    string ContactEmail,
    string TimeZone,
    bool IsActive,
    bool WorkflowEnabled,
    bool AllowNegativeStock,
    bool RequireLocationForAdjustment,
    bool BlockExpiredReceipt,
    int ExpiryWarningDays,
    IReadOnlyList<WarehouseOperationalLocationDto> OperationalLocations,
    IReadOnlyList<WarehouseOperationalLocationRole> MissingOperationalLocationRoles,
    WarehouseNumberSequenceDto NumberSequence,
    int LocationCount,
    int ActiveLocationCount,
    int AssignedUserCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public bool WorkflowReady => MissingOperationalLocationRoles.Count == 0;
}

public sealed record CreateWarehouseRequest(
    string Code,
    string Name,
    string? ArabicName,
    string? Address,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string TimeZone,
    bool AllowNegativeStock,
    bool RequireLocationForAdjustment,
    bool BlockExpiredReceipt,
    int ExpiryWarningDays,
    long NextReceiptNumber = 1,
    long NextOrderNumber = 1,
    long NextWorkNumber = 1,
    long NextShipmentNumber = 1,
    long NextTransferNumber = 1,
    long NextCountNumber = 1);

public sealed record UpdateWarehouseRequest(
    int Id,
    string Name,
    string? ArabicName,
    string? Address,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    string TimeZone,
    bool AllowNegativeStock,
    bool RequireLocationForAdjustment,
    bool BlockExpiredReceipt,
    int ExpiryWarningDays,
    long NextReceiptNumber,
    long NextOrderNumber,
    long NextWorkNumber,
    long NextShipmentNumber,
    long NextTransferNumber,
    long NextCountNumber);

public sealed record WarehouseOperationalLocationSelection(
    WarehouseOperationalLocationRole Role,
    int LocationId);

public interface IWarehouseManagementService
{
    Task<Result<IReadOnlyList<WarehouseSummaryDto>>> ListAsync(
        bool includeInactive = true,
        CancellationToken cancellationToken = default);

    Task<Result<WarehousePageDto>> ListPageAsync(
        WarehouseListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WarehouseLocationOptionDto>>> ListLocationOptionsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseDto>> CreateAsync(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseDto>> UpdateAsync(
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> ConfigureOperationalLocationsAsync(
        int warehouseId,
        IReadOnlyCollection<WarehouseOperationalLocationSelection> selections,
        CancellationToken cancellationToken = default);

    Task<Result> EnableWorkflowsAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    Task<Result> ActivateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);
}
