// Wms.Application/UseCases/Receiving/PutawayUseCase.cs

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
    private readonly IItemQuantityConversionService? _quantityConversionService;
    private readonly ILotService? _lotService;
    private readonly IClock? _clock;
    private readonly IInventoryCommandIdempotencyService? _idempotencyService;
    private readonly IRequestContext? _requestContext;

    public PutawayUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<PutawayUseCase> logger,
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

    public async Task<Result<ReceiptResultDto>> ExecuteAsync(PutawayDto request, string userId,
        CancellationToken cancellationToken = default)
    {
        var transactionStarted = false;
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

            var lotId = default(int?);
            string? resolvedLotNumber = request.LotNumber;
            if (_lotService is not null)
            {
                var lotResult = await _lotService.ResolveForMovementAsync(
                    item,
                    request.LotNumber,
                    requireAllocationEligibility: false,
                    GetBusinessDate(fromLocation),
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
                    "Lot-controlled putaway is not available.",
                    isRetryable: false));
            }

            // Validate stock exists in from location using the requested lot identity.
            var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                item.Id,
                fromLocation.Id,
                lotId,
                request.SerialNumber,
                cancellationToken: cancellationToken,
                licensePlateId: request.LicensePlateId);

            if (stock == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "stock.not_found",
                    $"No stock found for item '{request.ItemSku}' in location '{request.FromLocationCode}'."));

            var quantityResult = await ConvertToBaseAsync(
                item,
                request.Quantity,
                request.UnitOfMeasure ?? item.UnitOfMeasure,
                request.PackagingCode,
                cancellationToken);
            if (quantityResult.IsFailure)
            {
                return quantityResult.ToFailure<ReceiptResultDto>();
            }

            var requestedQuantity = quantityResult.Value;
            if (stock.GetAvailableQuantity() < requestedQuantity)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "stock.insufficient",
                    $"Insufficient stock. Available: {stock.GetAvailableQuantity()}, Requested: {requestedQuantity}."));

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

            InventoryCommandIdempotencyLease? idempotencyLease = null;
            if (_idempotencyService is not null &&
                !string.IsNullOrWhiteSpace(_requestContext?.IdempotencyKey))
            {
                var idempotencyResult = await _idempotencyService.BeginAsync(
                    new InventoryCommandIdempotencyRequest(
                        "inventory.putaway",
                        _requestContext.IdempotencyKey!,
                        InventoryCommandIdempotencyScope.For(
                            _requestContext,
                            userId,
                            fromLocation.WarehouseId),
                        InventoryCommandRequestHasher.Compute("inventory.putaway", request),
                        _requestContext.CorrelationId,
                        userId,
                        fromLocation.WarehouseId),
                    cancellationToken);
                if (idempotencyResult.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return idempotencyResult.ToFailure<ReceiptResultDto>();
                }

                if (idempotencyResult.Value.IsReplay)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return InventoryCommandJson.DeserializeResult<ReceiptResultDto>(
                        idempotencyResult.Value);
                }

                idempotencyLease = idempotencyResult.Value.Lease;
            }

            // Create the putaway movement
            var movement = await _stockMovementService.PutawayAsync(
                item.Id, fromLocation.Id, toLocation.Id, requestedQuantity, userId,
                lotId ?? stock.LotId, request.SerialNumber, notes: request.Notes,
                cancellationToken: cancellationToken,
                licensePlateId: request.LicensePlateId);

            var result = new ReceiptResultDto(
                movement.Id,
                request.ItemSku,
                request.ToLocationCode,
                request.Quantity,
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

            _logger.LogInformation("Item {ItemSku} putaway: {Quantity} from {FromLocation} to {ToLocation} by {UserId}",
                request.ItemSku, request.Quantity, request.FromLocationCode, request.ToLocationCode, userId);

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
                    _logger.LogError(rollbackException, "Putaway transaction rollback failed");
                }
            }

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

    private DateOnly GetBusinessDate(Wms.Domain.Entities.Location location) =>
        WmsBusinessTime.GetBusinessDate(
            _clock?.UtcNow ?? DateTimeOffset.UtcNow,
            location.Warehouse?.TimeZone ?? WmsTimeZoneCatalog.Utc);
}
