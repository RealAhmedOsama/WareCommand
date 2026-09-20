// Wms.Application/UseCases/Receiving/PutawayUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Logging;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Receiving;

public interface IPutawayUseCase
{
    Task<Result<ReceiptResultDto>> ExecuteAsync(PutawayDto request, string userId,
        CancellationToken cancellationToken = default);
}

public class PutawayUseCase : IPutawayUseCase
{
    private readonly ILogger<PutawayUseCase> _logger;
    private readonly IStockMovementService _stockMovementService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public PutawayUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<PutawayUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<ReceiptResultDto>> ExecuteAsync(PutawayDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PutawayExecute,
                cancellationToken: cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReceiptResultDto>();
            }

            // Validate item exists
            var item = await _unitOfWork.Items.GetBySkuAsync(request.ItemSku, cancellationToken);
            if (item == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "item.not_found",
                    $"Item with SKU '{request.ItemSku}' was not found."));

            if (!item.IsActive)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "item.inactive",
                    $"Item '{request.ItemSku}' is inactive."));

            // Validate from location
            var fromLocation = await _unitOfWork.Locations.GetByCodeAsync(request.FromLocationCode, cancellationToken);
            if (fromLocation == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "location.source_not_found",
                    $"Source location '{request.FromLocationCode}' was not found."));

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PutawayExecute,
                fromLocation.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReceiptResultDto>();
            }

            // Validate to location
            var toLocation = await _unitOfWork.Locations.GetByCodeAsync(request.ToLocationCode, cancellationToken);
            if (toLocation == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "location.destination_not_found",
                    $"Destination location '{request.ToLocationCode}' was not found."));

            if (toLocation.WarehouseId != fromLocation.WarehouseId)
            {
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "putaway.warehouse_mismatch",
                    "Putaway source and destination must belong to the same warehouse."));
            }

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.PutawayExecute,
                toLocation.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReceiptResultDto>();
            }

            if (!toLocation.IsReceivable)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "location.not_receivable",
                    $"Location '{request.ToLocationCode}' is not receivable."));

            if (!toLocation.IsActive)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "location.inactive",
                    $"Location '{request.ToLocationCode}' is inactive."));

            // Validate stock exists in from location
            var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                item.Id, fromLocation.Id, null, request.SerialNumber, cancellationToken);

            if (stock == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "stock.not_found",
                    $"No stock found for item '{request.ItemSku}' in location '{request.FromLocationCode}'."));

            var requestedQuantity = new Quantity(request.Quantity);
            if (stock.GetAvailableQuantity() < requestedQuantity)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "stock.insufficient",
                    $"Insufficient stock. Available: {stock.GetAvailableQuantity()}, Requested: {requestedQuantity}."));

            // Create the putaway movement
            var movement = await _stockMovementService.PutawayAsync(
                item.Id, fromLocation.Id, toLocation.Id, requestedQuantity, userId,
                stock.LotId, request.SerialNumber, notes: request.Notes, cancellationToken: cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Item {ItemSku} putaway: {Quantity} from {FromLocation} to {ToLocation} by {UserId}",
                request.ItemSku, request.Quantity, request.FromLocationCode, request.ToLocationCode, userId);

            return Result.Success(new ReceiptResultDto(
                movement.Id,
                request.ItemSku,
                request.ToLocationCode,
                request.Quantity,
                request.LotNumber,
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
                "putaway.failed",
                "Error during putaway. Please try again.");
            _logger.LogError(
                WmsLogEvents.InventoryOperationFailed,
                ex,
                "Inventory putaway failed for item {ItemSku} with error code {ErrorCode}",
                request.ItemSku,
                failure.Code);
            return Result.Failure<ReceiptResultDto>(failure);
        }
    }
}
