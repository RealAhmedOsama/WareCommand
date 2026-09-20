// Wms.Application/UseCases/Inventory/GetStockUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;

namespace Wms.Application.UseCases.Inventory;

public interface IGetStockUseCase
{
    Task<Result<IEnumerable<StockDto>>> GetAllStockAsync(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<StockDto>>> GetStockByItemAsync(int itemId, CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<StockDto>>> GetStockByLocationAsync(int locationId,
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<StockSummaryDto>>> GetStockSummaryAsync(CancellationToken cancellationToken = default);
}

public class GetStockUseCase : IGetStockUseCase
{
    private readonly ILogger<GetStockUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public GetStockUseCase(
        IUnitOfWork unitOfWork,
        ILogger<GetStockUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<IEnumerable<StockDto>>> GetAllStockAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<StockDto>>();
            }

            var stockItems = await _unitOfWork.Stock.GetAllAsync(cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all stock");
            return Result.Failure<IEnumerable<StockDto>>(WmsErrors.FromException(ex,
                "inventory.read_failed",
                "Error retrieving stock. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<StockDto>>> GetStockByItemAsync(int itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<StockDto>>();
            }

            var stockItems = await _unitOfWork.Stock.GetByItemIdAsync(itemId, cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock for item {ItemId}", itemId);
            return Result.Failure<IEnumerable<StockDto>>(WmsErrors.FromException(ex,
                "inventory.read_failed",
                "Error retrieving stock. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<StockDto>>> GetStockByLocationAsync(int locationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<StockDto>>();
            }

            var stockItems = await _unitOfWork.Stock.GetByLocationIdAsync(locationId, cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock for location {LocationId}", locationId);
            return Result.Failure<IEnumerable<StockDto>>(WmsErrors.FromException(ex,
                "inventory.read_failed",
                "Error retrieving stock. Please try again."));
        }
    }

    public async Task<Result<IEnumerable<StockSummaryDto>>> GetStockSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IEnumerable<StockSummaryDto>>();
            }

            var stockItems = await _unitOfWork.Stock.GetAllAsync(cancellationToken);

            var summaries = stockItems
                .GroupBy(s => new { s.Item.Sku, s.Item.Name })
                .SelectMany(g => g
                    .GroupBy(s => new
                    {
                        Code = s.InventoryStatus?.Code ?? InventoryStatusCodes.Available,
                        Name = s.InventoryStatus?.Name ?? "Available",
                        IsAllocatable = s.InventoryStatus?.IsAllocatable ?? true
                    })
                    .Select(statusGroup => new StockSummaryDto(
                        g.Key.Sku,
                        g.Key.Name,
                        statusGroup.Sum(s => s.QuantityAvailable.Value),
                        statusGroup.Sum(s => s.QuantityReserved.Value),
                        statusGroup.Sum(s => s.GetAvailableQuantity().Value),
                        statusGroup.Select(s => s.LocationId).Distinct().Count(),
                        statusGroup.Key.Code,
                        statusGroup.Key.Name,
                        statusGroup.Key.IsAllocatable)));

            return Result.Success(summaries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock summary");
            return Result.Failure<IEnumerable<StockSummaryDto>>(WmsErrors.FromException(ex,
                "inventory.summary_failed",
                "Error retrieving stock summary. Please try again."));
        }
    }

    private static StockDto MapToDto(Stock stock)
    {
        var statusId = stock.InventoryStatus?.Id ?? stock.InventoryStatusId;
        var statusCode = stock.InventoryStatus?.Code ?? InventoryStatusCodes.Available;
        var statusName = stock.InventoryStatus?.Name ?? "Available";
        var isAllocatable = stock.InventoryStatus?.IsAllocatable ?? true;
        var isPickable = stock.InventoryStatus?.IsPickable ?? true;
        var isShippable = stock.InventoryStatus?.IsShippable ?? true;
        return new StockDto(
            stock.Id,
            stock.ItemId,
            stock.Item.Sku,
            stock.Item.Name,
            stock.LocationId,
            stock.Location.Code,
            stock.Location.Name,
            stock.LotId,
            stock.Lot?.Number,
            stock.SerialNumber,
            stock.QuantityAvailable.Value,
            stock.QuantityReserved.Value,
            stock.GetAvailableQuantity().Value,
            stock.CreatedAt,
            stock.UpdatedAt,
            stock.SerialNumberId,
            statusId,
            statusCode,
            statusName,
            isAllocatable,
            isPickable,
            isShippable,
            stock.LicensePlateId,
            stock.LicensePlate?.Number
        );
    }
}
