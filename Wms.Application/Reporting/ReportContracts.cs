using Wms.Application.Common;
using Wms.Application.UseCases.Reports;
using Wms.Domain.Enums;

namespace Wms.Application.Reporting;

public enum MovementReportSort
{
    Timestamp,
    ItemSku,
    Quantity,
    MovementType,
    User
}

public enum MovementReportGroupBy
{
    None,
    Item,
    Location,
    MovementType,
    User
}

/// <summary>
/// Business-date and warehouse-scoped movement ledger criteria. Dates are
/// inclusive business dates and are converted to a half-open UTC range by the
/// report infrastructure service.
/// </summary>
public sealed record MovementLedgerQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int? WarehouseId = null,
    string? SearchTerm = null,
    string? ItemSku = null,
    string? LocationCode = null,
    MovementType? MovementType = null,
    string? UserId = null,
    string? ReferenceNumber = null,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? LicensePlateNumber = null,
    MovementReportSort Sort = MovementReportSort.Timestamp,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50,
    string? DisplayUnitOfMeasure = null,
    string? BusinessTimeZoneId = null);

public sealed record ReportMetadata(
    string ReportName,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset DataCutoffUtc,
    string TimeZoneId,
    string? UserId,
    IReadOnlyDictionary<string, string?> Filters);

public sealed record ReportPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    ReportMetadata Metadata);

public sealed record ReportGroupRowDto(
    string Key,
    int RowCount,
    decimal TotalBaseQuantity);

public sealed record ReportGroupPageDto(
    IReadOnlyList<ReportGroupRowDto> Groups,
    int Page,
    int PageSize,
    int TotalGroups,
    int TotalPages,
    ReportMetadata Metadata);

public interface IReportQueryService
{
    Task<Result<ReportPage<MovementReportDto>>> QueryMovementLedgerAsync(
        MovementLedgerQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<ReportGroupPageDto>> GroupMovementLedgerAsync(
        MovementLedgerQuery query,
        MovementReportGroupBy groupBy,
        CancellationToken cancellationToken = default);
}
