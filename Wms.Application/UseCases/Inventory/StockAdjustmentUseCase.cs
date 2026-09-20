// Wms.Application/UseCases/Inventory/StockAdjustmentUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Logging;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Inventory;

public record StockAdjustmentDto(
    string ItemSku,
    string LocationCode,
    decimal NewQuantity,
    string Reason,
    string? LotNumber = null,
    string? SerialNumber = null
);

public interface IStockAdjustmentUseCase
{
    Task<Result<ReceiptResultDto>> ExecuteAsync(StockAdjustmentDto request, string userId,
        CancellationToken cancellationToken = default);
}

public class StockAdjustmentUseCase : IStockAdjustmentUseCase
{
    private readonly ILogger<StockAdjustmentUseCase> _logger;
    private readonly IStockMovementService _stockMovementService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public StockAdjustmentUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<StockAdjustmentUseCase> logger,
        IWarehouseAccessService warehouseAccessService)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
    }

    public async Task<Result<ReceiptResultDto>> ExecuteAsync(StockAdjustmentDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
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

            // Validate location exists
            var location = await _unitOfWork.Locations.GetByCodeAsync(request.LocationCode, cancellationToken);
            if (location == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location '{request.LocationCode}' was not found."));

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReceiptResultDto>();
            }

            if (!location.IsActive)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "location.inactive",
                    $"Location '{request.LocationCode}' is inactive."));

            // Validate reason is provided
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "inventory.adjustment_reason_required",
                    "Adjustment reason is required."));

            // Create the adjustment
            var newQuantity = new Quantity(request.NewQuantity);
            var movement = await _stockMovementService.AdjustAsync(
                item.Id, location.Id, newQuantity, userId, request.Reason,
                null, request.SerialNumber, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Stock adjusted: Item {ItemSku} in {LocationCode} to {NewQuantity} by {UserId}. Reason: {Reason}",
                request.ItemSku, request.LocationCode, request.NewQuantity, userId, request.Reason);

            return Result.Success(new ReceiptResultDto(
                movement.Id,
                request.ItemSku,
                request.LocationCode,
                request.NewQuantity,
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
                "inventory.adjustment_failed",
                "Error adjusting stock. Please try again.");
            _logger.LogError(
                WmsLogEvents.InventoryOperationFailed,
                ex,
                "Inventory adjustment failed for item {ItemSku} with error code {ErrorCode}",
                request.ItemSku,
                failure.Code);
            return Result.Failure<ReceiptResultDto>(failure);
        }
    }
}
