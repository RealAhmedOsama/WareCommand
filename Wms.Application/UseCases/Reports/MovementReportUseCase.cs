// Wms.Application/UseCases/Reports/MovementReportUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Identity;
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
);

public record MovementReportRequest(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? ItemSku = null,
    string? LocationCode = null,
    MovementType? MovementType = null,
    string? UserId = null
);

public interface IMovementReportUseCase
{
    Task<Result<IEnumerable<MovementReportDto>>> ExecuteAsync(MovementReportRequest request,
        CancellationToken cancellationToken = default);
}

public class MovementReportUseCase : IMovementReportUseCase
{
    private readonly ILogger<MovementReportUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public MovementReportUseCase(
        IUnitOfWork unitOfWork,
        ILogger<MovementReportUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
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
                return Result.Failure<IEnumerable<MovementReportDto>>(authorization.Error);
            }

            var movements = await GetFilteredMovementsAsync(request, cancellationToken);
            var reportData = movements.Select(MapToDto);

            _logger.LogInformation("Movement report generated with {Count} records", reportData.Count());
            return Result.Success(reportData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating movement report");
            return Result.Failure<IEnumerable<MovementReportDto>>($"Error generating report: {ex.Message}");
        }
    }

    private async Task<IEnumerable<Movement>> GetFilteredMovementsAsync(
        MovementReportRequest request, CancellationToken cancellationToken)
    {
        // Start with date range if provided
        if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            return await _unitOfWork.Movements.GetByDateRangeAsync(
                request.FromDate.Value, request.ToDate.Value, cancellationToken);
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

        // Default to last 30 days
        var utcToday = DateTime.UtcNow.Date;
        var defaultFromDate = utcToday.AddDays(-30);
        var defaultToDate = utcToday.AddDays(1);
        return await _unitOfWork.Movements.GetByDateRangeAsync(defaultFromDate, defaultToDate, cancellationToken);
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
            movement.Timestamp
        );
    }
}
