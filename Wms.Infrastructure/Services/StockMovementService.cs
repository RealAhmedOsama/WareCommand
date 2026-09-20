// Wms.Infrastructure/Services/StockMovementService.cs

using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Services;

public class StockMovementService : IStockMovementService
{
    private readonly IAuditWriter _auditWriter;
    private readonly ILogger<StockMovementService> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public StockMovementService(
        IUnitOfWork unitOfWork,
        ILogger<StockMovementService> logger,
        IAuditWriter auditWriter)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _auditWriter = auditWriter;
    }

    public async Task<Movement> ReceiveAsync(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
        var existingStock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
            itemId, locationId, lotId, serialNumber, cancellationToken);
        var quantityBefore = existingStock?.QuantityAvailable.Value ?? 0m;

        // Create receipt movement
        var movement = Movement.CreateReceipt(itemId, locationId, quantity, userId,
            lotId, serialNumber, referenceNumber, notes);

        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

        // Update or create stock record
        if (existingStock != null)
        {
            existingStock.AddQuantity(quantity);
            await _unitOfWork.Stock.UpdateAsync(existingStock, cancellationToken);
        }
        else
        {
            var newStock = new Stock(itemId, locationId, quantity, lotId, serialNumber);
            await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
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

        _logger.LogInformation("Receipt processed: Item {ItemId}, Quantity {Quantity}, Location {LocationId}",
            itemId, quantity.Value, locationId);

        return movement;
    }

    public async Task<Movement> PutawayAsync(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null, CancellationToken cancellationToken = default)
    {
        var fromLocation = await _unitOfWork.Locations.GetByIdAsync(fromLocationId, cancellationToken);
        var toLocation = await _unitOfWork.Locations.GetByIdAsync(toLocationId, cancellationToken);
        // Validate source stock
        var sourceStock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
            itemId, fromLocationId, lotId, serialNumber, cancellationToken);

        if (sourceStock == null)
            throw new InvalidOperationException("Source stock not found");

        if (sourceStock.GetAvailableQuantity() < quantity)
            throw new InvalidOperationException("Insufficient available quantity");

        var sourceQuantityBefore = sourceStock.QuantityAvailable.Value;
        var destinationStock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
            itemId, toLocationId, lotId, serialNumber, cancellationToken);
        var destinationQuantityBefore = destinationStock?.QuantityAvailable.Value ?? 0m;

        // Create putaway movement
        var movement = Movement.CreatePutaway(itemId, fromLocationId, toLocationId, quantity, userId,
            lotId, serialNumber, referenceNumber, notes);

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
            var newStock = new Stock(itemId, toLocationId, quantity, lotId, serialNumber);
            await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
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
            "Putaway processed: Item {ItemId}, Quantity {Quantity}, From {FromLocationId} to {ToLocationId}",
            itemId, quantity.Value, fromLocationId, toLocationId);

        return movement;
    }

    public async Task<Movement> PickAsync(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null,
        string? notes = null, CancellationToken cancellationToken = default)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(fromLocationId, cancellationToken);
        // Validate source stock
        var sourceStock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
            itemId, fromLocationId, lotId, serialNumber, cancellationToken);

        if (sourceStock == null)
            throw new InvalidOperationException("Source stock not found");

        if (sourceStock.GetAvailableQuantity() < quantity)
            throw new InvalidOperationException("Insufficient available quantity");

        var sourceQuantityBefore = sourceStock.QuantityAvailable.Value;

        // Create pick movement
        var movement = Movement.CreatePick(itemId, fromLocationId, quantity, userId,
            lotId, serialNumber, referenceNumber, notes);

        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

        // Remove from source location
        sourceStock.RemoveQuantity(quantity);
        await _unitOfWork.Stock.UpdateAsync(sourceStock, cancellationToken);

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

        _logger.LogInformation("Pick processed: Item {ItemId}, Quantity {Quantity}, From {FromLocationId}",
            itemId, quantity.Value, fromLocationId);

        return movement;
    }

    public async Task<Movement> AdjustAsync(int itemId, int locationId, Quantity newQuantity, string userId,
        string reason, int? lotId = null, string? serialNumber = null, CancellationToken cancellationToken = default)
    {
        var location = await _unitOfWork.Locations.GetByIdAsync(locationId, cancellationToken);
        var stock = await _unitOfWork.Stock.GetByItemAndLocationAsync(
            itemId, locationId, lotId, serialNumber, cancellationToken);
        var quantityBefore = stock?.QuantityAvailable.Value;

        if (stock == null)
        {
            // Create new stock if adjusting to positive quantity
            if (newQuantity.Value > 0)
            {
                var newStock = new Stock(itemId, locationId, newQuantity, lotId, serialNumber);
                await _unitOfWork.Stock.AddAsync(newStock, cancellationToken);
            }
        }
        else
        {
            stock.AdjustQuantity(newQuantity, reason);
            await _unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
        }

        // Create adjustment movement
        var movement = Movement.CreateAdjustment(itemId, locationId, newQuantity, userId,
            lotId, serialNumber, notes: reason);

        await _unitOfWork.Movements.AddAsync(movement, cancellationToken);

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
                    ["reason"] = reason
                },
                ActorUserId: userId),
            cancellationToken);

        _logger.LogInformation(
            "Adjustment processed: Item {ItemId}, New Quantity {Quantity}, Location {LocationId}, Reason: {Reason}",
            itemId, newQuantity.Value, locationId, reason);

        return movement;
    }
}
