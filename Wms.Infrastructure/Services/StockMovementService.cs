// Wms.Infrastructure/Services/StockMovementService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.InventoryStatuses;
using Wms.Application.Integrations;
using Wms.Application.Quality;
using Wms.Application.Logging;
using Wms.Application.SerialNumbers;
using Wms.Application.Telemetry;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Logging;

namespace Wms.Infrastructure.Services;

public class StockMovementService : IStockMovementService
{
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly ILogger<StockMovementService> _logger;
    private readonly IWmsOperationContextAccessor _operationContextAccessor;
    private readonly IRequestContext _requestContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISerialNumberService? _serialNumberService;
    private readonly IInventoryStatusService? _inventoryStatusService;
    private readonly IInventoryLedgerService? _inventoryLedgerService;
    private readonly IQualityInspectionService? _qualityInspectionService;
    private readonly IIntegrationEventWriter? _integrationEventWriter;

    public StockMovementService(
        IUnitOfWork unitOfWork,
        ILogger<StockMovementService> logger,
        IAuditWriter auditWriter,
        IRequestContext requestContext,
        IWmsOperationContextAccessor operationContextAccessor,
        IClock clock,
        ISerialNumberService? serialNumberService = null,
        IInventoryStatusService? inventoryStatusService = null,
        IInventoryLedgerService? inventoryLedgerService = null,
        IQualityInspectionService? qualityInspectionService = null,
        IIntegrationEventWriter? integrationEventWriter = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _auditWriter = auditWriter;
        _requestContext = requestContext;
        _operationContextAccessor = operationContextAccessor;
        _clock = clock;
        _serialNumberService = serialNumberService;
        _inventoryStatusService = inventoryStatusService;
        _inventoryLedgerService = inventoryLedgerService;
        _qualityInspectionService = qualityInspectionService;
        _integrationEventWriter = integrationEventWriter;
    }

    public async Task<Movement> ReceiveAsync(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null, int? receiptId = null, int? receiptLineId = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null, string? ownerCodeSnapshot = null)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
        using var operationScope = WmsLogging.BeginOperation(
            _logger,
            _operationContextAccessor,
            _requestContext,
            "inventory.receipt",
            referenceNumber,
            userId,
            location?.WarehouseId);
        using var telemetryScope = WmsTelemetry.BeginInventoryOperation(
            "receipt",
            location?.WarehouseId);
        try
        {
            await EnsureActiveItemAsync(itemId, cancellationToken);
            EnsureReceivingLocation(location);
            await ValidateLicensePlateAsync(
                licensePlateId,
                location!.WarehouseId,
                locationId,
                cancellationToken);
            await ValidateLotAsync(itemId, lotId, requireAllocationEligibility: false, cancellationToken);
            var serial = await ResolveSerialForReceiptAsync(
                itemId,
                lotId,
                serialNumber,
                quantity,
                cancellationToken);
            var inboundStatusId = await ResolveInboundStatusIdAsync(
                location,
                itemId,
                cancellationToken);
            var existingStock = await FindStockAsync(
                itemId,
                locationId,
                lotId,
                serial?.Number ?? serialNumber,
                serial?.Id,
                inboundStatusId,
                licensePlateId,
                cancellationToken,
                ownerKind,
                inventoryOwnerId);
            await EnsureInboundCapacityAsync(
                location,
                itemId,
                lotId,
                quantity,
                cancellationToken,
                incomingLicensePlateId: licensePlateId);
            var quantityBefore = existingStock?.QuantityAvailable.Value ?? 0m;

            // Create receipt movement
            var movement = Movement.CreateReceipt(itemId, locationId, quantity, userId,
                lotId,
                serial?.Number ?? serialNumber,
                referenceNumber,
                notes,
                _clock.UtcNow.UtcDateTime,
                serial?.Id,
                inboundStatusId,
                toLicensePlateId: licensePlateId);
            movement.SetOwnership(ownerKind, inventoryOwnerId, ownerCodeSnapshot);

            if (receiptId.HasValue != receiptLineId.HasValue)
            {
                throw new InvalidOperationException(
                    "A receiving movement must receive both a receipt ID and receipt line ID.");
            }

            if (receiptId.HasValue)
            {
                movement.LinkReceipt(receiptId.Value, receiptLineId!.Value);
            }

            await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

            // Update or create stock record
            if (existingStock != null)
            {
                existingStock.AddQuantity(quantity);
                await _unitOfWork.Stock.UpdateAsync(existingStock, cancellationToken);
            }
            else
            {
                var newStock = new Stock(
                    itemId,
                    locationId,
                    quantity,
                    lotId,
                    serial?.Number ?? serialNumber,
                    serial?.Id,
                    inboundStatusId,
                    licensePlateId,
                    ownerKind,
                    inventoryOwnerId,
                    ownerCodeSnapshot);
                await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
            }

            if (serial is not null)
            {
                if (location is null)
                {
                    throw new InvalidOperationException("Receipt location was not found.");
                }

                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                serial.RecordReceipt(
                    location.WarehouseId,
                    location.Id,
                    lotId,
                    referenceNumber,
                    item.QualityInspectionRequired,
                    _clock.UtcNow.UtcDateTime,
                    licensePlateId);
                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }

            if (_inventoryLedgerService is not null)
            {
                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                await _inventoryLedgerService.RecordAsync(
                    new[]
                    {
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Receipt,
                            new InventoryBalanceKey(
                                location!.WarehouseId,
                                locationId,
                                itemId,
                                lotId,
                                serial?.Id,
                                serial?.Number ?? serialNumber,
                                licensePlateId,
                                inboundStatusId,
                                item.UnitOfMeasure,
                                ownerKind,
                                inventoryOwnerId,
                                ownerCodeSnapshot),
                            quantity.Value,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: referenceNumber,
                            Reason: notes,
                            OccurredAtUtc: movement.Timestamp,
                            CorrelationId: _requestContext.CorrelationId,
                            IdempotencyKey: $"{transactionGroupId}:1",
                            TransactionGroupId: transactionGroupId,
                            MovementId: movement.Id > 0 ? movement.Id : null)
                    },
                    cancellationToken);
            }

            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReceiptRecorded,
                    WmsAuditEntityTypes.Movement,
                    $"{itemId}:{locationId}",
                    location?.WarehouseId,
                    Before: new Dictionary<string, object?>
                    {
                        ["quantityAvailable"] = quantityBefore
                    },
                    After: new Dictionary<string, object?>
                    {
                        ["quantity"] = quantity.Value,
                        ["quantityAvailable"] = quantityBefore + quantity.Value,
                        ["referenceNumber"] = referenceNumber
                    },
                    ActorUserId: userId),
                cancellationToken);

            _logger.LogInformation(
                WmsLogEvents.InventoryReceiptCompleted,
                "Inventory receipt completed for item {ItemId}, quantity {Quantity}, location {LocationId}",
                itemId,
                quantity.Value,
                locationId);

            await EnqueueMovementEventAsync(
                WmsIntegrationEventTypes.InventoryMovementRecorded,
                movement,
                userId,
                location?.WarehouseId,
                cancellationToken);
            telemetryScope.Complete(quantity.Value);
            return movement;
        }
        catch (OperationCanceledException)
        {
            telemetryScope.Cancel();
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            telemetryScope.Fail(exception, conflict: true);
            throw;
        }
        catch (Exception exception)
        {
            telemetryScope.Fail(exception);
            throw;
        }
    }

    public async Task<Movement> ReverseReceiptAsync(
        int receiptId,
        int receiptLineId,
        Movement originalMovement,
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalMovement);
        if (originalMovement.Type != MovementType.Receipt ||
            originalMovement.ReceiptId != receiptId ||
            originalMovement.ReceiptLineId != receiptLineId)
        {
            throw new InvalidOperationException(
                "Only the original movement for the receipt line can be reversed.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reversal reason is required.", nameof(reason));
        }

        var locationId = originalMovement.ToLocationId
            ?? throw new InvalidOperationException("The receipt movement has no receiving location.");
        var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken)
            ?? throw new InvalidOperationException("The receipt location was not found.");
        var quantity = originalMovement.Quantity;
        var licensePlateId = originalMovement.ToLicensePlateId ?? originalMovement.LicensePlateId;
        var stock = await FindStockAsync(
            originalMovement.ItemId,
            locationId,
            originalMovement.LotId,
            originalMovement.SerialNumber,
            originalMovement.SerialNumberId,
            originalMovement.InventoryStatusId,
            licensePlateId,
            cancellationToken,
            originalMovement.OwnerKind,
            originalMovement.InventoryOwnerId);
        if (stock is null || stock.GetAvailableQuantity() < quantity)
        {
            throw new InvalidOperationException(
                "The receipt cannot be reversed because its quantity is no longer available.");
        }

        var before = stock.QuantityAvailable.Value;
        stock.RemoveQuantity(quantity);
        await _unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
        var after = stock.QuantityAvailable.Value;
        var reversal = Movement.CreateAdjustment(
            originalMovement.ItemId,
            locationId,
            new Quantity(after),
            userId,
            originalMovement.LotId,
            originalMovement.SerialNumber,
            originalMovement.ReferenceNumber,
            reason,
            _clock.UtcNow.UtcDateTime,
            originalMovement.SerialNumberId,
            originalMovement.InventoryStatusId,
            licensePlateId,
            before,
            -quantity.Value,
            after);
        reversal.SetOwnership(
            originalMovement.OwnerKind,
            originalMovement.InventoryOwnerId,
            originalMovement.OwnerCodeSnapshot);
        reversal.LinkReceipt(
            receiptId,
            receiptLineId,
            ReceiptMovementKind.Reversal,
            originalMovement.Id);
        await _unitOfWork.Movements.AddAsync(reversal, cancellationToken);

        if (originalMovement.SerialNumberId.HasValue && after == 0m)
        {
            var serial = await _unitOfWork.SerialNumbers.GetByIdAsync(
                originalMovement.SerialNumberId.Value,
                cancellationToken);
            serial?.RecordCorrection(reason, _clock.UtcNow.UtcDateTime);
            if (serial is not null)
            {
                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }
        }

        if (_inventoryLedgerService is not null)
        {
            var item = await _unitOfWork.Items.GetByIdAsync(originalMovement.ItemId, cancellationToken)
                ?? throw new InvalidOperationException($"Item {originalMovement.ItemId} was not found.");
            var transactionGroupId = $"movement:{Guid.NewGuid():N}";
            await _inventoryLedgerService.RecordAsync(
                [
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Adjustment,
                        new InventoryBalanceKey(
                            location.WarehouseId,
                            locationId,
                            originalMovement.ItemId,
                            originalMovement.LotId,
                            originalMovement.SerialNumberId,
                            originalMovement.SerialNumber,
                            licensePlateId,
                            originalMovement.InventoryStatusId,
                            item.UnitOfMeasure,
                            originalMovement.OwnerKind,
                            originalMovement.InventoryOwnerId,
                            originalMovement.OwnerCodeSnapshot),
                        -quantity.Value,
                        ActorUserId: userId,
                        ReferenceType: "Receipt",
                        ReferenceId: originalMovement.ReferenceNumber,
                        Reason: reason,
                        OccurredAtUtc: reversal.Timestamp,
                        CorrelationId: _requestContext.CorrelationId,
                        IdempotencyKey: $"{transactionGroupId}:1",
                        TransactionGroupId: transactionGroupId,
                        MovementId: reversal.Id > 0 ? reversal.Id : null)
                ],
                cancellationToken);
        }

        await _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ReceiptRecorded,
                WmsAuditEntityTypes.ReceiptLineMovement,
                $"{receiptId}:{receiptLineId}",
                location.WarehouseId,
                Before: new Dictionary<string, object?>
                {
                    ["quantityAvailable"] = before,
                    ["movementId"] = originalMovement.Id
                },
                After: new Dictionary<string, object?>
                {
                    ["quantityAvailable"] = after,
                    ["reversalMovementId"] = reversal.Id,
                    ["reason"] = reason
                },
                ActorUserId: userId),
            cancellationToken);

        await EnqueueMovementEventAsync(
            WmsIntegrationEventTypes.InventoryMovementRecorded,
            reversal,
            userId,
            location.WarehouseId,
            cancellationToken);
        return reversal;
    }

    public async Task<Movement> PutawayAsync(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null, InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null, string? ownerCodeSnapshot = null)
    {
        var fromLocation = await _unitOfWork.Locations.GetByIdAsync(fromLocationId, cancellationToken);
        var toLocation = await _unitOfWork.Locations.GetByIdAsync(toLocationId, cancellationToken);
        using var operationScope = WmsLogging.BeginOperation(
            _logger,
            _operationContextAccessor,
            _requestContext,
            "inventory.putaway",
            referenceNumber,
            userId,
            fromLocation?.WarehouseId ?? toLocation?.WarehouseId);
        using var telemetryScope = WmsTelemetry.BeginInventoryOperation(
            "putaway",
            fromLocation?.WarehouseId ?? toLocation?.WarehouseId);
        try
        {
            await EnsureActiveItemAsync(itemId, cancellationToken);
            EnsurePutawayLocations(fromLocation, toLocation);
            var licensePlate = await ValidateLicensePlateAsync(
                licensePlateId,
                fromLocation!.WarehouseId,
                fromLocationId,
                cancellationToken);
            // Putaway moves received stock between locations. It must preserve the
            // lot identity, but quarantine/hold lots are valid here because this
            // is not an allocation decision. Picking/reservation remains guarded
            // by allocation eligibility.
            await ValidateLotAsync(itemId, lotId, requireAllocationEligibility: false, cancellationToken);
            var serial = await ResolveSerialForMovementAsync(
                itemId,
                lotId,
                serialNumber,
                fromLocationId,
                requireAllocationEligibility: false,
                quantity,
                cancellationToken);
            // Validate source stock
            var sourceStock = await FindStockAsync(
                itemId,
                fromLocationId,
                lotId,
                serial?.Number ?? serialNumber,
                serial?.Id,
                statusId: null,
                licensePlateId,
                cancellationToken,
                ownerKind,
                inventoryOwnerId);

            if (sourceStock == null)
            {
                throw new InvalidOperationException("Source stock not found");
            }

            if (sourceStock.GetAvailableQuantity() < quantity)
            {
                throw new InvalidOperationException("Insufficient available quantity");
            }

            var sourceQuantityBefore = sourceStock.QuantityAvailable.Value;
            var sourceStatus = sourceStock.InventoryStatus;
            if (sourceStatus is null && _inventoryStatusService is not null)
            {
                sourceStatus = await _unitOfWork.InventoryStatuses.GetByIdAsync(
                    sourceStock.InventoryStatusId,
                    cancellationToken);
            }

            var destinationStatusId = await ResolveDestinationStatusIdAsync(
                sourceStatus,
                toLocation ?? throw new InvalidOperationException("Putaway destination was not found."),
                cancellationToken);
            var destinationStock = await FindStockAsync(
                itemId,
                toLocationId,
                lotId,
                serial?.Number ?? serialNumber,
                serial?.Id,
                destinationStatusId,
                licensePlateId,
                cancellationToken,
                sourceStock.OwnerKind,
                sourceStock.InventoryOwnerId);
            await EnsureInboundCapacityAsync(
                toLocation,
                itemId,
                lotId,
                quantity,
                cancellationToken,
                incomingLicensePlateId: licensePlateId);
            var destinationQuantityBefore = destinationStock?.QuantityAvailable.Value ?? 0m;

            if (licensePlate is not null)
            {
                await MoveWholeLicensePlateForPutawayAsync(
                    licensePlate,
                    fromLocationId,
                    toLocation,
                    sourceStock,
                    quantity,
                    cancellationToken);
            }

            // Create putaway movement
            var movement = Movement.CreatePutaway(itemId, fromLocationId, toLocationId, quantity, userId,
                lotId,
                serial?.Number ?? serialNumber,
                referenceNumber,
                notes,
                _clock.UtcNow.UtcDateTime,
                serial?.Id,
                destinationStatusId,
                fromLicensePlateId: licensePlateId,
                toLicensePlateId: licensePlateId);
            movement.SetOwnership(
                sourceStock.OwnerKind,
                sourceStock.InventoryOwnerId,
                sourceStock.OwnerCodeSnapshot);

            await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

            // Remove from source location
            sourceStock.RemoveQuantity(quantity);
            await _unitOfWork.Stock.UpdateAsync(sourceStock, cancellationToken);

            // Add to destination location
            if (destinationStock != null)
            {
                destinationStock.AddQuantity(quantity);
                await _unitOfWork.Stock.UpdateAsync(destinationStock, cancellationToken);
            }
            else
            {
                var newStock = new Stock(
                    itemId,
                    toLocationId,
                    quantity,
                    lotId,
                    serial?.Number ?? serialNumber,
                    serial?.Id,
                    destinationStatusId,
                    licensePlateId,
                    sourceStock.OwnerKind,
                    sourceStock.InventoryOwnerId,
                    sourceStock.OwnerCodeSnapshot);
                await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
            }

            if (serial is not null)
            {
                if (toLocation is null)
                {
                    throw new InvalidOperationException("Putaway destination was not found.");
                }

                serial.MoveTo(
                    toLocation.WarehouseId,
                    toLocation.Id,
                    licensePlate: null,
                    _clock.UtcNow.UtcDateTime,
                    licensePlateId);
                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }

            if (_inventoryLedgerService is not null)
            {
                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                var sourceKey = new InventoryBalanceKey(
                    fromLocation!.WarehouseId,
                    fromLocationId,
                    itemId,
                    lotId,
                    serial?.Id,
                    serial?.Number ?? serialNumber,
                    licensePlateId,
                    sourceStock.InventoryStatusId,
                    item.UnitOfMeasure,
                    sourceStock.OwnerKind,
                    sourceStock.InventoryOwnerId,
                    sourceStock.OwnerCodeSnapshot);
                var destinationKey = new InventoryBalanceKey(
                    toLocation!.WarehouseId,
                    toLocationId,
                    itemId,
                    lotId,
                    serial?.Id,
                    serial?.Number ?? serialNumber,
                    licensePlateId,
                    destinationStatusId,
                    item.UnitOfMeasure,
                    sourceStock.OwnerKind,
                    sourceStock.InventoryOwnerId,
                    sourceStock.OwnerCodeSnapshot);
                await _inventoryLedgerService.RecordAsync(
                    new[]
                    {
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Putaway,
                            sourceKey,
                            -quantity.Value,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: referenceNumber,
                            Reason: notes,
                            OccurredAtUtc: movement.Timestamp,
                            CorrelationId: _requestContext.CorrelationId,
                            IdempotencyKey: $"{transactionGroupId}:1",
                            TransactionGroupId: transactionGroupId,
                            EntrySequence: 1,
                            MovementId: movement.Id > 0 ? movement.Id : null),
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Putaway,
                            destinationKey,
                            quantity.Value,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: referenceNumber,
                            Reason: notes,
                            OccurredAtUtc: movement.Timestamp,
                            CorrelationId: _requestContext.CorrelationId,
                            IdempotencyKey: $"{transactionGroupId}:2",
                            TransactionGroupId: transactionGroupId,
                            EntrySequence: 2,
                            MovementId: movement.Id > 0 ? movement.Id : null)
                    },
                    cancellationToken);
            }

            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PutawayCompleted,
                    WmsAuditEntityTypes.Movement,
                    $"{fromLocationId}:{toLocationId}",
                    fromLocation?.WarehouseId ?? toLocation?.WarehouseId,
                    Before: new Dictionary<string, object?>
                    {
                        ["sourceQuantityAvailable"] = sourceQuantityBefore,
                        ["destinationQuantityAvailable"] = destinationQuantityBefore
                    },
                    After: new Dictionary<string, object?>
                    {
                        ["quantity"] = quantity.Value,
                        ["sourceQuantityAvailable"] = sourceQuantityBefore - quantity.Value,
                        ["destinationQuantityAvailable"] = destinationQuantityBefore + quantity.Value
                    },
                    ActorUserId: userId),
                cancellationToken);

            _logger.LogInformation(
                WmsLogEvents.InventoryPutawayCompleted,
                "Inventory putaway completed for item {ItemId}, quantity {Quantity}, from {FromLocationId} to {ToLocationId}",
                itemId, quantity.Value, fromLocationId, toLocationId);

            await EnqueueMovementEventAsync(
                WmsIntegrationEventTypes.InventoryMovementRecorded,
                movement,
                userId,
                fromLocation?.WarehouseId ?? toLocation?.WarehouseId,
                cancellationToken);
            telemetryScope.Complete(quantity.Value);
            return movement;
        }
        catch (OperationCanceledException)
        {
            telemetryScope.Cancel();
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            telemetryScope.Fail(exception, conflict: true);
            throw;
        }
        catch (Exception exception)
        {
            telemetryScope.Fail(exception);
            throw;
        }
    }

    public async Task<Movement> PickAsync(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default,
        int? licensePlateId = null, int? inventoryStatusId = null, bool recordLedger = true,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null, string? ownerCodeSnapshot = null)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(fromLocationId, cancellationToken);
        using var operationScope = WmsLogging.BeginOperation(
            _logger,
            _operationContextAccessor,
            _requestContext,
            "inventory.pick",
            referenceNumber,
            userId,
            location?.WarehouseId);
        using var telemetryScope = WmsTelemetry.BeginInventoryOperation(
            "pick",
            location?.WarehouseId);
        try
        {
            await EnsureActiveItemAsync(itemId, cancellationToken);
            EnsurePickingLocation(location);
            await ValidateLicensePlateAsync(
                licensePlateId,
                location!.WarehouseId,
                fromLocationId,
                cancellationToken);
            await ValidateLotAsync(itemId, lotId, requireAllocationEligibility: true, cancellationToken);
            var serial = await ResolveSerialForMovementAsync(
                itemId,
                lotId,
                serialNumber,
                fromLocationId,
                requireAllocationEligibility: true,
                quantity,
                cancellationToken);
            // Validate source stock
            var sourceStock = await FindStockAsync(
                itemId,
                fromLocationId,
                lotId,
                serial?.Number ?? serialNumber,
                serial?.Id,
                statusId: inventoryStatusId,
                licensePlateId,
                cancellationToken,
                ownerKind,
                inventoryOwnerId);

            if (sourceStock == null)
            {
                throw new InvalidOperationException("Source stock not found");
            }

            await EnsureStatusOperationAllowedAsync(
                sourceStock,
                InventoryStatusOperation.Pick,
                cancellationToken);

            if (sourceStock.GetAvailableQuantity() < quantity)
            {
                throw new InvalidOperationException("Insufficient available quantity");
            }

            var sourceQuantityBefore = sourceStock.QuantityAvailable.Value;

            // Create pick movement
            var movement = Movement.CreatePick(itemId, fromLocationId, quantity, userId,
                lotId,
                serial?.Number ?? serialNumber,
                referenceNumber,
                notes,
                _clock.UtcNow.UtcDateTime,
                serial?.Id,
                sourceStock.InventoryStatusId,
                fromLicensePlateId: licensePlateId);
            movement.SetOwnership(
                sourceStock.OwnerKind,
                sourceStock.InventoryOwnerId,
                sourceStock.OwnerCodeSnapshot);

            await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

            // Remove from source location
            sourceStock.RemoveQuantity(quantity);
            await _unitOfWork.Stock.UpdateAsync(sourceStock, cancellationToken);

            if (serial is not null)
            {
                serial.RecordPick(_clock.UtcNow.UtcDateTime);
                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }

            if (_inventoryLedgerService is not null && recordLedger)
            {
                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                await _inventoryLedgerService.RecordAsync(
                    new[]
                    {
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Pick,
                            new InventoryBalanceKey(
                                location!.WarehouseId,
                                fromLocationId,
                                itemId,
                                lotId,
                                serial?.Id,
                                serial?.Number ?? serialNumber,
                                sourceStock.LicensePlateId,
                                sourceStock.InventoryStatusId,
                                item.UnitOfMeasure,
                                sourceStock.OwnerKind,
                                sourceStock.InventoryOwnerId,
                                sourceStock.OwnerCodeSnapshot),
                            -quantity.Value,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: referenceNumber,
                            Reason: notes,
                            OccurredAtUtc: movement.Timestamp,
                            CorrelationId: _requestContext.CorrelationId,
                            IdempotencyKey: $"{transactionGroupId}:1",
                            TransactionGroupId: transactionGroupId,
                            MovementId: movement.Id > 0 ? movement.Id : null)
                    },
                    cancellationToken);
            }

            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PickCompleted,
                    WmsAuditEntityTypes.Movement,
                    $"{itemId}:{fromLocationId}",
                    location?.WarehouseId,
                    Before: new Dictionary<string, object?>
                    {
                        ["quantityAvailable"] = sourceQuantityBefore
                    },
                    After: new Dictionary<string, object?>
                    {
                        ["quantity"] = quantity.Value,
                        ["quantityAvailable"] = sourceQuantityBefore - quantity.Value,
                        ["referenceNumber"] = referenceNumber
                    },
                    ActorUserId: userId),
                cancellationToken);

            _logger.LogInformation(
                WmsLogEvents.InventoryPickCompleted,
                "Inventory pick completed for item {ItemId}, quantity {Quantity}, from {FromLocationId}",
                itemId,
                quantity.Value,
                fromLocationId);

            await EnqueueMovementEventAsync(
                WmsIntegrationEventTypes.InventoryMovementRecorded,
                movement,
                userId,
                location?.WarehouseId,
                cancellationToken);
            telemetryScope.Complete(quantity.Value);
            return movement;
        }
        catch (OperationCanceledException)
        {
            telemetryScope.Cancel();
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            telemetryScope.Fail(exception, conflict: true);
            throw;
        }
        catch (Exception exception)
        {
            telemetryScope.Fail(exception);
            throw;
        }
    }

    public async Task<Movement> AdjustAsync(int itemId, int locationId, Quantity newQuantity, string userId,
        string reason, int? lotId = null, string? serialNumber = null,
        CancellationToken cancellationToken = default, int? licensePlateId = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null, string? ownerCodeSnapshot = null)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
        using var operationScope = WmsLogging.BeginOperation(
            _logger,
            _operationContextAccessor,
            _requestContext,
            "inventory.adjustment",
            $"{itemId}:{locationId}",
            userId,
            location?.WarehouseId);
        using var telemetryScope = WmsTelemetry.BeginInventoryOperation(
            "adjustment",
            location?.WarehouseId);
        try
        {
            await EnsureActiveItemAsync(itemId, cancellationToken);
            EnsureAdjustmentLocation(location, reason);
            await ValidateLicensePlateAsync(
                licensePlateId,
                location!.WarehouseId,
                locationId,
                cancellationToken);
            await ValidateLotAsync(itemId, lotId, requireAllocationEligibility: false, cancellationToken);
            var serial = await ResolveSerialForAdjustmentAsync(
                itemId,
                lotId,
                serialNumber,
                locationId,
                newQuantity,
                cancellationToken);
            var stock = await FindStockAsync(
                itemId,
                locationId,
                lotId,
                serial?.Number ?? serialNumber,
                serial?.Id,
                statusId: null,
                licensePlateId,
                cancellationToken,
                ownerKind,
                inventoryOwnerId);
            var adjustmentStatusId = stock?.InventoryStatusId ?? await ResolveInboundStatusIdAsync(
                location,
                itemId,
                cancellationToken);
            var quantityBefore = stock?.QuantityAvailable.Value ?? 0m;
            var adjustmentDelta = newQuantity.Value - quantityBefore;

            if (stock == null)
            {
                // Create new stock if adjusting to positive quantity
                if (newQuantity.Value > 0)
                {
                    await EnsureInboundCapacityAsync(
                        location,
                        itemId,
                        lotId,
                        newQuantity,
                        cancellationToken,
                        incomingLicensePlateId: licensePlateId);
                    var newStock = new Stock(
                        itemId,
                        locationId,
                        newQuantity,
                        lotId,
                        serial?.Number ?? serialNumber,
                        serial?.Id,
                        adjustmentStatusId,
                        licensePlateId,
                        ownerKind,
                        inventoryOwnerId,
                        ownerCodeSnapshot);
                    await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
                }
            }
            else
            {
                var delta = newQuantity.Value - stock.QuantityAvailable.Value;
                if (delta > 0)
                {
                    await EnsureInboundCapacityAsync(
                        location,
                        itemId,
                        lotId,
                        new Quantity(delta, newQuantity.ConversionSnapshot),
                        cancellationToken,
                        incomingLicensePlateId: licensePlateId);
                }
                stock.AdjustQuantity(newQuantity, reason);
                await _unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
            }

            // Create adjustment movement
            var movement = Movement.CreateAdjustment(itemId, locationId, newQuantity, userId,
                lotId,
                serial?.Number ?? serialNumber,
                notes: reason,
                timestampUtc: _clock.UtcNow.UtcDateTime,
                serialNumberId: serial?.Id,
                inventoryStatusId: adjustmentStatusId,
                licensePlateId: licensePlateId,
                adjustmentBeforeQuantity: quantityBefore,
                adjustmentDelta: adjustmentDelta,
                adjustmentAfterQuantity: newQuantity.Value);
            movement.SetOwnership(ownerKind, inventoryOwnerId, ownerCodeSnapshot);

            await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

            if (serial is not null)
            {
                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                if (newQuantity.Value == 0)
                {
                    serial.RecordCorrection(reason, _clock.UtcNow.UtcDateTime);
                }
                else if (location is null)
                {
                    throw new InvalidOperationException("Adjustment location was not found.");
                }
                else if (serial.Id == 0 || serial.Status == SerialStatus.Returned)
                {
                    serial.RecordReceipt(
                        location.WarehouseId,
                        location.Id,
                        lotId,
                        referenceNumber: null,
                        quarantine: item.QualityInspectionRequired,
                        timestampUtc: _clock.UtcNow.UtcDateTime,
                        licensePlateId: licensePlateId);
                }
                else
                {
                    serial.MoveTo(
                        location.WarehouseId,
                        location.Id,
                        licensePlate: null,
                        _clock.UtcNow.UtcDateTime,
                        licensePlateId);
                }

                await _unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }

            if (_inventoryLedgerService is not null)
            {
                var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
                    ?? throw new InvalidOperationException($"Item {itemId} was not found.");
                var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                await _inventoryLedgerService.RecordAsync(
                    new[]
                    {
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Adjustment,
                            new InventoryBalanceKey(
                                location!.WarehouseId,
                                locationId,
                                itemId,
                                lotId,
                                serial?.Id,
                                serial?.Number ?? serialNumber,
                                licensePlateId,
                                adjustmentStatusId,
                                item.UnitOfMeasure,
                                ownerKind,
                                inventoryOwnerId,
                                ownerCodeSnapshot),
                            adjustmentDelta,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: $"{itemId}:{locationId}",
                            Reason: reason,
                            OccurredAtUtc: movement.Timestamp,
                            CorrelationId: _requestContext.CorrelationId,
                            IdempotencyKey: $"{transactionGroupId}:1",
                            TransactionGroupId: transactionGroupId,
                            MovementId: movement.Id > 0 ? movement.Id : null)
                    },
                    cancellationToken);
            }

            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.StockAdjusted,
                    WmsAuditEntityTypes.Stock,
                    $"{itemId}:{locationId}",
                    location?.WarehouseId,
                    Before: new Dictionary<string, object?>
                    {
                        ["quantityAvailable"] = quantityBefore
                    },
                    After: new Dictionary<string, object?>
                    {
                        ["quantityAvailable"] = newQuantity.Value,
                        ["delta"] = adjustmentDelta,
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);

            _logger.LogInformation(
                WmsLogEvents.InventoryAdjustmentCompleted,
                "Inventory adjustment completed for item {ItemId}, new quantity {Quantity}, location {LocationId}",
                itemId,
                newQuantity.Value,
                locationId);

            await EnqueueMovementEventAsync(
                WmsIntegrationEventTypes.InventoryStockAdjusted,
                movement,
                userId,
                location?.WarehouseId,
                cancellationToken);
            telemetryScope.Complete(newQuantity.Value);
            return movement;
        }
        catch (OperationCanceledException)
        {
            telemetryScope.Cancel();
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            telemetryScope.Fail(exception, conflict: true);
            throw;
        }
        catch (Exception exception)
        {
            telemetryScope.Fail(exception);
            throw;
        }
    }

    private async Task<SerialNumber?> ResolveSerialForReceiptAsync(
        int itemId,
        int? lotId,
        string? serialNumber,
        Quantity quantity,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {itemId} was not found.");
        if (!item.RequiresSerial)
        {
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new InvalidOperationException(
                    $"Item '{item.Sku}' is not serial controlled and must not receive a serial number.");
            }

            return null;
        }

        EnsureSerialQuantity(item, quantity, allowZero: false);
        if (_serialNumberService is null)
        {
            throw new InvalidOperationException("Serial tracking is not available.");
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new InvalidOperationException($"Item '{item.Sku}' requires a serial number.");
        }

        var result = await _serialNumberService.ResolveForReceiptAsync(
            item,
            serialNumber,
            lotId,
            cancellationToken);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error);
    }

    private async Task<SerialNumber?> ResolveSerialForMovementAsync(
        int itemId,
        int? lotId,
        string? serialNumber,
        int expectedLocationId,
        bool requireAllocationEligibility,
        Quantity quantity,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {itemId} was not found.");
        if (!item.RequiresSerial)
        {
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new InvalidOperationException(
                    $"Item '{item.Sku}' is not serial controlled and must not carry a serial number.");
            }

            return null;
        }

        EnsureSerialQuantity(item, quantity, allowZero: false);
        if (_serialNumberService is null)
        {
            throw new InvalidOperationException("Serial tracking is not available.");
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new InvalidOperationException($"Item '{item.Sku}' requires a serial number.");
        }

        var result = await _serialNumberService.ResolveForMovementAsync(
            item,
            serialNumber,
            lotId,
            expectedLocationId,
            requireAllocationEligibility,
            cancellationToken);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Error);
    }

    private async Task<SerialNumber?> ResolveSerialForAdjustmentAsync(
        int itemId,
        int? lotId,
        string? serialNumber,
        int expectedLocationId,
        Quantity newQuantity,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {itemId} was not found.");
        if (!item.RequiresSerial)
        {
            if (!string.IsNullOrWhiteSpace(serialNumber))
            {
                throw new InvalidOperationException(
                    $"Item '{item.Sku}' is not serial controlled and must not carry a serial number.");
            }

            return null;
        }

        EnsureSerialQuantity(item, newQuantity, allowZero: true);
        if (_serialNumberService is null)
        {
            throw new InvalidOperationException("Serial tracking is not available.");
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new InvalidOperationException($"Item '{item.Sku}' requires a serial number.");
        }

        var existing = await _serialNumberService.ResolveForMovementAsync(
            item,
            serialNumber,
            lotId,
            expectedLocationId,
            requireAllocationEligibility: false,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return existing.Value;
        }

        if (newQuantity.Value > 0 && existing.ErrorCode == "serial.not_found")
        {
            var created = await _serialNumberService.ResolveForReceiptAsync(
                item,
                serialNumber,
                lotId,
                cancellationToken);
            return created.IsSuccess
                ? created.Value
                : throw new InvalidOperationException(created.Error);
        }

        throw new InvalidOperationException(existing.Error);
    }

    private static void EnsureSerialQuantity(Item item, Quantity quantity, bool allowZero)
    {
        if (allowZero && quantity.Value == 0m)
        {
            return;
        }

        if (quantity.Value != 1m)
        {
            throw new InvalidOperationException(
                $"Serial-controlled item '{item.Sku}' requires a quantity of exactly one unit.");
        }
    }

    private async Task<Item> EnsureActiveItemAsync(
        int itemId,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {itemId} was not found.");
        if (!item.IsActive)
        {
            throw new InvalidOperationException($"Item '{item.Sku}' is inactive.");
        }

        return item;
    }

    private static void EnsureReceivingLocation(Location? location)
    {
        if (location is null)
        {
            throw new InvalidOperationException("Receipt location was not found.");
        }

        if (!location.IsActive)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is inactive.");
        }

        if (!location.IsReceivable || !location.SupportsReceiving)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is not receivable.");
        }
    }

    private static void EnsurePutawayLocations(Location? fromLocation, Location? toLocation)
    {
        if (fromLocation is null)
        {
            throw new InvalidOperationException("Putaway source location was not found.");
        }

        if (toLocation is null)
        {
            throw new InvalidOperationException("Putaway destination was not found.");
        }

        if (!fromLocation.IsActive)
        {
            throw new InvalidOperationException($"Location '{fromLocation.Code}' is inactive.");
        }

        if (!toLocation.IsActive)
        {
            throw new InvalidOperationException($"Location '{toLocation.Code}' is inactive.");
        }

        if (fromLocation.WarehouseId != toLocation.WarehouseId)
        {
            throw new InvalidOperationException(
                "Putaway source and destination must belong to the same warehouse.");
        }

        if (!toLocation.IsReceivable || !toLocation.SupportsReceiving)
        {
            throw new InvalidOperationException($"Location '{toLocation.Code}' is not receivable.");
        }
    }

    private static void EnsurePickingLocation(Location? location)
    {
        if (location is null)
        {
            throw new InvalidOperationException("Pick source location was not found.");
        }

        if (!location.IsActive)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is inactive.");
        }

        if (!location.IsPickable || !location.SupportsPicking)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is not pickable.");
        }
    }

    private static void EnsureAdjustmentLocation(Location? location, string reason)
    {
        if (location is null)
        {
            throw new InvalidOperationException("Adjustment location was not found.");
        }

        if (!location.IsActive)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is inactive.");
        }

        if (!location.IsCountable)
        {
            throw new InvalidOperationException($"Location '{location.Code}' is not countable.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Adjustment reason is required.");
        }
    }

    private async Task<LicensePlate?> ValidateLicensePlateAsync(
        int? licensePlateId,
        int warehouseId,
        int locationId,
        CancellationToken cancellationToken)
    {
        if (!licensePlateId.HasValue)
        {
            return null;
        }

        var licensePlate = await _unitOfWork.LicensePlates.GetByIdAsync(
            licensePlateId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"License plate {licensePlateId.Value} was not found.");

        if (!licensePlate.IsActive ||
            licensePlate.Status is not (LicensePlateStatus.Open or LicensePlateStatus.Returned))
        {
            throw new InvalidOperationException(
                $"License plate '{licensePlate.Number}' is not available for inventory movement.");
        }

        if (licensePlate.WarehouseId != warehouseId)
        {
            throw new InvalidOperationException(
                $"License plate '{licensePlate.Number}' belongs to another warehouse.");
        }

        if (licensePlate.CurrentLocationId != locationId)
        {
            throw new InvalidOperationException(
                $"License plate '{licensePlate.Number}' is not at the requested location.");
        }

        return licensePlate;
    }

    private async Task MoveWholeLicensePlateForPutawayAsync(
        LicensePlate licensePlate,
        int sourceLocationId,
        Location destinationLocation,
        Stock sourceStock,
        Quantity quantity,
        CancellationToken cancellationToken)
    {
        var contents = (await _unitOfWork.Stock.GetByLicensePlateIdAsync(
                licensePlate.Id,
                cancellationToken))
            .Where(stock => stock.LocationId == sourceLocationId &&
                            (stock.QuantityAvailable.Value > 0 || stock.QuantityReserved.Value > 0))
            .ToArray();

        if (contents.Length != 1 ||
            contents[0].Id != sourceStock.Id ||
            sourceStock.QuantityReserved.Value != 0m ||
            sourceStock.QuantityAvailable.Value != quantity.Value)
        {
            throw new InvalidOperationException(
                "Putaway of an LPN must move its complete unreserved contents in one operation.");
        }

        licensePlate.MoveTo(destinationLocation.WarehouseId, destinationLocation.Id);
        await _unitOfWork.LicensePlates.UpdateAsync(licensePlate, cancellationToken);
    }

    private async Task<Stock?> FindStockAsync(
        int itemId,
        int locationId,
        int? lotId,
        string? serialNumber,
        int? serialNumberId,
        int? statusId,
        int? licensePlateId,
        CancellationToken cancellationToken,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        var normalizedOwnerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        if (_inventoryStatusService is null && !statusId.HasValue)
        {
            var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
                itemId,
                locationId,
                lotId,
                serialNumber,
                serialNumberId,
                cancellationToken,
                licensePlateId,
                ownerKind,
                inventoryOwnerId);
            return stock is null || stock.OwnerCodeSnapshot == normalizedOwnerCode
                ? stock
                : null;
        }

        var candidates = (await _unitOfWork.Stock.GetByLocationIdAsync(
                locationId,
                cancellationToken))
            .Where(stock => stock.ItemId == itemId && stock.LotId == lotId)
            .Where(stock => HasSerialIdentity(
                stock,
                serialNumber,
                serialNumberId))
            .Where(stock => stock.LicensePlateId == licensePlateId)
            .Where(stock => stock.OwnerKind == ownerKind &&
                            stock.InventoryOwnerId == inventoryOwnerId &&
                            stock.OwnerCodeSnapshot == normalizedOwnerCode);
        if (statusId.HasValue)
        {
            candidates = candidates.Where(stock => stock.InventoryStatusId == statusId.Value);
        }

        return candidates
            .OrderByDescending(stock => stock.InventoryStatus?.IsAllocatable == true)
            .ThenByDescending(stock => stock.GetAvailableQuantity().Value)
            .FirstOrDefault();
    }

    private async Task<int> ResolveInboundStatusIdAsync(
        Location? location,
        int itemId,
        CancellationToken cancellationToken)
    {
        if (_inventoryStatusService is null)
        {
            return InventoryStatusSystemIds.Available;
        }

        if (location is null)
        {
            throw new InvalidOperationException("Receipt location was not found.");
        }

        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken)
            ?? throw new InvalidOperationException($"Item {itemId} was not found.");
        var qualityRequired = item.QualityInspectionRequired;
        if (!qualityRequired && _qualityInspectionService is not null)
        {
            qualityRequired = await _qualityInspectionService.RequiresInboundInspectionAsync(
                location.WarehouseId,
                itemId,
                cancellationToken);
        }

        var result = await _inventoryStatusService.ResolveInboundStatusAsync(
            location,
            qualityRequired,
            cancellationToken);
        return result.IsSuccess
            ? result.Value.Id
            : throw new InvalidOperationException(result.Error);
    }

    private async Task<int> ResolveDestinationStatusIdAsync(
        InventoryStatus? sourceStatus,
        Location destination,
        CancellationToken cancellationToken)
    {
        if (_inventoryStatusService is null)
        {
            return sourceStatus?.Id ?? InventoryStatusSystemIds.Available;
        }

        sourceStatus ??= await _unitOfWork.InventoryStatuses.GetByIdAsync(
            sourceStatus?.Id ?? InventoryStatusSystemIds.Available,
            cancellationToken);
        if (sourceStatus is null)
        {
            throw new InvalidOperationException("Source inventory status was not found.");
        }

        var result = await _inventoryStatusService.ResolveDestinationStatusAsync(
            sourceStatus,
            destination,
            cancellationToken);
        return result.IsSuccess
            ? result.Value.Id
            : throw new InvalidOperationException(result.Error);
    }

    private async Task EnsureStatusOperationAllowedAsync(
        Stock stock,
        InventoryStatusOperation operation,
        CancellationToken cancellationToken)
    {
        if (_inventoryStatusService is null)
        {
            return;
        }

        var result = await _inventoryStatusService.ValidateOperationAsync(
            stock,
            operation,
            cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error);
        }
    }

    private static bool HasSerialIdentity(
        Stock stock,
        string? serialNumber,
        int? serialNumberId)
    {
        if (serialNumberId.HasValue)
        {
            return stock.SerialNumberId == serialNumberId.Value ||
                   (stock.SerialNumberId is null &&
                    string.Equals(
                        stock.SerialNumber,
                        serialNumber?.Trim(),
                        StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            return string.Equals(
                stock.SerialNumber,
                serialNumber.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        return stock.SerialNumber is null && stock.SerialNumberId is null;
    }

    private async Task EnsureInboundCapacityAsync(
        Location? location,
        int itemId,
        int? lotId,
        Quantity incomingQuantity,
        CancellationToken cancellationToken,
        int? incomingLicensePlateId = null)
    {
        if (location is null)
        {
            throw new LocationConstraintViolationException(
                "location.not_found",
                "The target location was not found.");
        }

        var occupiedStock = (await _unitOfWork.Stock.GetByLocationIdAsync(
                location.Id,
                cancellationToken))
            .Where(stock => stock.QuantityAvailable.Value > 0 || stock.QuantityReserved.Value > 0)
            .ToArray();
        var item = occupiedStock.FirstOrDefault(stock => stock.ItemId == itemId)?.Item ??
                   await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken);

        if (!location.AllowMixedItems && occupiedStock.Any(stock => stock.ItemId != itemId))
        {
            throw new LocationConstraintViolationException(
                "location.mixed_items_blocked",
                $"Location '{location.Code}' does not allow mixed items.");
        }

        if (!location.AllowMixedLots && occupiedStock.Any(stock =>
                stock.ItemId == itemId && stock.LotId != lotId))
        {
            throw new LocationConstraintViolationException(
                "location.mixed_lots_blocked",
                $"Location '{location.Code}' does not allow mixed lots.");
        }

        var currentCapacity = new LocationCapacitySnapshot(
            occupiedStock.Sum(stock => stock.QuantityAvailable.Value),
            occupiedStock.Sum(stock => stock.QuantityAvailable.Value * (stock.Item.NetWeightKg ?? 0m)),
            occupiedStock.Sum(stock => stock.QuantityAvailable.Value * (stock.Item.VolumeCubicMeters ?? 0m)),
            Lpns: occupiedStock
                .Where(stock => stock.LicensePlateId.HasValue)
                .Select(stock => stock.LicensePlateId!.Value)
                .Distinct()
                .Count());
        var incomingCapacity = CalculateIncomingCapacity(
            incomingQuantity,
            item,
            incomingLicensePlateId.HasValue &&
            occupiedStock.All(stock => stock.LicensePlateId != incomingLicensePlateId));
        var violation = location.ValidateCapacity(currentCapacity, incomingCapacity);
        if (violation is not null)
        {
            throw new LocationConstraintViolationException(
                violation.Code,
                violation.Message);
        }
    }

    private Task EnqueueMovementEventAsync(
        string eventType,
        Movement movement,
        string userId,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (_integrationEventWriter is null)
        {
            return Task.CompletedTask;
        }

        var aggregateKey = movement.Id > 0
            ? movement.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{movement.Type}:{movement.Timestamp.Ticks}:{movement.ItemId}:{movement.ToLocationId ?? movement.FromLocationId}";
        return _integrationEventWriter.EnqueueAsync(
            new IntegrationEventDraft(
                eventType,
                "Movement",
                aggregateKey,
                new
                {
                    movementId = movement.Id,
                    movementType = movement.Type.ToString(),
                    itemId = movement.ItemId,
                    fromLocationId = movement.FromLocationId,
                    toLocationId = movement.ToLocationId,
                    quantity = movement.Quantity.Value,
                    userId,
                    movement.Timestamp,
                    referenceNumber = movement.ReferenceNumber,
                    receiptId = movement.ReceiptId,
                    receiptLineId = movement.ReceiptLineId
                },
                warehouseId,
                _requestContext.CorrelationId,
                _operationContextAccessor.Current?.OperationId,
                new DateTimeOffset(DateTime.SpecifyKind(movement.Timestamp, DateTimeKind.Utc))),
            cancellationToken);
    }

    private async Task<Lot?> ValidateLotAsync(
        int itemId,
        int? lotId,
        bool requireAllocationEligibility,
        CancellationToken cancellationToken)
    {
        var item = await _unitOfWork.Items.GetByIdAsync(itemId, cancellationToken);
        if (item is null)
        {
            throw new InvalidOperationException($"Item {itemId} was not found.");
        }

        if (!lotId.HasValue)
        {
            if (item.RequiresLot)
            {
                throw new InvalidOperationException(
                    $"Item '{item.Sku}' requires a persisted lot identity for this movement.");
            }

            return null;
        }

        var lot = await _unitOfWork.Lots.GetByIdAsync(lotId.Value, cancellationToken);
        if (lot is null)
        {
            throw new InvalidOperationException($"Lot {lotId.Value} was not found.");
        }

        if (!item.RequiresLot)
        {
            throw new InvalidOperationException(
                $"Item '{item.Sku}' is not lot controlled and cannot carry lot '{lot.Number}'.");
        }

        if (lot.ItemId != itemId)
        {
            throw new InvalidOperationException(
                $"Lot '{lot.Number}' does not belong to item '{item.Sku}'.");
        }

        var businessDate = DateOnly.FromDateTime(_clock.UtcNow.DateTime);
        if (requireAllocationEligibility && !lot.IsAllocationEligible(businessDate))
        {
            throw new InvalidOperationException(
                $"Lot '{lot.Number}' is not eligible for allocation.");
        }

        if (!requireAllocationEligibility &&
            lot.Status is LotStatus.Recalled or LotStatus.Closed)
        {
            throw new InvalidOperationException(
                $"Lot '{lot.Number}' cannot be used while it is {lot.Status}.");
        }

        return lot;
    }

    private static LocationCapacitySnapshot CalculateIncomingCapacity(
        Quantity quantity,
        Item? item,
        bool addsLicensePlate = false)
    {
        var conversion = quantity.ConversionSnapshot;
        var packaging = conversion?.PackagingSnapshot;
        if (packaging is null)
        {
            return new LocationCapacitySnapshot(
                quantity.Value,
                quantity.Value * (item?.NetWeightKg ?? 0m),
                quantity.Value * (item?.VolumeCubicMeters ?? 0m),
                Lpns: addsLicensePlate ? 1 : 0);
        }

        var packageCount = conversion!.ConversionFactorToBase <= 0
            ? 0m
            : quantity.Value / conversion.ConversionFactorToBase;
        var pallets = packaging.Type == PackagingType.Pallet
            ? (int)Math.Ceiling(packageCount)
            : 0;
        return new LocationCapacitySnapshot(
            quantity.Value,
            (packaging.GrossWeightKg ?? item?.NetWeightKg * packaging.UnitsPerPackage ?? 0m) * packageCount,
            (packaging.VolumeCubicMeters ?? item?.VolumeCubicMeters * packaging.UnitsPerPackage ?? 0m) * packageCount,
            pallets,
            Lpns: addsLicensePlate ? 1 : 0);
    }
}
