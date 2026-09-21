using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Reporting;
using Wms.Application.Settings;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Application.UseCases.Reports;

public record MovementReportDto(
    int Id,
    string Type,
    string ItemSku,
    string ItemName,
    string? FromLocationCode,
    string? ToLocationCode,
    decimal Quantity,
    string? LotNumber,
    string? SerialNumber,
    string UserId,
    string? ReferenceNumber,
    string? Notes,
    DateTime Timestamp)
{
    public decimal BaseQuantity => Quantity;
    public string BaseUnitOfMeasure { get; init; } = "BASE";
    public decimal EnteredQuantity { get; init; } = Quantity;
    public string EnteredUnitOfMeasure { get; init; } = "BASE";
    public decimal DisplayQuantity { get; init; } = Quantity;
    public string DisplayUnitOfMeasure { get; init; } = "BASE";
    public decimal ConversionRoundingDelta { get; init; }
    public string? PackagingCode { get; init; }
    public string? PackagingName { get; init; }
    public PackagingType? PackagingType { get; init; }
    public int? PackagingVersion { get; init; }
    public decimal? PackagingUnitsPerPackage { get; init; }
    public InventoryOwnerKind OwnerKind { get; init; } = InventoryOwnerKind.CompanyOwned;
    public int? InventoryOwnerId { get; init; }
    public string OwnerCodeSnapshot { get; init; } = InventoryOwnershipDimension.CompanyOwnerCode;
}

// These values are business dates in the effective warehouse time zone. They
// are converted to a half-open UTC range before querying persistence.
public record MovementReportRequest(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? ItemSku = null,
    string? LocationCode = null,
    MovementType? MovementType = null,
    string? UserId = null,
    string? DisplayUnitOfMeasure = null,
    int? WarehouseId = null,
    string? SearchTerm = null,
    string? ReferenceNumber = null,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? LicensePlateNumber = null,
    MovementReportSort Sort = MovementReportSort.Timestamp,
    bool Descending = true,
    int Page = 1,
    int PageSize = 0);

public interface IMovementReportUseCase
{
    Task<Result<IEnumerable<MovementReportDto>>> ExecuteAsync(
        MovementReportRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Compatibility facade for the dashboard and retained desktop report
/// screen. New paged/reporting endpoints use IReportQueryService directly.
/// </summary>
public sealed class MovementReportUseCase(
    IReportQueryService reportQueryService,
    IWmsSettingsService settingsService,
    ILogger<MovementReportUseCase> logger) : IMovementReportUseCase
{
    public async Task<Result<IEnumerable<MovementReportDto>>> ExecuteAsync(
        MovementReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var settingsResult = await settingsService.GetAsync(
                cancellationToken: cancellationToken);
            var settings = settingsResult.IsSuccess
                ? settingsResult.Value.Values
                : WmsSettingsDefaults.Create();
            if (settingsResult.IsFailure)
            {
                logger.LogWarning(
                    "Report settings could not be loaded; defaults will be used: {ErrorCode}",
                    settingsResult.ErrorCode);
            }

            var maximumRows = Math.Max(1, settings.Reports.MaximumRows);
            var pageSize = request.PageSize > 0
                ? Math.Min(request.PageSize, maximumRows)
                : Math.Min(maximumRows, 200);
            var rows = new List<MovementReportDto>(Math.Min(maximumRows, pageSize));
            var page = Math.Max(1, request.Page);

            while (rows.Count < maximumRows)
            {
                var result = await reportQueryService.QueryMovementLedgerAsync(
                    new MovementLedgerQuery(
                        request.FromDate.HasValue
                            ? DateOnly.FromDateTime(request.FromDate.Value)
                            : null,
                        request.ToDate.HasValue
                            ? DateOnly.FromDateTime(request.ToDate.Value)
                            : null,
                        request.WarehouseId,
                        request.SearchTerm,
                        request.ItemSku,
                        request.LocationCode,
                        request.MovementType,
                        request.UserId,
                        request.ReferenceNumber,
                        request.LotNumber,
                        request.SerialNumber,
                        request.LicensePlateNumber,
                        request.Sort,
                        request.Descending,
                        page,
                        pageSize,
                        request.DisplayUnitOfMeasure),
                    cancellationToken);
                if (result.IsFailure)
                {
                    return result.ToFailure<IEnumerable<MovementReportDto>>();
                }

                rows.AddRange(result.Value.Items);
                if (result.Value.Items.Count == 0 || page >= result.Value.TotalPages)
                {
                    break;
                }

                page++;
            }

            if (rows.Count > maximumRows)
            {
                rows = rows.Take(maximumRows).ToList();
            }

            logger.LogInformation("Movement report generated with {Count} records", rows.Count);
            return Result.Success<IEnumerable<MovementReportDto>>(rows);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error generating movement report");
            return Result.Failure<IEnumerable<MovementReportDto>>(WmsErrors.FromException(
                exception,
                "reports.generation_failed",
                "Error generating report. Please try again."));
        }
    }
}
