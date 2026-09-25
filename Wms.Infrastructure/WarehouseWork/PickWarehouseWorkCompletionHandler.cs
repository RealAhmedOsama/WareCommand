using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.WarehouseWork;

/// <summary>
/// Completes reservation-backed pick work from scanner scans. The stock
/// movement removes the exact source dimension without writing its ledger leg;
/// reservation consumption writes that source leg and this handler writes the
/// matching staging destination leg in the same work transaction.
/// </summary>
public sealed class PickWarehouseWorkCompletionHandler(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IStockMovementService stockMovementService,
    IInventoryReservationService reservationService,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock) : IWarehouseWorkCompletionHandler
{
    public WarehouseWorkType WorkType => WarehouseWorkType.Pick;

    public async Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
        WarehouseWorkEntity work,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (input.Scans is null)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.pick_scans_required",
                "Pick completion requires exactly one scan contract for every work line."));
        }

        var scansByLine = input.Scans
            .GroupBy(scan => scan.LineId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (scansByLine.Count != input.Scans.Count ||
            input.Scans.Count != work.Lines.Count ||
            work.Lines.Any(line => !scansByLine.ContainsKey(line.Id) ||
                                   scansByLine[line.Id].Length != 1))
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.pick_scan_lines_invalid",
                "Pick scans must identify each work line exactly once."));
        }

        var actualLines = new List<WarehouseWorkLineActualInput>(work.Lines.Count);
        var movementIds = new List<int>(work.Lines.Count);
        foreach (var line in work.Lines.OrderBy(value => value.Sequence))
        {
            var scan = scansByLine[line.Id][0];
            var validation = await ValidateScanAsync(
                work,
                line,
                scan,
                input,
                cancellationToken);
            if (validation is not null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(validation);
            }

            var destination = await context.Locations
                .SingleAsync(location => location.Id == scan.DestinationLocationId, cancellationToken);
            var statusId = line.InventoryStatusId ?? InventoryStatusSystemIds.Available;
            var targetLicensePlateId = scan.TargetLicensePlateId ?? line.LicensePlateId;
            var quantity = new Quantity(scan.ActualQuantity);
            var movement = await stockMovementService.PickAsync(
                line.ItemId,
                line.SourceLocationId!.Value,
                quantity,
                userId,
                line.LotId,
                line.SerialNumber,
                work.WorkNumber,
                input.CompletionReference ?? "scanner pick",
                cancellationToken,
                line.LicensePlateId,
                statusId,
                recordLedger: false,
                ownerKind: line.OwnerKind,
                inventoryOwnerId: line.InventoryOwnerId,
                ownerCodeSnapshot: line.OwnerCodeSnapshot,
                allowCrossDockReceiving: work.SourceLineReference?.StartsWith(
                    "CROSSDOCK:",
                    StringComparison.OrdinalIgnoreCase) == true);
            movement.LinkPickDestination(destination.Id);

            var destinationStock = await context.Stock
                .SingleOrDefaultAsync(stock =>
                    stock.ItemId == line.ItemId &&
                    stock.LocationId == destination.Id &&
                    stock.LotId == line.LotId &&
                    stock.SerialNumberId == line.SerialNumberId &&
                    stock.SerialNumber == line.SerialNumber &&
                    stock.InventoryStatusId == statusId &&
                    stock.LicensePlateId == targetLicensePlateId &&
                    stock.OwnerKind == line.OwnerKind &&
                    stock.InventoryOwnerId == line.InventoryOwnerId &&
                    stock.OwnerCodeSnapshot == line.OwnerCodeSnapshot,
                    cancellationToken);
            if (destinationStock is null)
            {
                destinationStock = new Stock(
                    line.ItemId,
                    destination.Id,
                    quantity,
                    line.LotId,
                    line.SerialNumber,
                    line.SerialNumberId,
                    statusId,
                    targetLicensePlateId,
                    line.OwnerKind,
                    line.InventoryOwnerId,
                    line.OwnerCodeSnapshot);
                await unitOfWork.Stock.AddAsync(destinationStock, cancellationToken);
            }
            else
            {
                destinationStock.AddQuantity(quantity);
                await unitOfWork.Stock.UpdateAsync(destinationStock, cancellationToken);
            }

            if (line.SerialNumberId.HasValue)
            {
                var serial = await unitOfWork.SerialNumbers.GetByIdAsync(
                    line.SerialNumberId.Value,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Serial number '{line.SerialNumberId}' was not found.");
                var targetLicensePlate = targetLicensePlateId.HasValue
                    ? await unitOfWork.LicensePlates.GetByIdAsync(
                        targetLicensePlateId.Value,
                        cancellationToken)
                    : null;
                serial.MoveTo(
                    destination.WarehouseId,
                    destination.Id,
                    targetLicensePlate?.Number,
                    clock.UtcNow.UtcDateTime,
                    targetLicensePlateId);
                await unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            }

            var transactionGroupId = $"pick-work:{work.Id}:{line.Id}";
            await inventoryLedgerService.RecordAsync(
                [
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Pick,
                        new InventoryBalanceKey(
                            work.WarehouseId,
                            destination.Id,
                            line.ItemId,
                            line.LotId,
                            line.SerialNumberId,
                            line.SerialNumber,
                            targetLicensePlateId,
                            statusId,
                            line.BaseUnitOfMeasure,
                            line.OwnerKind,
                            line.InventoryOwnerId,
                            line.OwnerCodeSnapshot),
                        scan.ActualQuantity,
                        ActorUserId: userId,
                        ReferenceType: "WarehouseWork",
                        ReferenceId: work.WorkNumber,
                        ReferenceLine: line.Id,
                        Reason: input.CompletionReference ?? "picked to staging",
                        OccurredAtUtc: movement.Timestamp,
                        IdempotencyKey: $"{transactionGroupId}:destination",
                        TransactionGroupId: transactionGroupId)
                ],
                cancellationToken);

            var reservationResult = await reservationService.ConsumeAsync(
                new InventoryReservationMutationRequest(
                    line.ReservationId!.Value,
                    scan.ActualQuantity,
                    userId,
                    transactionGroupId,
                    input.CompletionReference ?? "pick consumed reservation",
                    line.ReservationAllocationId),
                cancellationToken);
            if (reservationResult.ConsumedQuantity <= 0m)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                    "work.pick_reservation_not_consumed",
                    "The pick did not consume its reservation allocation."));
            }

            var orderUpdate = await RecordOrderPickedQuantityAsync(
                work,
                line,
                scan.ActualQuantity,
                cancellationToken);
            if (orderUpdate is not null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(orderUpdate);
            }

            actualLines.Add(new WarehouseWorkLineActualInput(line.Id, scan.ActualQuantity));
            movementIds.Add(movement.Id);
        }

        return Result.Success(new WarehouseWorkHandlerResult(
            actualLines,
            "Reservation-backed pick moved stock to the scanned staging destination.",
            movementIds));
    }

    private async Task<ResultError?> ValidateScanAsync(
        WarehouseWorkEntity work,
        WarehouseWorkLine line,
        WarehouseWorkScanInput scan,
        WarehouseWorkCompletionInput input,
        CancellationToken cancellationToken)
    {
        if (scan.ItemId != line.ItemId)
        {
            return WmsErrors.Validation(
                "work.pick_wrong_item",
                $"Scan item '{scan.ItemId}' does not match work line item '{line.ItemId}'.");
        }

        if (line.SourceLocationId is null || scan.SourceLocationId != line.SourceLocationId)
        {
            return WmsErrors.Validation(
                "work.pick_wrong_location",
                "The scanned source location does not match the reservation-backed work line.");
        }

        if (scan.DestinationLocationId == line.SourceLocationId)
        {
            return WmsErrors.Validation(
                "work.pick_destination_invalid",
                "Picked stock must be moved to a different staging or packing location.");
        }

        if (line.DestinationLocationId.HasValue &&
            scan.DestinationLocationId != line.DestinationLocationId.Value &&
            (!scan.DestinationOverride || !input.SupervisorOverride))
        {
            return WmsErrors.Validation(
                "work.pick_destination_mismatch",
                "The scanned destination differs from the planned destination and requires a supervisor override.");
        }

        if (scan.ActualQuantity <= 0m || scan.ActualQuantity > line.PlannedQuantity)
        {
            return WmsErrors.Validation(
                "work.pick_quantity_invalid",
                "Picked quantity must be positive and cannot exceed the planned quantity.");
        }

        if (line.LicensePlateId != scan.LicensePlateId)
        {
            return WmsErrors.Validation(
                "work.pick_wrong_license_plate",
                "The scanned source license plate does not match the reservation-backed work line.");
        }

        if (line.LotId != scan.LotId ||
            line.SerialNumberId != scan.SerialNumberId ||
            !string.Equals(line.SerialNumber, scan.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return WmsErrors.Validation(
                "work.pick_wrong_traceability",
                "The scanned lot or serial does not match the reservation-backed work line.");
        }

        if (line.LicensePlateId.HasValue &&
            scan.ActualQuantity < line.PlannedQuantity)
        {
            return WmsErrors.BusinessRule(
                "work.partial_lpn_not_supported",
                "A license-plate pick must scan the complete reserved quantity.");
        }

        if (scan.TargetLicensePlateId.HasValue &&
            scan.TargetLicensePlateId != line.LicensePlateId)
        {
            return WmsErrors.BusinessRule(
                "work.target_lpn_not_supported",
                "Moving a pick to a different target license plate requires the packing-container workflow.");
        }

        if (line.ReservationId is null || line.ReservationAllocationId is null)
        {
            return WmsErrors.Dependency(
                "work.pick_reservation_link_missing",
                "Pick work is not linked to a reservation allocation.",
                isRetryable: false);
        }

        var destination = await context.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                location => location.Id == scan.DestinationLocationId,
                cancellationToken);
        if (destination is null ||
            !destination.IsActive ||
            destination.WarehouseId != work.WarehouseId ||
            destination.Type is not (LocationType.Staging or LocationType.Packing or LocationType.Shipping))
        {
            return WmsErrors.Validation(
                "work.pick_destination_invalid",
                "The destination must be an active staging, packing, or shipping location in the work warehouse.");
        }

        var source = await context.Stock
            .AsNoTracking()
            .SingleOrDefaultAsync(stock =>
                stock.ItemId == line.ItemId &&
                stock.LocationId == line.SourceLocationId.Value &&
                stock.LotId == line.LotId &&
                stock.SerialNumberId == line.SerialNumberId &&
                stock.SerialNumber == line.SerialNumber &&
                stock.InventoryStatusId == (line.InventoryStatusId ?? InventoryStatusSystemIds.Available) &&
                stock.LicensePlateId == line.LicensePlateId &&
                stock.OwnerKind == line.OwnerKind &&
                stock.InventoryOwnerId == line.InventoryOwnerId &&
                stock.OwnerCodeSnapshot == line.OwnerCodeSnapshot,
                cancellationToken);
        if (source is null || source.GetAvailableQuantity() < scan.ActualQuantity)
        {
            return WmsErrors.BusinessRule(
                "work.pick_source_unavailable",
                "The reservation source dimension no longer has enough available stock.");
        }

        return null;
    }

    private async Task<ResultError?> RecordOrderPickedQuantityAsync(
        WarehouseWorkEntity work,
        WarehouseWorkLine line,
        decimal quantity,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(work.SourceEntityType, "SalesOrderLine", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(
                work.SourceEntityId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var salesOrderLineId) ||
            salesOrderLineId <= 0)
        {
            return WmsErrors.Dependency(
                "work.pick_order_line_invalid",
                "The pick work does not identify a valid sales-order line.",
                isRetryable: false);
        }

        var orderLine = await context.SalesOrderLines
            .SingleOrDefaultAsync(value => value.Id == salesOrderLineId, cancellationToken);
        if (orderLine is null || orderLine.ItemId != line.ItemId)
        {
            return WmsErrors.Dependency(
                "work.pick_order_line_missing",
                "The pick work sales-order line was not found or does not match the item.",
                isRetryable: false);
        }

        if (orderLine.RemainingToPickBaseQuantity < quantity)
        {
            return WmsErrors.BusinessRule(
                "work.pick_order_quantity_exceeded",
                "The picked quantity exceeds the sales-order line quantity still awaiting pick.");
        }

        orderLine.RecordPicked(quantity);
        return null;
    }
}
