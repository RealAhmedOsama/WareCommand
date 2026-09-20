// Wms.Application/UseCases/Inventory/GetStockUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
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
                return Result.Failure<IEnumerable<StockDto>>(authorization.Error);
            }

            var stockItems = await _unitOfWork.Stock.GetAllAsync(cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all stock");
            return Result.Failure<IEnumerable<StockDto>>($"Error retrieving stock: {ex.Message}");
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
                return Result.Failure<IEnumerable<StockDto>>(authorization.Error);
            }

            var stockItems = await _unitOfWork.Stock.GetByItemIdAsync(itemId, cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock for item {ItemId}", itemId);
            return Result.Failure<IEnumerable<StockDto>>($"Error retrieving stock: {ex.Message}");
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
                return Result.Failure<IEnumerable<StockDto>>(authorization.Error);
            }

            var stockItems = await _unitOfWork.Stock.GetByLocationIdAsync(locationId, cancellationToken);
            var stockDtos = stockItems.Select(MapToDto);
            return Result.Success(stockDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock for location {LocationId}", locationId);
            return Result.Failure<IEnumerable<StockDto>>($"Error retrieving stock: {ex.Message}");
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
                return Result.Failure<IEnumerable<StockSummaryDto>>(authorization.Error);
            }

            var stockItems = await _unitOfWork.Stock.GetAllAsync(cancellationToken);

            var summaries = stockItems
                .GroupBy(s => new { s.Item.Sku, s.Item.Name })
                .Select(g => new StockSummaryDto(
                    g.Key.Sku,
                    g.Key.Name,
                    g.Sum(s => s.QuantityAvailable.Value),
                    g.Sum(s => s.QuantityReserved.Value),
                    g.Sum(s => s.GetAvailableQuantity().Value),
                    g.Select(s => s.LocationId).Distinct().Count()
                ));

            return Result.Success(summaries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving stock summary");
            return Result.Failure<IEnumerable<StockSummaryDto>>($"Error retrieving stock summary: {ex.Message}");
        }
    }

    private static StockDto MapToDto(Stock stock)
    {
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
            stock.UpdatedAt
        );
    }
}
