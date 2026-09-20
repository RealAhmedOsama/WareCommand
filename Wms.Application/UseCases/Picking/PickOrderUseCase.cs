// Wms.Application/UseCases/Picking/PickOrderUseCase.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.InventoryStatuses;
using Wms.Application.Logging;
using Wms.Application.Lots;
using Wms.Application.Time;
using Wms.Domain.Entities;
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
    string? UnitOfMeasure = null,
    string? PackagingCode = null
);

public record PickResultDto(
    int MovementId,
    string ItemSku,
    string FromLocationCode,
    decimal Quantity,
    string? OrderNumber,
    DateTime Timestamp,
    string? LotNumber = null
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
    private readonly ILotService? _lotService;
    private readonly IClock? _clock;
    private readonly IInventoryStatusService? _inventoryStatusService;

    public PickOrderUseCase(
        IUnitOfWork unitOfWork,
        IStockMovementService stockMovementService,
        ILogger<PickOrderUseCase> logger,
        IWarehouseAccessService warehouseAccessService,
        IItemQuantityConversionService? quantityConversionService = null,
        ILotService? lotService = null,
        IClock? clock = null,
        IInventoryStatusService? inventoryStatusService = null)
    {
        _unitOfWork = unitOfWork;
        _stockMovementService = stockMovementService;
        _logger = logger;
        _warehouseAccessService = warehouseAccessService;
        _quantityConversionService = quantityConversionService;
        _lotService = lotService;
        _clock = clock;
        _inventoryStatusService = inventoryStatusService;
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

            var quantityResult = await ConvertToBaseAsync(
                item,
                request.Quantity,
                request.UnitOfMeasure ?? item.SalesUnit,
                request.PackagingCode,
                cancellationToken);
            if (quantityResult.IsFailure)
            {
                return quantityResult.ToFailure<PickResultDto>();
            }

            var requestedQuantity = quantityResult.Value;

            int? lotId = null;
            string? resolvedLotNumber = request.LotNumber;
            Stock? stock;
            if (_lotService is not null &&
                (!string.IsNullOrWhiteSpace(request.LotNumber) || !item.RequiresLot || !item.UseFefo))
            {
                var lotResult = await _lotService.ResolveForMovementAsync(
                    item,
                    request.LotNumber,
                    requireAllocationEligibility: true,
                    GetBusinessDate(location),
                    cancellationToken);
                if (lotResult.IsFailure)
                {
                    return lotResult.ToFailure<PickResultDto>();
                }

                lotId = lotResult.Value?.Id;
                resolvedLotNumber = lotResult.Value?.Number ?? request.LotNumber;
                stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                    item.Id,
                    location.Id,
                    lotId,
                    request.SerialNumber,
                    cancellationToken: cancellationToken);
            }
            else if (_lotService is not null && item.RequiresLot && item.UseFefo)
            {
                var candidates = await _unitOfWork.Stock.GetByItemAndLocationCandidatesAsync(
                    item.Id,
                    location.Id,
                    request.SerialNumber,
                    cancellationToken);
                var businessDate = GetBusinessDate(location);
                stock = candidates.FirstOrDefault(candidate =>
                    (candidate.InventoryStatus is null ||
                     (candidate.InventoryStatus.IsActive &&
                      candidate.InventoryStatus.IsAllocatable &&
                      candidate.InventoryStatus.IsPickable)) &&
                    candidate.Lot is not null &&
                    candidate.Lot.IsAllocationEligible(businessDate) &&
                    candidate.GetAvailableQuantity() >= requestedQuantity);
                if (stock is not null)
                {
                    lotId = stock.LotId;
                    resolvedLotNumber = stock.Lot?.Number;
                }
            }
            else
            {
                if (item.RequiresLot)
                {
                    return Result.Failure<PickResultDto>(WmsErrors.Dependency(
                        "lot.service_unavailable",
                        "Lot-controlled picking is not available.",
                        isRetryable: false));
                }

                stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                    item.Id,
                    location.Id,
                    null,
                    request.SerialNumber,
                    cancellationToken: cancellationToken);
            }

            if (stock == null)
            {
                return Result.Failure<PickResultDto>(WmsErrors.NotFound(
                    "stock.not_found",
                    $"No eligible stock found for item '{request.ItemSku}' in location '{request.FromLocationCode}' and lot '{request.LotNumber ?? "FEFO"}'."));
            }

            if (_inventoryStatusService is not null)
            {
                var statusValidation = await _inventoryStatusService.ValidateOperationAsync(
                    stock,
                    InventoryStatusOperation.Pick,
                    cancellationToken);
                if (statusValidation.IsFailure)
                {
                    return statusValidation.ToFailure<PickResultDto>();
                }
            }
            else if (stock.InventoryStatus is not null &&
                     (!stock.InventoryStatus.IsActive || !stock.InventoryStatus.IsPickable))
            {
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "inventory_status.pick_blocked",
                    $"Inventory status '{stock.InventoryStatus.Code}' does not permit picking."));
            }

            if (stock.GetAvailableQuantity() < requestedQuantity)
                return Result.Failure<PickResultDto>(WmsErrors.BusinessRule(
                    "stock.insufficient",
                    $"Insufficient stock. Available: {stock.GetAvailableQuantity()}, Requested: {requestedQuantity}."));

            // Create the pick movement
            var movement = await _stockMovementService.PickAsync(
                item.Id, location.Id, requestedQuantity, userId,
                lotId ?? stock.LotId, request.SerialNumber, request.OrderNumber, request.Notes, cancellationToken);

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
                movement.Timestamp,
                resolvedLotNumber
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
