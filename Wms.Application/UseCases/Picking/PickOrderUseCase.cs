// Wms.Application/UseCases/Picking/PickOrderUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Picking;

public record PickItemDto(
    string ItemSku,
    string FromLocationCode,
    decimal Quantity,
    string? OrderNumber = null,
    string? LotNumber = null,
    string? SerialNumber = null,
    string? Notes = null
);

public record PickResultDto(
    int MovementId,
    string ItemSku,
    string FromLocationCode,
    decimal Quantity,
    string? OrderNumber,
    DateTime Timestamp
);

public interface IPickOrderUseCase
{
    Task<Result<PickResultDto>> ExecuteAsync(PickItemDto request, string userId,
        CancellationToken cancellationToken = default);
}

public class PickOrderUseCase : IPickOrderUseCase
{
    private readonly ILogger<PickOrderUseCase> _logger;
    private readonly IStockMovementService _stockMovementService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public PickOrderUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<PickOrderUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<PickResultDto>> ExecuteAsync(PickItemDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PickingExecute,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<PickResultDto>(authorization.Error);
            }

            // Validate item exists
            var item = await _unitOfWork.Items.GetBySkuAsync(request.ItemSku, cancellationToken);
            if (item == null)
                return Result.Failure<PickResultDto>($"Item with SKU '{request.ItemSku}' not found");

            if (!item.IsActive)
                return Result.Failure<PickResultDto>($"Item '{request.ItemSku}' is inactive");

            // Validate location exists and is pickable
            var location = await _unitOfWork.Locations.GetByCodeAsync(request.FromLocationCode, cancellationToken);
            if (location == null)
                return Result.Failure<PickResultDto>($"Location '{request.FromLocationCode}' not found");

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PickingExecute,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return Result.Failure<PickResultDto>(authorization.Error);
            }

            if (!location.IsPickable)
                return Result.Failure<PickResultDto>($"Location '{request.FromLocationCode}' is not pickable");

            if (!location.IsActive)
                return Result.Failure<PickResultDto>($"Location '{request.FromLocationCode}' is inactive");

            // Validate stock availability
            var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                item.Id, location.Id, null, request.SerialNumber, cancellationToken);

            if (stock == null)
                return Result.Failure<PickResultDto>(
                    $"No stock found for item '{request.ItemSku}' in location '{request.FromLocationCode}'");

            var requestedQuantity = new Quantity(request.Quantity);
            if (stock.GetAvailableQuantity() < requestedQuantity)
                return Result.Failure<PickResultDto>(
                    $"Insufficient stock. Available: {stock.GetAvailableQuantity()}, Requested: {requestedQuantity}");

            // Create the pick movement
            var movement = await _stockMovementService.PickAsync(
                item.Id, location.Id, requestedQuantity, userId,
                stock.LotId, request.SerialNumber, request.OrderNumber, request.Notes, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Item {ItemSku} picked: {Quantity} from {LocationCode} by {UserId} for order {OrderNumber}",
                request.ItemSku, request.Quantity, request.FromLocationCode, userId, request.OrderNumber);

            return Result.Success(new PickResultDto(
                movement.Id,
                request.ItemSku,
                request.FromLocationCode,
                request.Quantity,
                request.OrderNumber,
                movement.Timestamp
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error picking item {ItemSku}", request.ItemSku);
            return Result.Failure<PickResultDto>($"Error picking item: {ex.Message}");
        }
    }
}
