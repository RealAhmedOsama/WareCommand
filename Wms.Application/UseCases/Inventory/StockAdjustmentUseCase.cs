// Wms.Application/UseCases/Inventory/StockAdjustmentUseCase.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.DTOs;
using Wms.Application.Idempotency;
using Wms.Application.Identity;
using Wms.Application.Logging;
using Wms.Application.Lots;
using Wms.Application.Time;
using Wms.Application.Units;
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
    string? SerialNumber = null,
    string? UnitOfMeasure = null,
    string? PackagingCode = null
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
    private readonly IItemQuantityConversionService? _quantityConversionService;
    private readonly ILotService? _lotService;
    private readonly IClock? _clock;
    private readonly IInventoryCommandIdempotencyService? _idempotencyService;
    private readonly IRequestContext? _requestContext;

    public StockAdjustmentUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<StockAdjustmentUseCase> logger,
        IWarehouseAccessService warehouseAccessService,
        IItemQuantityConversionService? quantityConversionService = null,
        ILotService? lotService = null,
        IClock? clock = null,
        IInventoryCommandIdempotencyService? idempotencyService = null,
        IRequestContext? requestContext = null)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
        _quantityConversionService = quantityConversionService;
        _lotService = lotService;
        _clock = clock;
        _idempotencyService = idempotencyService;
        _requestContext = requestContext;
    }

    public async Task<Result<ReceiptResultDto>> ExecuteAsync(StockAdjustmentDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        var transactionStarted = false;
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

            int? lotId = null;
            string? resolvedLotNumber = request.LotNumber;
            if (_lotService is not null)
            {
                var lotResult = await _lotService.ResolveForMovementAsync(
                    item,
                    request.LotNumber,
                    requireAllocationEligibility: false,
                    WmsBusinessTime.GetBusinessDate(
                        _clock?.UtcNow ?? DateTimeOffset.UtcNow,
                        location.Warehouse?.TimeZone ?? WmsTimeZoneCatalog.Utc),
                    cancellationToken);
                if (lotResult.IsFailure)
                {
                    return lotResult.ToFailure<ReceiptResultDto>();
                }

                lotId = lotResult.Value?.Id;
                resolvedLotNumber = lotResult.Value?.Number ?? request.LotNumber;
            }
            else if (item.RequiresLot)
            {
                return Result.Failure<ReceiptResultDto>(WmsErrors.Dependency(
                    "lot.service_unavailable",
                    "Lot-controlled adjustment is not available.",
                    isRetryable: false));
            }

            // Create the adjustment with the resolved lot identity.
            var quantityResult = await ConvertToBaseAsync(
                item,
                request.NewQuantity,
                request.UnitOfMeasure ?? item.UnitOfMeasure,
                request.PackagingCode,
                cancellationToken);
            if (quantityResult.IsFailure)
            {
                return quantityResult.ToFailure<ReceiptResultDto>();
            }

            var newQuantity = quantityResult.Value;
            InventoryCommandIdempotencyLease? idempotencyLease = null;
            if (_idempotencyService is not null &&
                !string.IsNullOrWhiteSpace(_requestContext?.IdempotencyKey))
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                transactionStarted = true;
                var idempotencyResult = await _idempotencyService.BeginAsync(
                    new InventoryCommandIdempotencyRequest(
                        "inventory.adjustment",
                        _requestContext.IdempotencyKey!,
                        InventoryCommandIdempotencyScope.For(
                            _requestContext,
                            userId,
                            location.WarehouseId),
                        InventoryCommandRequestHasher.Compute("inventory.adjustment", request),
                        _requestContext.CorrelationId,
                        userId,
                        location.WarehouseId),
                    cancellationToken);
                if (idempotencyResult.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    transactionStarted = false;
                    return idempotencyResult.ToFailure<ReceiptResultDto>();
                }

                if (idempotencyResult.Value.IsReplay)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    transactionStarted = false;
                    return InventoryCommandJson.DeserializeResult<ReceiptResultDto>(
                        idempotencyResult.Value);
                }

                idempotencyLease = idempotencyResult.Value.Lease;
            }

            var movement = await _stockMovementService.AdjustAsync(
                item.Id, location.Id, newQuantity, userId, request.Reason,
                lotId, request.SerialNumber, cancellationToken);

            var result = new ReceiptResultDto(
                movement.Id,
                request.ItemSku,
                request.LocationCode,
                request.NewQuantity,
                resolvedLotNumber,
                movement.Timestamp);
            if (idempotencyLease is not null)
            {
                await _idempotencyService!.CompleteAsync(
                    idempotencyLease,
                    new InventoryCommandIdempotencyCompletion(
                        nameof(ReceiptResultDto),
                        InventoryCommandJson.Serialize(result),
                        movement.Id.ToString(CultureInfo.InvariantCulture)),
                    cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (transactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                transactionStarted = false;
            }

            _logger.LogInformation(
                "Stock adjusted: Item {ItemSku} in {LocationCode} to {NewQuantity} by {UserId}. Reason: {Reason}",
                request.ItemSku, request.LocationCode, request.NewQuantity, userId, request.Reason);

            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (transactionStarted)
            {
                try
                {
                    await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException, "Adjustment transaction rollback failed");
                }
            }

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

    private async Task<Result<Quantity>> ConvertToBaseAsync(
        Wms.Domain.Entities.Item item,
        decimal quantity,
        string unitOfMeasure,
        string? packagingCode,
        CancellationToken cancellationToken)
    {
        if (_quantityConversionService is null)
        {
            return Result.Success(new Quantity(quantity));
        }

        var result = !string.IsNullOrWhiteSpace(packagingCode)
            ? await _quantityConversionService.ConvertPackagingToBaseAsync(
                item.Id,
                quantity,
                packagingCode,
                cancellationToken: cancellationToken)
            : await _quantityConversionService.ConvertToBaseAsync(
                item.Id,
                quantity,
                unitOfMeasure,
                cancellationToken: cancellationToken);
        return result.IsFailure
            ? result.ToFailure<Quantity>()
            : Result.Success(new Quantity(result.Value.BaseQuantity, result.Value.ToSnapshot()));
    }
}
