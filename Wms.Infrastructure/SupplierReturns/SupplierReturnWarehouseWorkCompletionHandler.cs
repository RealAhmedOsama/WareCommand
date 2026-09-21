using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
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

namespace Wms.Infrastructure.SupplierReturns;

/// <summary>
/// Completes supplier-return work without routing Return-Pending, damaged, or
/// quarantine stock through customer-order picking rules. The source stock is
/// reserved before release and the source reservation is consumed or released
/// in the same transaction as the staging movement.
/// </summary>
public sealed class SupplierReturnWarehouseWorkCompletionHandler(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock) : IWarehouseWorkCompletionHandler
{
    public WarehouseWorkType WorkType => WarehouseWorkType.Return;

    public async Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
        WarehouseWorkEntity work,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (input.Scans is null)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "supplier_return.scans_required",
                "Supplier-return work completion requires exactly one scan for every return line."));
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
                "supplier_return.scan_lines_invalid",
                "Supplier-return scans must identify each work line exactly once."));
        }

        var returnId = int.TryParse(
            work.SourceEntityId,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsedReturnId)
            ? parsedReturnId
            : 0;
        if (returnId <= 0)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Dependency(
                "supplier_return.work_reference_invalid",
                "Supplier-return work does not identify a valid return.",
                isRetryable: false));
        }

        var supplierReturn = await context.SupplierReturns
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == returnId, cancellationToken);
        if (supplierReturn is null || supplierReturn.Status != SupplierReturnStatus.Picking)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                "supplier_return.work_state",
                "Supplier-return work can only execute while the return is in Picking state."));
        }

        var actualLines = new List<WarehouseWorkLineActualInput>(work.Lines.Count);
        var movementIds = new List<int>(work.Lines.Count);
        var ledgerEntries = new List<InventoryLedgerEntryRequest>(work.Lines.Count * 2);
        var sequence = 1;
        foreach (var workLine in work.Lines.OrderBy(value => value.Sequence))
        {
            var scan = scansByLine[workLine.Id][0];
            var lineId = int.TryParse(
                workLine.SourceReference,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedLineId)
                ? parsedLineId
                : 0;
            var returnLine = supplierReturn.Lines.SingleOrDefault(value => value.Id == lineId);
            if (returnLine is null || returnLine.ItemId != workLine.ItemId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Dependency(
                    "supplier_return.work_line_missing",
                    "Supplier-return work line no longer maps to its return line.",
                    isRetryable: false));
            }

            var validation = await ValidateScanAsync(
                supplierReturn,
                returnLine,
                workLine,
                scan,
                input,
                cancellationToken);
            if (validation is not null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(validation);
            }

            var source = await FindStockAsync(
                returnLine.ItemId,
                returnLine.SourceLocationId,
                returnLine,
                returnLine.InventoryStatusId,
                cancellationToken);
            if (source is null ||
                source.QuantityAvailable.Value < scan.ActualQuantity ||
                source.QuantityReserved.Value < returnLine.ApprovedBaseQuantity)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                    "supplier_return.source_unavailable",
                    $"The reserved source stock for return line {returnLine.LineNumber} is no longer available."));
            }

            var destination = await context.Locations
                .SingleAsync(value => value.Id == supplierReturn.StagingLocationId, cancellationToken);
            var destinationStock = await FindStockAsync(
                returnLine.ItemId,
                destination.Id,
                returnLine,
                InventoryStatusSystemIds.ReturnPending,
                cancellationToken);

            source.RemoveQuantity(new Quantity(scan.ActualQuantity));
            source.ReleaseReservation(new Quantity(returnLine.ApprovedBaseQuantity));
            await unitOfWork.Stock.UpdateAsync(source, cancellationToken);

            if (destinationStock is null)
            {
                destinationStock = new Stock(
                    returnLine.ItemId,
                    destination.Id,
                    new Quantity(scan.ActualQuantity),
                    returnLine.LotId,
                    returnLine.SerialNumber,
                    returnLine.SerialNumberId,
                    InventoryStatusSystemIds.ReturnPending,
                    returnLine.LicensePlateId);
                await unitOfWork.Stock.AddAsync(destinationStock, cancellationToken);
            }
            else
            {
                destinationStock.AddQuantity(new Quantity(scan.ActualQuantity));
                await unitOfWork.Stock.UpdateAsync(destinationStock, cancellationToken);
            }

            var movement = Movement.CreatePick(
                returnLine.ItemId,
                returnLine.SourceLocationId,
                new Quantity(scan.ActualQuantity),
                userId,
                returnLine.LotId,
                returnLine.SerialNumber,
                work.WorkNumber,
                input.CompletionReference ?? "supplier return to staging",
                clock.UtcNow.UtcDateTime,
                returnLine.SerialNumberId,
                returnLine.InventoryStatusId,
                returnLine.LicensePlateId);
            movement.LinkPickDestination(destination.Id);
            context.Movements.Add(movement);

            if (returnLine.SerialNumberId.HasValue)
            {
                var serial = await context.SerialNumbers
                    .SingleOrDefaultAsync(
                        value => value.Id == returnLine.SerialNumberId.Value,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Serial number {returnLine.SerialNumberId} was not found.");
                var licensePlate = returnLine.LicensePlateId.HasValue
                    ? await context.LicensePlates.SingleOrDefaultAsync(
                        value => value.Id == returnLine.LicensePlateId.Value,
                        cancellationToken)
                    : null;
                serial.MoveTo(
                    destination.WarehouseId,
                    destination.Id,
                    licensePlate?.Number,
                    clock.UtcNow.UtcDateTime,
                    returnLine.LicensePlateId);
            }

            returnLine.RecordStaged(scan.ActualQuantity);
            if (scan.ActualQuantity < returnLine.ApprovedBaseQuantity)
            {
                returnLine.AcceptShortPick();
            }

            var transactionGroupId = $"supplier-return-work:{work.Id}:{workLine.Id}";
            ledgerEntries.Add(new InventoryLedgerEntryRequest(
                InventoryTransactionType.Pick,
                new InventoryBalanceKey(
                    work.WarehouseId,
                    returnLine.SourceLocationId,
                    returnLine.ItemId,
                    returnLine.LotId,
                    returnLine.SerialNumberId,
                    returnLine.SerialNumber,
                    returnLine.LicensePlateId,
                    returnLine.InventoryStatusId,
                    returnLine.BaseUnitOfMeasure),
                -scan.ActualQuantity,
                -returnLine.ApprovedBaseQuantity,
                ReferenceType: WmsAuditEntityTypes.SupplierReturn,
                ReferenceId: supplierReturn.ReturnNumber,
                ReferenceLine: returnLine.LineNumber,
                Reason: input.CompletionReference ?? "supplier return to staging",
                ActorUserId: userId,
                OccurredAtUtc: movement.Timestamp,
                IdempotencyKey: $"{transactionGroupId}:source",
                TransactionGroupId: transactionGroupId,
                EntrySequence: sequence++,
                MovementId: movement.Id > 0 ? movement.Id : null));
            ledgerEntries.Add(new InventoryLedgerEntryRequest(
                InventoryTransactionType.Pick,
                new InventoryBalanceKey(
                    work.WarehouseId,
                    destination.Id,
                    returnLine.ItemId,
                    returnLine.LotId,
                    returnLine.SerialNumberId,
                    returnLine.SerialNumber,
                    returnLine.LicensePlateId,
                    InventoryStatusSystemIds.ReturnPending,
                    returnLine.BaseUnitOfMeasure),
                scan.ActualQuantity,
                ReferenceType: WmsAuditEntityTypes.SupplierReturn,
                ReferenceId: supplierReturn.ReturnNumber,
                ReferenceLine: returnLine.LineNumber,
                Reason: input.CompletionReference ?? "supplier return to staging",
                ActorUserId: userId,
                OccurredAtUtc: movement.Timestamp,
                IdempotencyKey: $"{transactionGroupId}:destination",
                TransactionGroupId: transactionGroupId,
                EntrySequence: sequence++));

            actualLines.Add(new WarehouseWorkLineActualInput(workLine.Id, scan.ActualQuantity));
            movementIds.Add(movement.Id);
        }

        await inventoryLedgerService.RecordAsync(ledgerEntries, cancellationToken);
        return Result.Success(new WarehouseWorkHandlerResult(
            actualLines,
            "Reserved supplier-return stock moved to the return staging location.",
            movementIds));
    }

    private async Task<ResultError?> ValidateScanAsync(
        SupplierReturn supplierReturn,
        SupplierReturnLine returnLine,
        WarehouseWorkLine workLine,
        WarehouseWorkScanInput scan,
        WarehouseWorkCompletionInput input,
        CancellationToken cancellationToken)
    {
        if (scan.ItemId != returnLine.ItemId ||
            scan.SourceLocationId != returnLine.SourceLocationId ||
            scan.DestinationLocationId != supplierReturn.StagingLocationId ||
            scan.LicensePlateId != returnLine.LicensePlateId ||
            scan.LotId != returnLine.LotId ||
            scan.SerialNumberId != returnLine.SerialNumberId ||
            !string.Equals(scan.SerialNumber, returnLine.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return WmsErrors.Validation(
                "supplier_return.scan_dimension_mismatch",
                $"Scan for supplier-return line {returnLine.LineNumber} does not match the reserved source dimension.");
        }

        if (scan.ActualQuantity <= 0m || scan.ActualQuantity > returnLine.ApprovedBaseQuantity)
        {
            return WmsErrors.Validation(
                "supplier_return.scan_quantity_invalid",
                "Scanned supplier-return quantity must be positive and cannot exceed the approved quantity.");
        }

        if (scan.ActualQuantity < returnLine.ApprovedBaseQuantity &&
            (!input.SupervisorOverride || string.IsNullOrWhiteSpace(input.OverrideReason)))
        {
            return WmsErrors.BusinessRule(
                "supplier_return.partial_pick_override_required",
                "A short supplier-return pick requires a supervisor override reason.");
        }

        if (workLine.PlannedQuantity != returnLine.ApprovedBaseQuantity)
        {
            return WmsErrors.Dependency(
                "supplier_return.work_quantity_drift",
                "Supplier-return work no longer matches the approved return quantity.",
                isRetryable: false);
        }

        var destination = await context.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value => value.Id == supplierReturn.StagingLocationId,
                cancellationToken);
        return destination is null || !destination.IsActive || destination.WarehouseId != supplierReturn.WarehouseId ||
               destination.Type != LocationType.Staging
            ? WmsErrors.Validation(
                "supplier_return.staging_location_invalid",
                "The supplier-return staging location is no longer active or valid.")
            : null;
    }

    private Task<Stock?> FindStockAsync(
        int itemId,
        int locationId,
        SupplierReturnLine line,
        int inventoryStatusId,
        CancellationToken cancellationToken) =>
        context.Stock.SingleOrDefaultAsync(
            stock => stock.ItemId == itemId &&
                     stock.LocationId == locationId &&
                     stock.LotId == line.LotId &&
                     stock.SerialNumberId == line.SerialNumberId &&
                     stock.SerialNumber == line.SerialNumber &&
                     stock.InventoryStatusId == inventoryStatusId &&
                     stock.LicensePlateId == line.LicensePlateId,
            cancellationToken);
}
