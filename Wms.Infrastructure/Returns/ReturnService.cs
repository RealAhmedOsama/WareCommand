using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Returns;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Returns;

public sealed class ReturnService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock,
    ILogger<ReturnService> logger) : IReturnService
{
    public async Task<Result<ReturnAuthorizationDto>> CreateAsync(
        ReturnAuthorizationInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReturnAuthorizationDto>();
        }

        var existing = await LoadByNumberAsync(input.RmaNumber, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                EnsureIdempotencyKey(input.IdempotencyKey);
                if (input.Lines.Count == 0)
                {
                    throw new ArgumentException("At least one return line is required.", nameof(input));
                }

                var location = await context.Locations.SingleOrDefaultAsync(
                    value => value.Id == input.ReturnLocationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The return location was not found.");
                if (location.WarehouseId != input.WarehouseId ||
                    location.Type is not (LocationType.Returns or LocationType.Quarantine))
                {
                    throw new InvalidOperationException(
                        "An RMA must use a Returns or Quarantine location in its warehouse.");
                }

                Shipment? shipment = null;
                if (input.ShipmentId.HasValue)
                {
                    shipment = await context.Shipments.SingleOrDefaultAsync(
                        value => value.Id == input.ShipmentId.Value,
                        cancellationToken)
                        ?? throw new InvalidOperationException("The source shipment was not found.");
                    if (shipment.WarehouseId != input.WarehouseId ||
                        shipment.Status is not (ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Closed))
                    {
                        throw new InvalidOperationException("Returns can only reference a completed shipment.");
                    }
                }
                else if (!input.Unplanned)
                {
                    throw new InvalidOperationException(
                        "A planned return must reference a shipped shipment; use the unplanned return policy explicitly.");
                }

                var shipmentLines = input.ShipmentId.HasValue
                    ? await context.ShipmentLines
                        .Where(value => value.ShipmentId == input.ShipmentId.Value)
                        .ToListAsync(cancellationToken)
                    : [];
                foreach (var lineInput in input.Lines)
                {
                    if (lineInput.ExpectedSerialNumberId.HasValue && lineInput.ExpectedQuantity != 1m)
                    {
                        throw new InvalidOperationException("A serial return line must authorize exactly one unit.");
                    }

                    if (shipment is not null)
                    {
                        var shipmentLine = ResolveShipmentLine(lineInput, shipmentLines);
                        if (shipmentLine is null || shipmentLine.ItemId != lineInput.ItemId)
                        {
                            throw new InvalidOperationException("The return line does not match shipped shipment history.");
                        }

                        if (lineInput.ExpectedQuantity > shipmentLine.Quantity)
                        {
                            throw new InvalidOperationException(
                                "The authorized return quantity cannot exceed the shipped quantity.");
                        }
                    }
                }

                var rma = new ReturnAuthorization(
                    input.RmaNumber,
                    input.WarehouseId,
                    input.CustomerId,
                    input.SalesOrderId,
                    input.ShipmentId,
                    input.PackageId,
                    input.ReturnLocationId,
                    input.Unplanned,
                    input.Reason,
                    userId,
                    clock.UtcNow.UtcDateTime);
                context.ReturnAuthorizations.Add(rma);
                await context.SaveChangesAsync(cancellationToken);

                foreach (var lineInput in input.Lines)
                {
                    var shipmentLine = shipment is null
                        ? null
                        : ResolveShipmentLine(lineInput, shipmentLines);
                    var line = new ReturnLine(
                        rma.Id,
                        lineInput.ItemId,
                        lineInput.ExpectedQuantity,
                        lineInput.SalesOrderLineId,
                        lineInput.ShipmentLineId ?? shipmentLine?.Id,
                        lineInput.ExpectedLotId,
                        lineInput.ExpectedSerialNumberId);
                    rma.AddLine(line);
                    context.ReturnLines.Add(line);
                }

                await context.SaveChangesAsync(cancellationToken);
                AddCommand(rma.Id, "create", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ReturnAudit(rma, "return.requested", userId, new Dictionary<string, object?>
                    {
                        ["lineCount"] = input.Lines.Count,
                        ["unplanned"] = input.Unplanned,
                        ["shipmentId"] = input.ShipmentId
                    }),
                    cancellationToken);
                return Map(rma);
            },
            "returns.create_failed",
            "The return authorization could not be created.",
            cancellationToken);
    }

    public async Task<Result<ReturnAuthorizationDto>> GetAsync(
        int returnAuthorizationId,
        CancellationToken cancellationToken = default)
    {
        var rma = await LoadAsync(returnAuthorizationId, cancellationToken);
        if (rma is null)
        {
            return Result.Failure<ReturnAuthorizationDto>(WmsErrors.NotFound(
                "returns.not_found",
                "The return authorization was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            rma.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<ReturnAuthorizationDto>()
            : Result.Success(Map(rma));
    }

    public Task<Result<ReturnAuthorizationDto>> AuthorizeAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input,
            "authorize",
            WmsPermissions.ReceivingExecute,
            async rma =>
            {
                rma.Authorize(userId, clock.UtcNow.UtcDateTime);
                await Task.CompletedTask;
            },
            userId,
            cancellationToken);

    public Task<Result<ReturnAuthorizationDto>> InspectAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input,
            "inspect",
            WmsPermissions.ReceivingExecute,
            async rma =>
            {
                rma.StartInspection();
                await Task.CompletedTask;
            },
            userId,
            cancellationToken);

    public async Task<Result<ReturnAuthorizationDto>> ReceiveAsync(
        ReturnReceiptInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var rma = await LoadAsync(input.ReturnAuthorizationId, cancellationToken);
        if (rma is null)
        {
            return Result.Failure<ReturnAuthorizationDto>(WmsErrors.NotFound(
                "returns.not_found",
                "The return authorization was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            rma.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReturnAuthorizationDto>();
        }

        var replay = await TryReplayAsync(rma, "receive", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadAsync(input.ReturnAuthorizationId, cancellationToken)
                    ?? throw new InvalidOperationException("The return authorization was not found.");
                if (current.Status is not (ReturnStatus.Authorized or ReturnStatus.Received or ReturnStatus.Inspecting))
                {
                    throw new InvalidOperationException($"An RMA in {current.Status} cannot receive goods.");
                }

                var line = current.Lines.SingleOrDefault(value => value.Id == input.ReturnLineId)
                    ?? throw new InvalidOperationException("The return line was not found.");
                var item = await context.Items.SingleAsync(value => value.Id == line.ItemId, cancellationToken);
                if (line.ExpectedLotId.HasValue && line.ExpectedLotId != input.LotId)
                {
                    throw new InvalidOperationException("The returned lot does not match the authorized shipment identity.");
                }

                var serial = input.SerialNumberId.HasValue
                    ? await context.SerialNumbers.SingleOrDefaultAsync(
                        value => value.Id == input.SerialNumberId.Value,
                        cancellationToken)
                    : null;
                if (item.RequiresSerial != input.SerialNumberId.HasValue ||
                    (input.SerialNumberId.HasValue && (input.Quantity != 1m || serial is null ||
                        serial.ItemId != item.Id ||
                        !string.Equals(serial.Number, input.SerialNumber, StringComparison.OrdinalIgnoreCase) ||
                        line.ExpectedSerialNumberId != serial.Id ||
                        serial.Status != SerialStatus.Shipped)))
                {
                    throw new InvalidOperationException("The returned serial identity does not match shipped history.");
                }

                if (input.InventoryStatusId != InventoryStatusSystemIds.ReturnPending)
                {
                    throw new InvalidOperationException(
                        "Customer returns must be received into the Return Pending inventory status.");
                }

                line.RecordReceipt(input.Quantity);
                var existingStock = await FindStockAsync(
                    item.Id,
                    current.ReturnLocationId,
                    input.LotId,
                    input.SerialNumberId,
                    serial?.Number ?? input.SerialNumber,
                    input.InventoryStatusId,
                    input.LicensePlateId,
                    cancellationToken);
                if (existingStock is null)
                {
                    existingStock = new Stock(
                        item.Id,
                        current.ReturnLocationId,
                        new Quantity(input.Quantity),
                        input.LotId,
                        serial?.Number ?? input.SerialNumber,
                        input.SerialNumberId,
                        input.InventoryStatusId,
                        input.LicensePlateId);
                    context.Stock.Add(existingStock);
                }
                else
                {
                    existingStock.AddQuantity(new Quantity(input.Quantity));
                }

                var movement = Movement.CreateReceipt(
                    item.Id,
                    current.ReturnLocationId,
                    new Quantity(input.Quantity),
                    userId,
                    input.LotId,
                    serial?.Number ?? input.SerialNumber,
                    current.RmaNumber,
                    "customer return received into pending disposition",
                    clock.UtcNow.UtcDateTime,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    input.LicensePlateId);
                context.Movements.Add(movement);
                await inventoryLedgerService.RecordAsync(
                    [
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.Return,
                            new InventoryBalanceKey(
                                current.WarehouseId,
                                current.ReturnLocationId,
                                item.Id,
                                input.LotId,
                                input.SerialNumberId,
                                serial?.Number ?? input.SerialNumber,
                                input.LicensePlateId,
                                input.InventoryStatusId,
                                item.UnitOfMeasure),
                            input.Quantity,
                            ActorUserId: userId,
                            ReferenceType: "ReturnAuthorization",
                            ReferenceId: current.RmaNumber,
                            ReferenceLine: line.Id,
                            Reason: "customer return receipt",
                            OccurredAtUtc: movement.Timestamp,
                            IdempotencyKey: $"return:{current.Id}:{input.IdempotencyKey}:receipt",
                            TransactionGroupId: $"return:{current.Id}:{input.IdempotencyKey}")
                    ],
                    cancellationToken);

                if (serial is not null)
                {
                    serial.SetStatus(SerialStatus.Returned, "customer return received", clock.UtcNow.UtcDateTime);
                    serial.RecordReceipt(
                        current.WarehouseId,
                        current.ReturnLocationId,
                        input.LotId,
                        current.RmaNumber,
                        quarantine: true,
                        clock.UtcNow.UtcDateTime,
                        input.LicensePlateId);
                }

                var receipt = new ReturnReceipt(
                    current.Id,
                    line.Id,
                    item.Id,
                    input.Quantity,
                    current.ReturnLocationId,
                    input.InventoryStatusId,
                    input.LotId,
                    input.SerialNumberId,
                    serial?.Number ?? input.SerialNumber,
                    input.LicensePlateId,
                    clock.UtcNow.UtcDateTime);
                current.AddReceipt(receipt);
                context.ReturnReceipts.Add(receipt);
                AddCommand(current.Id, "receive", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ReturnAudit(current, "return.received", userId, new Dictionary<string, object?>
                    {
                        ["returnLineId"] = line.Id,
                        ["quantity"] = input.Quantity,
                        ["inventoryStatusId"] = input.InventoryStatusId,
                        ["serialNumberId"] = input.SerialNumberId
                    }),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return Map(current);
            },
            "returns.receive_failed",
            "The return receipt could not be recorded.",
            cancellationToken);
    }

    public async Task<Result<ReturnAuthorizationDto>> DisposeAsync(
        ReturnDispositionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var rma = await LoadAsync(input.ReturnAuthorizationId, cancellationToken);
        if (rma is null)
        {
            return Result.Failure<ReturnAuthorizationDto>(WmsErrors.NotFound(
                "returns.not_found",
                "The return authorization was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            rma.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReturnAuthorizationDto>();
        }

        var replay = await TryReplayAsync(rma, "dispose", input.IdempotencyKey, Hash(input), cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadAsync(input.ReturnAuthorizationId, cancellationToken)
                    ?? throw new InvalidOperationException("The return authorization was not found.");
                if (current.Status is not (ReturnStatus.Received or ReturnStatus.Inspecting))
                {
                    throw new InvalidOperationException($"An RMA in {current.Status} cannot receive a disposition.");
                }

                var receipt = current.Receipts.SingleOrDefault(value => value.Id == input.ReturnReceiptId)
                    ?? throw new InvalidOperationException("The return receipt was not found.");
                var line = current.Lines.Single(value => value.Id == receipt.ReturnLineId);
                if (input.Quantity > receipt.RemainingToDispose)
                {
                    throw new InvalidOperationException("The disposition exceeds the received quantity.");
                }

                var sourceStock = await FindStockAsync(
                    receipt.ItemId,
                    receipt.ReturnLocationId,
                    receipt.LotId,
                    receipt.SerialNumberId,
                    receipt.SerialNumber,
                    receipt.InventoryStatusId,
                    receipt.LicensePlateId,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The pending return stock was not found.");
                if (sourceStock.GetAvailableQuantity().Value < input.Quantity)
                {
                    throw new InvalidOperationException("The pending return stock cannot satisfy the disposition.");
                }

                var item = await context.Items.SingleAsync(value => value.Id == receipt.ItemId, cancellationToken);
                var destinationRequired = input.Kind is not (ReturnDispositionKind.Scrap or ReturnDispositionKind.ReturnToVendor or ReturnDispositionKind.RejectToCustomer);
                if (destinationRequired && !input.DestinationLocationId.HasValue)
                {
                    throw new InvalidOperationException("This disposition requires a destination location.");
                }

                Location? destination = null;
                if (input.DestinationLocationId.HasValue)
                {
                    destination = await context.Locations.SingleOrDefaultAsync(
                        value => value.Id == input.DestinationLocationId.Value,
                        cancellationToken);
                    if (destination is null || destination.WarehouseId != current.WarehouseId)
                    {
                        throw new InvalidOperationException("The disposition destination is not in the return warehouse.");
                    }

                    ValidateDestination(input.Kind, destination.Type);
                }

                var quantity = new Quantity(input.Quantity);
                var movement = destination is not null
                    ? Movement.CreateTransfer(
                        item.Id,
                        sourceStock.LocationId,
                        destination.Id,
                        quantity,
                        userId,
                        receipt.LotId,
                        receipt.SerialNumber,
                        current.RmaNumber,
                        $"return disposition {input.Kind}",
                        clock.UtcNow.UtcDateTime,
                        receipt.SerialNumberId,
                        input.DestinationInventoryStatusId ?? StatusFor(input.Kind),
                        receipt.LicensePlateId,
                        receipt.LicensePlateId)
                    : Movement.CreateShip(
                        item.Id,
                        sourceStock.LocationId,
                        quantity,
                        userId,
                        receipt.LotId,
                        receipt.SerialNumber,
                        current.RmaNumber,
                        $"return disposition {input.Kind}",
                        clock.UtcNow.UtcDateTime,
                        receipt.SerialNumberId,
                        receipt.InventoryStatusId,
                        receipt.LicensePlateId);
                context.Movements.Add(movement);

                sourceStock.RemoveQuantity(quantity);
                if (sourceStock.QuantityAvailable.Value == 0m && sourceStock.QuantityReserved.Value == 0m)
                {
                    context.Stock.Remove(sourceStock);
                }

                var entries = new List<InventoryLedgerEntryRequest>
                {
                    new(
                        InventoryTransactionType.Return,
                        new InventoryBalanceKey(
                            current.WarehouseId,
                            sourceStock.LocationId,
                            item.Id,
                            sourceStock.LotId,
                            sourceStock.SerialNumberId,
                            sourceStock.SerialNumber,
                            sourceStock.LicensePlateId,
                            sourceStock.InventoryStatusId,
                            item.UnitOfMeasure),
                        -input.Quantity,
                        ActorUserId: userId,
                        ReferenceType: "ReturnAuthorization",
                        ReferenceId: current.RmaNumber,
                        ReferenceLine: line.Id,
                        Reason: $"return disposition {input.Kind}",
                        OccurredAtUtc: movement.Timestamp,
                        IdempotencyKey: $"return:{current.Id}:{input.IdempotencyKey}:source",
                        TransactionGroupId: $"return:{current.Id}:{input.IdempotencyKey}")
                };

                if (destination is not null)
                {
                    var targetStatus = input.DestinationInventoryStatusId ?? StatusFor(input.Kind);
                    var targetStock = await FindStockAsync(
                        item.Id,
                        destination.Id,
                        receipt.LotId,
                        receipt.SerialNumberId,
                        receipt.SerialNumber,
                        targetStatus,
                        receipt.LicensePlateId,
                        cancellationToken);
                    if (targetStock is null)
                    {
                        targetStock = new Stock(
                            item.Id,
                            destination.Id,
                            quantity,
                            receipt.LotId,
                            receipt.SerialNumber,
                            receipt.SerialNumberId,
                            targetStatus,
                            receipt.LicensePlateId);
                        context.Stock.Add(targetStock);
                    }
                    else
                    {
                        targetStock.AddQuantity(quantity);
                    }

                    entries.Add(new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Return,
                        new InventoryBalanceKey(
                            current.WarehouseId,
                            destination.Id,
                            item.Id,
                            receipt.LotId,
                            receipt.SerialNumberId,
                            receipt.SerialNumber,
                            receipt.LicensePlateId,
                            targetStatus,
                            item.UnitOfMeasure),
                        input.Quantity,
                        ActorUserId: userId,
                        ReferenceType: "ReturnAuthorization",
                        ReferenceId: current.RmaNumber,
                        ReferenceLine: line.Id,
                        Reason: $"return disposition {input.Kind}",
                        OccurredAtUtc: movement.Timestamp,
                        IdempotencyKey: $"return:{current.Id}:{input.IdempotencyKey}:destination",
                        TransactionGroupId: $"return:{current.Id}:{input.IdempotencyKey}",
                        EntrySequence: 2));
                }

                await inventoryLedgerService.RecordAsync(entries, cancellationToken);
                if (receipt.SerialNumberId.HasValue)
                {
                    var serial = await context.SerialNumbers.SingleAsync(
                        value => value.Id == receipt.SerialNumberId.Value,
                        cancellationToken);
                    var serialStatus = input.Kind switch
                    {
                        ReturnDispositionKind.RestockAvailable => SerialStatus.Available,
                        ReturnDispositionKind.RestockDamaged => SerialStatus.Damaged,
                        ReturnDispositionKind.Repair => SerialStatus.Quarantine,
                        ReturnDispositionKind.Scrap => SerialStatus.Scrapped,
                        _ => SerialStatus.Shipped
                    };
                    serial.SetStatus(serialStatus, input.Reason, clock.UtcNow.UtcDateTime);
                    if (input.Kind is not (ReturnDispositionKind.Scrap or
                        ReturnDispositionKind.ReturnToVendor or
                        ReturnDispositionKind.RejectToCustomer) && destination is not null)
                    {
                        serial.MoveTo(
                            current.WarehouseId,
                            destination.Id,
                            null,
                            clock.UtcNow.UtcDateTime,
                            receipt.LicensePlateId);
                    }
                }

                receipt.RecordDisposition(input.Quantity);
                line.RecordDisposition(input.Quantity);
                var disposition = new ReturnDisposition(
                    current.Id,
                    receipt.Id,
                    input.Kind,
                    input.Quantity,
                    input.DestinationLocationId,
                    input.DestinationInventoryStatusId ?? StatusFor(input.Kind),
                    input.Reason,
                    userId,
                    clock.UtcNow.UtcDateTime);
                current.AddDisposition(disposition);
                context.ReturnDispositions.Add(disposition);
                if (current.Lines.All(value => value.RemainingToDispose == 0m))
                {
                    current.MarkDisposed();
                }

                AddCommand(current.Id, "dispose", input.IdempotencyKey, Hash(input), userId);
                await auditWriter.RecordAsync(
                    ReturnAudit(current, "return.disposed", userId, new Dictionary<string, object?>
                    {
                        ["returnReceiptId"] = receipt.Id,
                        ["kind"] = input.Kind.ToString(),
                        ["quantity"] = input.Quantity,
                        ["destinationLocationId"] = input.DestinationLocationId
                    }),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                return Map(current);
            },
            "returns.dispose_failed",
            "The return disposition could not be recorded.",
            cancellationToken);
    }

    public Task<Result<ReturnAuthorizationDto>> CloseAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input,
            "close",
            WmsPermissions.ReceivingExecute,
            async rma =>
            {
                rma.Close(clock.UtcNow.UtcDateTime);
                await Task.CompletedTask;
            },
            userId,
            cancellationToken);

    public Task<Result<ReturnAuthorizationDto>> CancelAsync(
        ReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input,
            "cancel",
            WmsPermissions.ReceivingExecute,
            async rma =>
            {
                rma.Cancel();
                await Task.CompletedTask;
            },
            userId,
            cancellationToken);

    private async Task<Result<ReturnAuthorizationDto>> ExecuteCommandAsync(
        ReturnCommandInput input,
        string operation,
        string permission,
        Func<ReturnAuthorization, Task> mutation,
        string userId,
        CancellationToken cancellationToken)
    {
        var rma = await LoadAsync(input.ReturnAuthorizationId, cancellationToken);
        if (rma is null)
        {
            return Result.Failure<ReturnAuthorizationDto>(WmsErrors.NotFound(
                "returns.not_found",
                "The return authorization was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            rma.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReturnAuthorizationDto>();
        }

        var requestHash = Hash(input);
        var replay = await TryReplayAsync(rma, operation, input.IdempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadAsync(input.ReturnAuthorizationId, cancellationToken)
                    ?? throw new InvalidOperationException("The return authorization was not found.");
                await mutation(current);
                AddCommand(current.Id, operation, input.IdempotencyKey, requestHash, userId);
                await auditWriter.RecordAsync(
                    ReturnAudit(current, $"return.{operation}", userId, new Dictionary<string, object?>
                    {
                        ["reason"] = input.Reason
                    }),
                    cancellationToken);
                return Map(current);
            },
            $"returns.{operation}_failed",
            "The return authorization command could not be completed.",
            cancellationToken);
    }

    private async Task<Result<T>> ExecuteMutationAsync<T>(
        Func<Task<T>> mutation,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        var transactionStarted = true;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            var value = await mutation();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return Result.Failure<T>(WmsErrors.Concurrency("returns.concurrency_conflict", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<T>(WmsErrors.Validation(errorCode, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<T>(WmsErrors.BusinessRule(errorCode, exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Return operation failed with {ErrorCode}", errorCode);
            return Result.Failure<T>(WmsErrors.FromException(exception, errorCode, safeMessage));
        }
        finally
        {
            if (transactionStarted)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }
        }
    }

    private async Task<ReturnAuthorization?> LoadAsync(int id, CancellationToken cancellationToken) =>
        await context.ReturnAuthorizations
            .Include(value => value.Lines)
            .Include(value => value.Receipts)
            .Include(value => value.Dispositions)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    private async Task<ReturnAuthorization?> LoadByNumberAsync(string number, CancellationToken cancellationToken) =>
        await context.ReturnAuthorizations
            .Include(value => value.Lines)
            .Include(value => value.Receipts)
            .Include(value => value.Dispositions)
            .SingleOrDefaultAsync(value => value.RmaNumber == number.Trim(), cancellationToken);

    private async Task<Result<ReturnAuthorizationDto>?> TryReplayAsync(
        ReturnAuthorization rma,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<ReturnAuthorizationDto>(WmsErrors.Validation(
                "returns.idempotency_required",
                "Every return command requires an idempotency key."));
        }

        var command = await context.ReturnCommands.SingleOrDefaultAsync(
            value => value.ReturnAuthorizationId == rma.Id &&
                     value.Operation == operation &&
                     value.IdempotencyKey == idempotencyKey.Trim(),
            cancellationToken);
        if (command is null)
        {
            return null;
        }

        return command.RequestHash == requestHash
            ? Result.Success(Map(rma))
            : Result.Failure<ReturnAuthorizationDto>(WmsErrors.Conflict(
                "returns.idempotency_reuse",
                "The idempotency key was already used with a different request."));
    }

    private void AddCommand(int rmaId, string operation, string idempotencyKey, string requestHash, string userId) =>
        context.ReturnCommands.Add(new ReturnCommand(
            rmaId,
            operation,
            idempotencyKey,
            requestHash,
            userId,
            clock.UtcNow.UtcDateTime));

    private static ShipmentLine? ResolveShipmentLine(
        ReturnLineInput input,
        IReadOnlyCollection<ShipmentLine> shipmentLines) =>
        input.ShipmentLineId.HasValue
            ? shipmentLines.SingleOrDefault(value => value.Id == input.ShipmentLineId.Value)
            : input.SalesOrderLineId.HasValue
                ? shipmentLines.SingleOrDefault(value => value.SalesOrderLineId == input.SalesOrderLineId.Value)
                : shipmentLines.SingleOrDefault(value => value.ItemId == input.ItemId);

    private static async Task<Stock?> FindStockAsync(
        WmsDbContext context,
        int itemId,
        int locationId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int inventoryStatusId,
        int? licensePlateId,
        CancellationToken cancellationToken) =>
        await context.Stock.SingleOrDefaultAsync(
            value => value.ItemId == itemId &&
                     value.LocationId == locationId &&
                     value.LotId == lotId &&
                     value.SerialNumberId == serialNumberId &&
                     value.SerialNumber == serialNumber &&
                     value.InventoryStatusId == inventoryStatusId &&
                     value.LicensePlateId == licensePlateId,
            cancellationToken);

    private Task<Stock?> FindStockAsync(
        int itemId,
        int locationId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int inventoryStatusId,
        int? licensePlateId,
        CancellationToken cancellationToken) =>
        FindStockAsync(context, itemId, locationId, lotId, serialNumberId, serialNumber, inventoryStatusId, licensePlateId, cancellationToken);

    private static void ValidateDestination(ReturnDispositionKind kind, LocationType type)
    {
        var valid = kind switch
        {
            ReturnDispositionKind.RestockAvailable => type is LocationType.Storage or LocationType.PickFace or LocationType.Bin or LocationType.Bulk,
            ReturnDispositionKind.RestockDamaged => type == LocationType.Damaged,
            ReturnDispositionKind.Repair => type is LocationType.Quarantine or LocationType.Returns,
            _ => true
        };
        if (!valid)
        {
            throw new InvalidOperationException($"Location type '{type}' cannot receive disposition '{kind}'.");
        }
    }

    private static int StatusFor(ReturnDispositionKind kind) => kind switch
    {
        ReturnDispositionKind.RestockAvailable => InventoryStatusSystemIds.Available,
        ReturnDispositionKind.RestockDamaged => InventoryStatusSystemIds.Damaged,
        ReturnDispositionKind.Repair => InventoryStatusSystemIds.Quarantine,
        _ => InventoryStatusSystemIds.ScrapPending
    };

    private static AuditRecord ReturnAudit(
        ReturnAuthorization rma,
        string operation,
        string userId,
        IReadOnlyDictionary<string, object?> details) =>
        new(
            WmsAuditActions.ReturnProcessed,
            WmsAuditEntityTypes.Return,
            rma.RmaNumber,
            rma.WarehouseId,
            After: new Dictionary<string, object?>(details)
            {
                ["operation"] = operation,
                ["status"] = rma.Status.ToString()
            },
            ActorUserId: userId);

    private static string Hash<T>(T value)
    {
        var payload = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static void EnsureIdempotencyKey(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        }
    }

    private static ReturnAuthorizationDto Map(ReturnAuthorization rma) => new(
        rma.Id,
        rma.RmaNumber,
        rma.WarehouseId,
        rma.CustomerId,
        rma.SalesOrderId,
        rma.ShipmentId,
        rma.PackageId,
        rma.ReturnLocationId,
        rma.Unplanned,
        rma.Reason,
        rma.Status,
        rma.ExpectedQuantity,
        rma.ReceivedQuantity,
        rma.DisposedQuantity,
        rma.Lines.Select(line => new ReturnLineDto(
            line.Id,
            line.ItemId,
            line.ExpectedQuantity,
            line.ReceivedQuantity,
            line.DisposedQuantity,
            line.RemainingToReceive,
            line.RemainingToDispose,
            line.SalesOrderLineId,
            line.ShipmentLineId,
            line.ExpectedLotId,
            line.ExpectedSerialNumberId,
            line.Revision)).ToArray(),
        rma.Receipts.Select(receipt => new ReturnReceiptDto(
            receipt.Id,
            receipt.ReturnLineId,
            receipt.ItemId,
            receipt.Quantity,
            receipt.DisposedQuantity,
            receipt.RemainingToDispose,
            receipt.ReturnLocationId,
            receipt.InventoryStatusId,
            receipt.LotId,
            receipt.SerialNumberId,
            receipt.SerialNumber,
            receipt.LicensePlateId,
            receipt.Status,
            receipt.ReceivedAtUtc,
            receipt.Revision)).ToArray(),
        rma.Dispositions.Select(disposition => new ReturnDispositionDto(
            disposition.Id,
            disposition.ReturnReceiptId,
            disposition.Kind,
            disposition.Quantity,
            disposition.DestinationLocationId,
            disposition.DestinationInventoryStatusId,
            disposition.Reason,
            disposition.UserId,
            disposition.DisposedAtUtc)).ToArray(),
        rma.CreatedAtUtc,
        rma.AuthorizedAtUtc,
        rma.ReceivedAtUtc,
        rma.ClosedAtUtc,
        rma.Revision);
}
