// Wms.Application/UseCases/Picking/PickOrderUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Logging;
using Wms.Application.Units;
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
    string? Notes = null,
    string? UnitOfMeasure = null
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
    private readonly IItemQuantityConversionService? _quantityConversionService;

    public PickOrderUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<PickOrderUseCase> logger,
        IWarehouseAccessService warehouseAccessService,
        IItemQuantityConversionService? quantityConversionService = null)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
        _quantityConversionService = quantityConversionService;
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
                return authorization.ToFailure<PickResultDto>();
            }

            // Validate item exists
            var item = await _unitOfWork.Items.GetBySkuAsync(request.ItemSku, cancellationToken);
            if (item == null)
                return Result.Failure<PickResultDto>(WmsErrors.NotFound(
                    "item.not_found",
                    $"Item with SKU '{request.ItemSku}' was not found."));

            if (!item.IsActive)
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "item.inactive",
                    $"Item '{request.ItemSku}' is inactive."));

            // Validate location exists and is pickable
            var location = await _unitOfWork.Locations.GetByCodeAsync(request.FromLocationCode, cancellationToken);
            if (location == null)
                return Result.Failure<PickResultDto>(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location '{request.FromLocationCode}' was not found."));

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PickingExecute,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<PickResultDto>();
            }

            if (!location.IsPickable)
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "location.not_pickable",
                    $"Location '{request.FromLocationCode}' is not pickable."));

            if (!location.IsActive)
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "location.inactive",
                    $"Location '{request.FromLocationCode}' is inactive."));

            // Validate stock availability
            var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                item.Id, location.Id, null, request.SerialNumber, cancellationToken);

            if (stock == null)
                return Result.Failure<PickResultDto>(WmsErrors.NotFound(
                    "stock.not_found",
                    $"No stock found for item '{request.ItemSku}' in location '{request.FromLocationCode}'."));

            var quantityResult = await ConvertToBaseAsync(
                item,
                request.Quantity,
                request.UnitOfMeasure ?? item.SalesUnit,
                cancellationToken);
            if (quantityResult.IsFailure)
            {
                return quantityResult.ToFailure<PickResultDto>();
            }

            var requestedQuantity = quantityResult.Value;
            if (stock.GetAvailableQuantity() < requestedQuantity)
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "stock.insufficient",
                    $"Insufficient stock. Available: {stock.GetAvailableQuantity()}, Requested: {requestedQuantity}."));

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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = WmsErrors.FromException(ex,
                "picking.failed",
                "Error picking item. Please try again.");
            _logger.LogError(
                WmsLogEvents.InventoryOperationFailed,
                ex,
                "Inventory pick failed for item {ItemSku} with error code {ErrorCode}",
                request.ItemSku,
                failure.Code);
            return Result.Failure<PickResultDto>(failure);
        }
    }

    private async Task<Result<Quantity>> ConvertToBaseAsync(
        Wms.Domain.Entities.Item item,
        decimal quantity,
        string unitOfMeasure,
        CancellationToken cancellationToken)
    {
        if (_quantityConversionService is null)
        {
            return Result.Success(new Quantity(quantity));
        }

        var result = await _quantityConversionService.ConvertToBaseAsync(
            item.Id,
            quantity,
            unitOfMeasure,
            cancellationToken: cancellationToken);
        return result.IsFailure
            ? result.ToFailure<Quantity>()
            : Result.Success(new Quantity(result.Value.BaseQuantity, result.Value.ToSnapshot()));
    }
}
