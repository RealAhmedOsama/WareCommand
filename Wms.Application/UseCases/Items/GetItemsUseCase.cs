// Wms.Application/UseCases/Items/GetItemsUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Items;

public interface IGetItemsUseCase
{
    Task<Result<IEnumerable<ItemDto>>> ExecuteAsync(string? searchTerm = null,
        CancellationToken cancellationToken = default);

    Task<Result<ItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<ItemDto>> GetBySkuAsync(string sku, CancellationToken cancellationToken = default);
    Task<Result<ItemDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
}

public class GetItemsUseCase : IGetItemsUseCase
{
    private readonly ILogger<GetItemsUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public GetItemsUseCase(
        IUnitOfWork unitOfWork,
        ILogger<GetItemsUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<IEnumerable<ItemDto>>> ExecuteAsync(string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<IEnumerable<ItemDto>>(authorization.Error);
            }

            var items = string.IsNullOrWhiteSpace(searchTerm)
                ? await _unitOfWork.Items.GetAllAsync(cancellationToken)
                : await _unitOfWork.Items.SearchAsync(searchTerm, cancellationToken);

            var itemDtos = items.Select(MapToDto);
            return Result.Success(itemDtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving items");
            return Result.Failure<IEnumerable<ItemDto>>($"Error retrieving items: {ex.Message}");
        }
    }

    public async Task<Result<ItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<ItemDto>(authorization.Error);
            }

            var item = await _unitOfWork.Items.GetByIdAsync(id, cancellationToken);
            if (item == null)
                return Result.Failure<ItemDto>($"Item with ID {id} not found");

            return Result.Success(MapToDto(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving item {ItemId}", id);
            return Result.Failure<ItemDto>($"Error retrieving item: {ex.Message}");
        }
    }

    public async Task<Result<ItemDto>> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<ItemDto>(authorization.Error);
            }

            var item = await _unitOfWork.Items.GetBySkuAsync(sku, cancellationToken);
            if (item == null)
                return Result.Failure<ItemDto>($"Item with SKU '{sku}' not found");

            return Result.Success(MapToDto(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving item {ItemSku}", sku);
            return Result.Failure<ItemDto>($"Error retrieving item: {ex.Message}");
        }
    }

    public async Task<Result<ItemDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsRead,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<ItemDto>(authorization.Error);
            }

            var item = await _unitOfWork.Items.GetByBarcodeAsync(new Barcode(barcode), cancellationToken);
            if (item == null)
                return Result.Failure<ItemDto>($"Item with barcode '{barcode}' not found");

            return Result.Success(MapToDto(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving item by barcode {Barcode}", barcode);
            return Result.Failure<ItemDto>($"Error retrieving item: {ex.Message}");
        }
    }

    private static ItemDto MapToDto(Item item)
    {
        return new ItemDto(
            item.Id,
            item.Sku,
            item.Name,
            item.Description,
            item.UnitOfMeasure,
            item.IsActive,
            item.RequiresLot,
            item.RequiresSerial,
            item.ShelfLifeDays,
            item.Barcodes.Select(b => b.Value).ToList(),
            item.CreatedAt,
            item.UpdatedAt
        );
    }
}
