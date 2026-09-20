// Wms.Application/UseCases/Items/CreateItemUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Items;

public record CreateItemDto(
    string Sku,
    string Name,
    string Description,
    string UnitOfMeasure,
    bool RequiresLot = false,
    bool RequiresSerial = false,
    int ShelfLifeDays = 0,
    List<string> Barcodes = null!
);

public record UpdateItemDto(
    int Id,
    string Name,
    string Description,
    int ShelfLifeDays = 0,
    List<string> Barcodes = null!
);

public interface ICreateItemUseCase
{
    Task<Result<ItemDto>> ExecuteAsync(CreateItemDto request, string userId,
        CancellationToken cancellationToken = default);
}

public interface IUpdateItemUseCase
{
    Task<Result<ItemDto>> ExecuteAsync(UpdateItemDto request, string userId,
        CancellationToken cancellationToken = default);
}

public interface IDeleteItemUseCase
{
    Task<Result> ExecuteAsync(int itemId, string userId,
        CancellationToken cancellationToken = default);
}

public class CreateItemUseCase : ICreateItemUseCase
{
    private readonly ILogger<CreateItemUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public CreateItemUseCase(
        IUnitOfWork unitOfWork,
        ILogger<CreateItemUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<ItemDto>> ExecuteAsync(CreateItemDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsManage,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<ItemDto>(authorization.Error);
            }

            // Check if SKU already exists
            var existingItem = await _unitOfWork.Items.GetBySkuAsync(request.Sku, cancellationToken);
            if (existingItem != null)
                return Result.Failure<ItemDto>($"Item with SKU '{request.Sku}' already exists");

            // Create new item
            var item = new Item(
                request.Sku,
                request.Name,
                request.UnitOfMeasure,
                request.RequiresLot,
                request.RequiresSerial);

            if (!string.IsNullOrWhiteSpace(request.Description))
                item.UpdateDetails(request.Name, request.Description);

            if (request.ShelfLifeDays > 0)
                item.SetShelfLife(request.ShelfLifeDays);

            // Add barcodes
            if (request.Barcodes?.Any() == true)
            {
                foreach (var barcodeValue in request.Barcodes)
                {
                    if (!string.IsNullOrWhiteSpace(barcodeValue))
                    {
                        var barcode = new Barcode(barcodeValue);
                        item.AddBarcode(barcode);
                    }
                }
            }

            await _unitOfWork.Items.AddAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Item created: {ItemSku} by {UserId}", request.Sku, userId);

            return Result.Success(MapToDto(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating item {ItemSku}", request.Sku);
            return Result.Failure<ItemDto>($"Error creating item: {ex.Message}");
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

public class UpdateItemUseCase : IUpdateItemUseCase
{
    private readonly ILogger<UpdateItemUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public UpdateItemUseCase(
        IUnitOfWork unitOfWork,
        ILogger<UpdateItemUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<ItemDto>> ExecuteAsync(UpdateItemDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsManage,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<ItemDto>(authorization.Error);
            }

            var item = await _unitOfWork.Items.GetByIdAsync(request.Id, cancellationToken);
            if (item == null)
                return Result.Failure<ItemDto>($"Item with ID {request.Id} not found");

            // Update item details
            item.UpdateDetails(request.Name, request.Description);

            if (request.ShelfLifeDays >= 0)
                item.SetShelfLife(request.ShelfLifeDays);

            // Update barcodes (simplified - remove all and add new ones)
            var currentBarcodes = item.Barcodes.ToList();
            foreach (var barcode in currentBarcodes)
            {
                item.RemoveBarcode(barcode);
            }

            if (request.Barcodes?.Any() == true)
            {
                foreach (var barcodeValue in request.Barcodes)
                {
                    if (!string.IsNullOrWhiteSpace(barcodeValue))
                    {
                        var barcode = new Barcode(barcodeValue);
                        item.AddBarcode(barcode);
                    }
                }
            }

            await _unitOfWork.Items.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Item updated: {ItemSku} by {UserId}", item.Sku, userId);

            return Result.Success(MapToDto(item));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating item {ItemId}", request.Id);
            return Result.Failure<ItemDto>($"Error updating item: {ex.Message}");
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

public class DeleteItemUseCase : IDeleteItemUseCase
{
    private readonly ILogger<DeleteItemUseCase> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public DeleteItemUseCase(
        IUnitOfWork unitOfWork,
        ILogger<DeleteItemUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result> ExecuteAsync(int itemId, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ItemsManage,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }

            var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken);
            if (item == null)
                return Result.Failure($"Item with ID {itemId} not found");

            // Check if item has stock before deleting
            var stockItems = await _unitOfWork.Stock.GetByItemIdAsync(itemId, cancellationToken);
            if (stockItems.Any(s => s.QuantityAvailable.Value > 0))
            {
                return Result.Failure("Cannot delete item with existing stock. Please adjust stock to zero first.");
            }

            // Soft delete by deactivating
            item.Deactivate();
            await _unitOfWork.Items.UpdateAsync(item, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Item deactivated: {ItemSku} by {UserId}", item.Sku, userId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting item {ItemId}", itemId);
            return Result.Failure($"Error deleting item: {ex.Message}");
        }
    }
}
