// Wms.Application/UseCases/Receiving/ReceiveItemUseCase.cs

using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.DTOs;
using Wms.Application.Idempotency;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Logging;
using Wms.Application.Lots;
using Wms.Application.Purchasing;
using Wms.Application.Receiving;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Application.UseCases.Receiving;

public interface IReceiveItemUseCase
{
    Task<Result<ReceiptResultDto>> ExecuteAsync(ReceiveItemDto request, string userId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);
}

public class ReceiveItemUseCase : IReceiveItemUseCase
{
    private readonly ILogger<ReceiveItemUseCase> _logger;
    private readonly IStockMovementService _stockMovementService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IItemQuantityConversionService? _quantityConversionService;
    private readonly ILotService? _lotService;
    private readonly IInventoryCommandIdempotencyService? _idempotencyService;
    private readonly IRequestContext? _requestContext;
    private readonly IPurchaseOrderService? _purchaseOrderService;
    private readonly IAdvanceShippingNoticeService? _advanceShippingNoticeService;
    private readonly IReceiptService? _receiptService;

    public ReceiveItemUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<ReceiveItemUseCase> logger,
        IWarehouseAccessService warehouseAccessService,
        IItemQuantityConversionService? quantityConversionService = null,
        ILotService? lotService = null,
        IInventoryCommandIdempotencyService? idempotencyService = null,
        IRequestContext? requestContext = null,
        IPurchaseOrderService? purchaseOrderService = null,
        IAdvanceShippingNoticeService? advanceShippingNoticeService = null,
        IReceiptService? receiptService = null)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
        _quantityConversionService = quantityConversionService;
        _lotService = lotService;
        _idempotencyService = idempotencyService;
        _requestContext = requestContext;
        _purchaseOrderService = purchaseOrderService;
        _advanceShippingNoticeService = advanceShippingNoticeService;
        _receiptService = receiptService;
    }

    public async Task<Result<ReceiptResultDto>> ExecuteAsync(ReceiveItemDto request, string userId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var lotTransactionStarted = false;
        try
        {
            var authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReceivingExecute,
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

            // Validate location exists and is receivable
            var location = await _unitOfWork.Locations.GetByCodeAsync(request.LocationCode, cancellationToken);
            if (location == null)
                return Result.Failure<ReceiptResultDto>(WmsErrors.NotFound(
                    "location.not_found",
                    $"Location '{request.LocationCode}' was not found."));

            authorization = await _warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReceivingExecute,
                location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReceiptResultDto>();
            }

            if (!location.IsReceivable)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "location.not_receivable",
                    $"Location '{request.LocationCode}' is not receivable."));

            if (!location.IsActive)
                return Result.Failure<ReceiptResultDto>(WmsErrors.BusinessRule(
                    "location.inactive",
                    $"Location '{request.LocationCode}' is inactive."));

            // Handle lot creation if required
            int? lotId = null;
            if (!item.RequiresLot && !string.IsNullOrWhiteSpace(request.LotNumber))
            {
                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "receiving.lot_not_allowed",
                    $"Item '{request.ItemSku}' is not lot controlled and must not receive a lot number."));
            }

            if (item.RequiresSerial && string.IsNullOrWhiteSpace(request.SerialNumber))
            {
                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "receiving.serial_required",
                    $"Item '{request.ItemSku}' requires a serial number."));
            }

            var identityTransactionRequired =
                (item.RequiresLot && !string.IsNullOrWhiteSpace(request.LotNumber)) ||
                (item.RequiresSerial && !string.IsNullOrWhiteSpace(request.SerialNumber));
            if (identityTransactionRequired)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                lotTransactionStarted = true;
            }

            if (item.RequiresLot && !string.IsNullOrWhiteSpace(request.LotNumber))
            {
                if (_lotService is null)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                    return Result.Failure<ReceiptResultDto>(WmsErrors.Dependency(
                        "lot.service_unavailable",
                        "Lot-controlled receiving is not available.",
                        isRetryable: false));
                }

                var lotResult = await _lotService.ResolveForReceiptAsync(
                    item,
                    request.LotNumber,
                    new LotDetailsRequest(
                        request.ExpiryDate,
                        request.ManufacturedDate),
                    userId,
                    cancellationToken);
                if (lotResult.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                    return lotResult.ToFailure<ReceiptResultDto>();
                }

                lotId = lotResult.Value.Id;
            }
            else if (item.RequiresLot)
            {
                if (lotTransactionStarted)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                }

                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "receiving.lot_required",
                    $"Item '{request.ItemSku}' requires a lot number."));
            }

            // Validate serial number requirement
            if (item.RequiresSerial && string.IsNullOrWhiteSpace(request.SerialNumber))
                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "receiving.serial_required",
                    $"Item '{request.ItemSku}' requires a serial number."));

            // Create the receipt movement
            var quantityResult = await ConvertToBaseAsync(
                item,
                request.Quantity,
                request.UnitOfMeasure ?? item.PurchaseUnit,
                request.PackagingCode,
                cancellationToken);
            if (quantityResult.IsFailure)
            {
                if (lotTransactionStarted)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                }

                return quantityResult.ToFailure<ReceiptResultDto>();
            }

            var quantity = quantityResult.Value;
            PurchaseOrderReceiptPlan? purchaseOrderReceiptPlan = null;
            AdvanceShippingNoticeReceiptPlan? advanceShippingNoticeReceiptPlan = null;
            var hasPurchaseOrderReference = request.PurchaseOrderId.HasValue || request.PurchaseOrderLineId.HasValue;
            var hasAdvanceShippingNoticeReference =
                request.AdvanceShippingNoticeId.HasValue || request.AdvanceShippingNoticeLineId.HasValue;
            if (hasPurchaseOrderReference && hasAdvanceShippingNoticeReference)
            {
                if (lotTransactionStarted)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                }

                return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                    "receiving.reference_ambiguous",
                    "Receive against either a purchase-order line or an ASN line, not both. The purchase order is derived from an ASN line."));
            }

            if (hasAdvanceShippingNoticeReference)
            {
                if (!request.AdvanceShippingNoticeId.HasValue || !request.AdvanceShippingNoticeLineId.HasValue)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                        "asn.reference_incomplete",
                        "Both ASN ID and ASN line ID are required when receiving against an advance shipping notice."));
                }

                if (_advanceShippingNoticeService is null)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return Result.Failure<ReceiptResultDto>(WmsErrors.Dependency(
                        "asn.service_unavailable",
                        "ASN receiving is not available.",
                        isRetryable: false));
                }

                var receiptPlanResult = await _advanceShippingNoticeService.ValidateReceiptAsync(
                    request.AdvanceShippingNoticeId.Value,
                    request.AdvanceShippingNoticeLineId.Value,
                    item.Id,
                    location.WarehouseId,
                    quantity.Value,
                    request.LotNumber,
                    request.ExpiryDate,
                    request.SerialNumber,
                    request.LicensePlateId,
                    cancellationToken);
                if (receiptPlanResult.IsFailure)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return receiptPlanResult.ToFailure<ReceiptResultDto>();
                }

                advanceShippingNoticeReceiptPlan = receiptPlanResult.Value;
            }

            if (hasPurchaseOrderReference)
            {
                if (!request.PurchaseOrderId.HasValue || !request.PurchaseOrderLineId.HasValue)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return Result.Failure<ReceiptResultDto>(WmsErrors.Validation(
                        "purchase_order.reference_incomplete",
                        "Both purchase order ID and purchase order line ID are required when receiving against a purchase order."));
                }

                if (_purchaseOrderService is null)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return Result.Failure<ReceiptResultDto>(WmsErrors.Dependency(
                        "purchase_order.service_unavailable",
                        "Purchase-order receiving is not available.",
                        isRetryable: false));
                }

                var receiptPlanResult = await _purchaseOrderService.ValidateReceiptAsync(
                    request.PurchaseOrderId.Value,
                    request.PurchaseOrderLineId.Value,
                    item.Id,
                    location.WarehouseId,
                    quantity.Value,
                    cancellationToken);
                if (receiptPlanResult.IsFailure)
                {
                    if (lotTransactionStarted)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        lotTransactionStarted = false;
                    }

                    return receiptPlanResult.ToFailure<ReceiptResultDto>();
                }

                purchaseOrderReceiptPlan = receiptPlanResult.Value;
            }

            var receiptService = _receiptService;
            if (receiptService is null)
            {
                if (lotTransactionStarted)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                }

                return Result.Failure<ReceiptResultDto>(WmsErrors.Dependency(
                    "receipt.service_unavailable",
                    "Receiving requires a persisted receipt service.",
                    isRetryable: false));
            }

            if (!lotTransactionStarted)
            {
                await _unitOfWork.BeginTransactionAsync(cancellationToken);
                lotTransactionStarted = true;
            }

            InventoryCommandIdempotencyLease? idempotencyLease = null;
            var effectiveIdempotencyKey = idempotencyKey ?? _requestContext?.IdempotencyKey;
            var requestContext = _requestContext;
            if (_idempotencyService is not null &&
                !string.IsNullOrWhiteSpace(effectiveIdempotencyKey))
            {
                var idempotencyResult = await _idempotencyService.BeginAsync(
                    new InventoryCommandIdempotencyRequest(
                        "inventory.receipt",
                        effectiveIdempotencyKey!,
                        InventoryCommandIdempotencyScope.For(
                            requestContext,
                            userId,
                            location.WarehouseId),
                        InventoryCommandRequestHasher.Compute("inventory.receipt", request),
                        requestContext?.CorrelationId ?? WmsExecutionIdentifiers.NewCorrelationId(),
                        userId,
                        location.WarehouseId),
                    cancellationToken);
                if (idempotencyResult.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                    return idempotencyResult.ToFailure<ReceiptResultDto>();
                }

                if (idempotencyResult.Value.IsReplay)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    lotTransactionStarted = false;
                    return InventoryCommandJson.DeserializeResult<ReceiptResultDto>(
                        idempotencyResult.Value);
                }

                idempotencyLease = idempotencyResult.Value.Lease;
            }

            var receiptResult = await receiptService.OpenForReceivingAsync(
                new ReceiptReceivingInput(
                    location.WarehouseId,
                    location.Id,
                    item.Id,
                    item.Sku,
                    item.Name,
                    quantity,
                    request.LotNumber,
                    request.ExpiryDate,
                    request.SerialNumber,
                    request.LicensePlateId,
                    request.PurchaseOrderId,
                    request.PurchaseOrderLineId,
                    request.AdvanceShippingNoticeId,
                    request.AdvanceShippingNoticeLineId,
                    purchaseOrderReceiptPlan,
                    advanceShippingNoticeReceiptPlan,
                    request.ReferenceNumber,
                    request.Notes,
                    SessionReference: request.SessionReference,
                    OwnerKind: request.OwnerKind,
                    InventoryOwnerId: request.InventoryOwnerId,
                    OwnerCodeSnapshot: request.OwnerCodeSnapshot),
                userId,
                cancellationToken);
            if (receiptResult.IsFailure)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                lotTransactionStarted = false;
                return receiptResult.ToFailure<ReceiptResultDto>();
            }

            var receiptPlan = receiptResult.Value
                ?? throw new InvalidOperationException("The receipt service returned no receiving plan.");

            var movement = await _stockMovementService.ReceiveAsync(
                item.Id, location.Id, quantity, userId, lotId,
                request.SerialNumber, receiptPlan.DocumentNumber, request.Notes,
                cancellationToken: cancellationToken,
                licensePlateId: request.LicensePlateId,
                receiptId: receiptPlan.ReceiptId,
                receiptLineId: receiptPlan.ReceiptLineId,
                ownerKind: request.OwnerKind,
                inventoryOwnerId: request.InventoryOwnerId,
                ownerCodeSnapshot: request.OwnerCodeSnapshot);

            var finalizeResult = await receiptService.FinalizeReceivingAsync(
                receiptPlan,
                movement,
                userId,
                cancellationToken);
            if (finalizeResult.IsFailure)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                lotTransactionStarted = false;
                return finalizeResult.ToFailure<ReceiptResultDto>();
            }

            var result = new ReceiptResultDto(
                movement.Id,
                request.ItemSku,
                request.LocationCode,
                request.Quantity,
                request.LotNumber,
                movement.Timestamp,
                receiptPlan.ReceiptId,
                receiptPlan.DocumentNumber);
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
            if (lotTransactionStarted)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                lotTransactionStarted = false;
            }

            _logger.LogInformation("Item {ItemSku} received: {Quantity} to {LocationCode} by {UserId}",
                request.ItemSku, request.Quantity, request.LocationCode, userId);

            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            if (lotTransactionStarted)
            {
                await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (lotTransactionStarted)
            {
                try
                {
                    await _unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogError(rollbackException, "Lot receipt transaction rollback failed");
                }
            }

            var failure = WmsErrors.FromException(ex,
                "receiving.failed",
                "Error receiving item. Please try again.");
            _logger.LogError(
                WmsLogEvents.InventoryOperationFailed,
                ex,
                "Inventory receipt failed for item {ItemSku} with error code {ErrorCode}",
                request.ItemSku,
                failure.Code);
            return Result.Failure<ReceiptResultDto>(failure);
        }
    }

    private async Task<Result<Quantity>> ConvertToBaseAsync(
        Item item,
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
