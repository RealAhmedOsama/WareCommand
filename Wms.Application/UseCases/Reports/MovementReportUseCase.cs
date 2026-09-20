// Wms.Application/UseCases/Reports/MovementReportUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;

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
    DateTime Timestamp
)
{
    public decimal BaseQuantity => Quantity;
    public string BaseUnitOfMeasure { get; init; } = "BASE";
    public decimal EnteredQuantity { get; init; } = Quantity;
    public string EnteredUnitOfMeasure { get; init; } = "BASE";
    public decimal DisplayQuantity { get; init; } = Quantity;
    public string DisplayUnitOfMeasure { get; init; } = "BASE";
    public decimal ConversionRoundingDelta { get; init; }
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
    string? DisplayUnitOfMeasure = null
);

public interface IMovementReportUseCase
{
    Task<Result<IEnumerable<MovementReportDto>>> ExecuteAsync(MovementReportRequest request,
        CancellationToken cancellationToken = default);
}

public class MovementReportUseCase : IMovementReportUseCase
{
    private readonly ILogger<MovementReportUseCase> _logger;
    private readonly IClock _clock;
    private readonly IWmsSettingsService _settingsService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IItemQuantityConversionService? _quantityConversionService;

    public MovementReportUseCase(
        IUnitOfWork unitOfWork,
        ILogger<MovementReportUseCase> logger,
        IWarehouseAccessService warehouseAccessService,
        IClock clock,
        IWmsSettingsService settingsService,
        IItemQuantityConversionService? quantityConversionService = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
        _clock = clock;
        _settingsService = settingsService;
        _quantityConversionService = quantityConversionService;
    }

    public async Task<Result<IEnumerable<MovementReportDto>>> ExecuteAsync(MovementReportRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReportsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<MovementReportDto>>();
            }

            var settingsResult = await _settingsService.GetAsync(cancellationToken: cancellationToken);
            var settings = settingsResult.IsSuccess
                ? settingsResult.Value.Values
                : WmsSettingsDefaults.Create();
            if (settingsResult.IsFailure)
            {
                _logger.LogWarning(
                    "Report settings could not be loaded; UTC defaults will be used: {ErrorCode}",
                    settingsResult.ErrorCode);
            }

            var movements = await GetFilteredMovementsAsync(request, settings, cancellationToken);
            var reportData = new List<MovementReportDto>();
            foreach (var movement in movements)
            {
                var row = MapToDto(movement);
                if (!string.IsNullOrWhiteSpace(request.DisplayUnitOfMeasure) &&
                    _quantityConversionService is not null)
                {
                    var displayResult = await _quantityConversionService.ConvertFromBaseAsync(
                        movement.ItemId,
                        movement.Quantity.Value,
                        request.DisplayUnitOfMeasure,
                        QuantityRoundingMode.ToEven,
                        cancellationToken);
                    if (displayResult.IsFailure)
                    {
                        return displayResult.ToFailure<IEnumerable<MovementReportDto>>();
                    }

                    row = row with
                    {
                        DisplayQuantity = displayResult.Value,
                        DisplayUnitOfMeasure = request.DisplayUnitOfMeasure.Trim().ToUpperInvariant()
                    };
                }

                reportData.Add(row);
            }

            _logger.LogInformation("Movement report generated with {Count} records", reportData.Count);
            return Result.Success<IEnumerable<MovementReportDto>>(reportData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating movement report");
            return Result.Failure<IEnumerable<MovementReportDto>>(WmsErrors.FromException(ex,
                "reports.generation_failed",
                "Error generating report. Please try again."));
        }
    }

    private async Task<IEnumerable<Movement>> GetFilteredMovementsAsync(
        MovementReportRequest request,
        WmsSettingsValues settings,
        CancellationToken cancellationToken)
    {
        if (request.FromDate.HasValue || request.ToDate.HasValue)
        {
            var today = WmsBusinessTime.GetBusinessDate(_clock.UtcNow, settings.Localization.TimeZone);
            var fromDate = request.FromDate.HasValue
                ? DateOnly.FromDateTime(request.FromDate.Value)
                : today.AddDays(-settings.Reports.DefaultPeriodDays);
            var toDate = request.ToDate.HasValue
                ? DateOnly.FromDateTime(request.ToDate.Value)
                : today;
            var range = WmsBusinessTime.GetInclusiveDateRange(
                fromDate,
                toDate,
                settings.Localization.TimeZone);
            return await _unitOfWork.Movements.GetByDateRangeAsync(
                range.FromUtc,
                range.ToUtcExclusive,
                cancellationToken);
        }

        // Filter by movement type if provided
        if (request.MovementType.HasValue)
        {
            return await _unitOfWork.Movements.GetByTypeAsync(request.MovementType.Value, cancellationToken);
        }

        // Filter by user if provided
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            return await _unitOfWork.Movements.GetByUserIdAsync(request.UserId, cancellationToken);
        }

        var businessToday = WmsBusinessTime.GetBusinessDate(_clock.UtcNow, settings.Localization.TimeZone);
        var defaultRange = WmsBusinessTime.GetInclusiveDateRange(
            businessToday.AddDays(-settings.Reports.DefaultPeriodDays),
            businessToday,
            settings.Localization.TimeZone);
        return await _unitOfWork.Movements.GetByDateRangeAsync(
            defaultRange.FromUtc,
            defaultRange.ToUtcExclusive,
            cancellationToken);
    }

    private static MovementReportDto MapToDto(Movement movement)
    {
        return new MovementReportDto(
            movement.Id,
            movement.Type.ToString(),
            movement.Item.Sku,
            movement.Item.Name,
            movement.FromLocation?.Code,
            movement.ToLocation?.Code,
            movement.Quantity.Value,
            movement.Lot?.Number,
            movement.SerialNumber,
            movement.UserId,
            movement.ReferenceNumber,
            movement.Notes,
            movement.Timestamp)
        {
            BaseUnitOfMeasure = movement.BaseUnitOfMeasure,
            EnteredQuantity = movement.EnteredQuantity,
            EnteredUnitOfMeasure = movement.EnteredUnitOfMeasure,
            DisplayQuantity = movement.Quantity.Value,
            DisplayUnitOfMeasure = movement.BaseUnitOfMeasure,
            ConversionRoundingDelta = movement.ConversionRoundingDelta
        };
    }
}
