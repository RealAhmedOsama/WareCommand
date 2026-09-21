using Wms.Application.Common;
using Wms.Application.DTOs;

namespace Wms.Application.Inventory;

public enum InventoryInquirySort
{
    ItemSku,
    Location,
    Quantity,
    Expiry,
    Updated
}

/// <summary>
/// Composable, provider-neutral inventory inquiry criteria.
/// The infrastructure implementation applies every filter to the database query.
/// </summary>
public sealed record InventoryInquiryQuery(
    int? WarehouseId = null,
    int? LocationId = null,
    int? ItemId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    int? LicensePlateId = null,
    int? InventoryStatusId = null,
    string? ScanValue = null,
    string? SearchTerm = null,
    DateTime? ExpiryFrom = null,
    DateTime? ExpiryTo = null,
    bool AvailableOnly = false,
    bool IncludeZero = false,
    InventoryInquirySort Sort = InventoryInquirySort.ItemSku,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

public sealed record InventoryInquiryPageDto(
    IReadOnlyList<StockDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public interface IInventoryInquiryService
{
    Task<Result<InventoryInquiryPageDto>> QueryAsync(
        InventoryInquiryQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<StockSummaryDto>>> SummarizeAsync(
        InventoryInquiryQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryDashboardMetricsDto>> GetDashboardMetricsAsync(
        InventoryInquiryQuery query,
        DateOnly asOfBusinessDate,
        int expiryWarningDays,
        CancellationToken cancellationToken = default);
}
