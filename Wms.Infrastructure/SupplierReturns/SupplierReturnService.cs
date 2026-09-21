using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.SupplierReturns;
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

public sealed class SupplierReturnService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IInventoryLedgerService inventoryLedgerService,
    IWarehouseWorkService warehouseWorkService,
    IClock clock,
    ILogger<SupplierReturnService> logger) : ISupplierReturnService
{
    public async Task<Result<SupplierReturnDto>> CreateAsync(
        SupplierReturnCreateInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SupplierReturnsManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        try
        {
            if (input.Lines is null || input.Lines.Count == 0)
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                    "supplier_return.lines_required",
                    "A supplier return requires at least one source line."));
            }

            var existing = await LoadByNumberAsync(
                input.WarehouseId,
                input.ReturnNumber,
                cancellationToken);
            if (existing is not null)
            {
                return Result.Success(Map(existing));
            }

            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    value => value.Id == input.WarehouseId && value.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.NotFound(
                    "supplier_return.warehouse_not_found",
                    "The supplier-return warehouse was not found or is inactive."));
            }

            var supplier = await context.Suppliers
                .SingleOrDefaultAsync(
                    value => value.Id == input.SupplierId && value.IsActive,
                    cancellationToken);
            if (supplier is null)
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.NotFound(
                    "supplier_return.supplier_not_found",
                    "The supplier was not found or is inactive."));
            }

            var staging = await context.Locations
                .SingleOrDefaultAsync(
                    value => value.Id == input.StagingLocationId &&
                            value.WarehouseId == input.WarehouseId &&
                            value.IsActive,
                    cancellationToken);
            if (staging is null || staging.Type != LocationType.Staging)
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                    "supplier_return.staging_location_invalid",
                    "Supplier-return stock must use an active staging location in the selected warehouse."));
            }

            var supplierAuthorizationReference = input.SupplierAuthorizationReference?.Trim().ToUpperInvariant();
            if (supplierAuthorizationReference is not null &&
                await context.SupplierReturns.AnyAsync(
                    value => value.WarehouseId == input.WarehouseId &&
                            value.SupplierId == input.SupplierId &&
                            value.SupplierAuthorizationReference == supplierAuthorizationReference,
                    cancellationToken))
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                    "supplier_return.authorization_duplicate",
                    "The supplier authorization or RMA reference is already used for this supplier and warehouse."));
            }

            var root = new SupplierReturn(
                input.ReturnNumber,
                input.WarehouseId,
                input.SupplierId,
                input.StagingLocationId,
                input.SourceType,
                input.Reason,
                userId,
                clock.UtcNow.UtcDateTime,
                input.PurchaseOrderId,
                input.AdvanceShippingNoticeId,
                input.ReceiptId,
                input.QualityInspectionId,
                input.SupplierAuthorizationReference,
                input.ExternalReference);

            await ValidateRootReferencesAsync(root, cancellationToken);
            var itemIds = input.Lines.Select(line => line.ItemId).Distinct().ToArray();
            var items = await context.Items
                .Where(item => itemIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            if (items.Count != itemIds.Length || items.Values.Any(item => !item.IsActive))
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.NotFound(
                    "supplier_return.item_not_found",
                    "Every supplier-return line must reference an active item."));
            }

            var lineNumber = 1;
            foreach (var inputLine in input.Lines)
            {
                if (!items.TryGetValue(inputLine.ItemId, out var item))
                {
                    return Result.Failure<SupplierReturnDto>(WmsErrors.NotFound(
                        "supplier_return.item_not_found",
                        $"Item {inputLine.ItemId} was not found."));
                }

                if (!string.Equals(
                        inputLine.BaseUnitOfMeasure.Trim(),
                        item.UnitOfMeasure,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                        "supplier_return.uom_mismatch",
                        $"Return line {lineNumber} must use item base unit '{item.UnitOfMeasure}'."));
                }

                if (inputLine.RequestedBaseQuantity <= 0m)
                {
                    return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                        "supplier_return.quantity_invalid",
                        $"Return line {lineNumber} quantity must be greater than zero."));
                }

                if (item.RequiresLot && !inputLine.LotId.HasValue)
                {
                    return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                        "supplier_return.lot_required",
                        $"Return line {lineNumber} must identify a lot for item '{item.Sku}'."));
                }

                if (item.RequiresSerial && !inputLine.SerialNumberId.HasValue &&
                    string.IsNullOrWhiteSpace(inputLine.SerialNumber))
                {
                    return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                        "supplier_return.serial_required",
                        $"Return line {lineNumber} must identify a serial for item '{item.Sku}'."));
                }

                var line = new SupplierReturnLine(
                    lineNumber,
                    input.WarehouseId,
                    item.Id,
                    item.Sku,
                    item.Name,
                    inputLine.RequestedBaseQuantity,
                    item.UnitOfMeasure,
                    inputLine.SourceLocationId,
                    inputLine.InventoryStatusId,
                    inputLine.Reason,
                    inputLine.LotId,
                    inputLine.SerialNumberId,
                    inputLine.SerialNumber,
                    inputLine.LicensePlateId,
                    inputLine.PurchaseOrderLineId,
                    inputLine.AdvanceShippingNoticeLineId,
                    inputLine.ReceiptLineId,
                    inputLine.QualityInspectionId,
                    inputLine.QualityInspectionDispositionId,
                    inputLine.SourceReference);
                root.AddLine(line);
                lineNumber++;
            }

            context.SupplierReturns.Add(root);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnCreated,
                    WmsAuditEntityTypes.SupplierReturn,
                    root.ReturnNumber,
                    root.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["supplierId"] = root.SupplierId,
                        ["sourceType"] = root.SourceType,
                        ["lineCount"] = root.Lines.Count,
                        ["supplierAuthorizationReference"] = root.SupplierAuthorizationReference
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(root));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                "supplier_return.invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            logger.LogError(exception, "Supplier return creation failed for {ReturnNumber}", input.ReturnNumber);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                "supplier_return.create_conflict",
                "The supplier return conflicts with another document."));
        }
    }

    public async Task<Result<SupplierReturnPageDto>> ListAsync(
        SupplierReturnQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SupplierReturnsRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnPageDto>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var returnsQuery = context.SupplierReturns
                .AsNoTracking()
                .Include(value => value.Lines)
                .AsQueryable();
            if (!scope.HasGlobalAccess)
            {
                returnsQuery = returnsQuery.Where(value => scope.WarehouseIds.Contains(value.WarehouseId));
            }

            if (query.WarehouseId.HasValue)
            {
                returnsQuery = returnsQuery.Where(value => value.WarehouseId == query.WarehouseId.Value);
            }

            if (query.SupplierId.HasValue)
            {
                returnsQuery = returnsQuery.Where(value => value.SupplierId == query.SupplierId.Value);
            }

            if (query.Status.HasValue)
            {
                returnsQuery = returnsQuery.Where(value => value.Status == query.Status.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.Trim();
                returnsQuery = returnsQuery.Where(value =>
                    value.ReturnNumber.Contains(term) ||
                    (value.SupplierAuthorizationReference != null &&
                     value.SupplierAuthorizationReference.Contains(term)) ||
                    (value.ExternalReference != null && value.ExternalReference.Contains(term)));
            }

            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);
            var totalCount = await returnsQuery.CountAsync(cancellationToken);
            var returns = await returnsQuery
                .OrderByDescending(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return Result.Success(new SupplierReturnPageDto(
                returns.Select(Map).ToArray(),
                page,
                pageSize,
                totalCount,
                totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Supplier return list failed");
            return Result.Failure<SupplierReturnPageDto>(WmsErrors.FromException(
                exception,
                "supplier_return.list_failed",
                "Supplier returns could not be loaded."));
        }
    }

    public async Task<Result<SupplierReturnDto>> GetAsync(
        int supplierReturnId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(supplierReturnId, cancellationToken);
        if (loaded is null)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.NotFound(
                "supplier_return.not_found",
                "The supplier return was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SupplierReturnsRead,
            loaded.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<SupplierReturnDto>()
            : Result.Success(Map(loaded));
    }

    public async Task<Result<SupplierReturnDto>> ApproveAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(
            loaded,
            WmsPermissions.SupplierReturnsManage,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        if (input.SupervisorOverride)
        {
            if (string.IsNullOrWhiteSpace(input.OverrideReason))
            {
                return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                    "supplier_return.override_reason_required",
                    "A supervisor override reason is required."));
            }

            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReceivingOverride,
                loaded!.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<SupplierReturnDto>();
            }
        }

        var replay = await TryReplayAsync(loaded!, "approve", input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.Status != SupplierReturnStatus.Draft)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.approve_state",
                $"A supplier return in {loaded.Status} cannot be approved."));
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var ledgerEntries = new List<InventoryLedgerEntryRequest>();
            var sequence = 1;
            foreach (var line in loaded.Lines.OrderBy(value => value.LineNumber))
            {
                await ValidateLineReferencesAsync(loaded, line, input.SupervisorOverride, cancellationToken);
                var stock = await FindSourceStockAsync(line, cancellationToken);
                if (stock is null)
                {
                    throw new InvalidOperationException(
                        $"No stock balance matches supplier-return line {line.LineNumber}'s complete source dimension.");
                }

                var available = stock.GetAvailableQuantity().Value;
                if (available < line.RequestedBaseQuantity)
                {
                    throw new InvalidOperationException(
                        $"Supplier-return line {line.LineNumber} requests {line.RequestedBaseQuantity} but only {available} unreserved units are eligible.");
                }

                stock.ReserveQuantity(new Quantity(line.RequestedBaseQuantity));
                await unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
                line.ApproveAndReserve();
                ledgerEntries.Add(new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Reservation,
                    new InventoryBalanceKey(
                        loaded.WarehouseId,
                        line.SourceLocationId,
                        line.ItemId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        line.InventoryStatusId,
                        line.BaseUnitOfMeasure),
                    0m,
                    line.RequestedBaseQuantity,
                    ReferenceType: WmsAuditEntityTypes.SupplierReturn,
                    ReferenceId: loaded.ReturnNumber,
                    ReferenceLine: line.LineNumber,
                    Reason: line.Reason,
                    ActorUserId: userId,
                    OccurredAtUtc: clock.UtcNow.UtcDateTime,
                    IdempotencyKey: $"supplier-return:{loaded.Id}:approve:{sequence}",
                    TransactionGroupId: $"supplier-return:{loaded.Id}:approve",
                    EntrySequence: sequence++));
            }

            await inventoryLedgerService.RecordAsync(ledgerEntries, cancellationToken);
            loaded.Approve(userId, clock.UtcNow.UtcDateTime);
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                "approve",
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnApproved,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["requestedQuantity"] = loaded.RequestedQuantity,
                        ["supervisorOverride"] = input.SupervisorOverride,
                        ["overrideReason"] = input.OverrideReason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(loaded));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Concurrency(
                "supplier_return.concurrency",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                "supplier_return.approve_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.approve_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            logger.LogError(exception, "Supplier return approval failed for {ReturnId}", input.SupplierReturnId);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                "supplier_return.approve_conflict",
                "The supplier return was changed by another operation."));
        }
    }

    public async Task<Result<SupplierReturnDto>> ReleaseAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(
            loaded,
            WmsPermissions.SupplierReturnsManage,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        var replay = await TryReplayAsync(loaded!, "release", input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.Status != SupplierReturnStatus.Approved)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.release_state",
                $"A supplier return in {loaded.Status} cannot be released to picking."));
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var workResult = await warehouseWorkService.CreateAsync(
                new WarehouseWorkInput(
                    $"supplier-return:{loaded.Id}",
                    WarehouseWorkType.Return,
                    loaded.WarehouseId,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.Id.ToString(CultureInfo.InvariantCulture),
                    Priority: 70,
                    QueueCode: "RTV",
                    Notes: loaded.Reason,
                    MakeAvailable: true,
                    Lines: loaded.Lines.OrderBy(value => value.LineNumber)
                        .Select(line => new WarehouseWorkLineInput(
                            line.LineNumber,
                            loaded.WarehouseId,
                            line.ItemId,
                            line.ApprovedBaseQuantity,
                            line.BaseUnitOfMeasure,
                            line.SourceLocationId,
                            loaded.StagingLocationId,
                            line.LotId,
                            line.SerialNumberId,
                            line.SerialNumber,
                            line.LicensePlateId,
                            line.InventoryStatusId,
                            line.Id.ToString(CultureInfo.InvariantCulture),
                            $"supplierReturnId={loaded.Id};supplierReturnLineId={line.Id}"))
                        .ToArray()),
                userId,
                cancellationToken);
            if (workResult.IsFailure)
            {
                await RollbackIfNeededAsync(transactionStarted);
                return workResult.ToFailure<SupplierReturnDto>();
            }

            loaded.Release(workResult.Value.Id, userId, clock.UtcNow.UtcDateTime);
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                "release",
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnReleased,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["warehouseWorkId"] = loaded.WarehouseWorkId,
                        ["queue"] = "RTV"
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(loaded));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            logger.LogError(exception, "Supplier return release failed for {ReturnId}", input.SupplierReturnId);
            return Result.Failure<SupplierReturnDto>(WmsErrors.FromException(
                exception,
                "supplier_return.release_failed",
                "The supplier return could not be released."));
        }
    }

    public Task<Result<SupplierReturnDto>> PackAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteSimpleCommandAsync(
            input,
            "pack",
            WmsPermissions.SupplierReturnsManage,
            SupplierReturnStatus.Picking,
            (value, actor, now) => value.MarkPacked(actor, now),
            WmsAuditActions.SupplierReturnPacked,
            userId,
            cancellationToken);

    public async Task<Result<SupplierReturnDto>> ShipAsync(
        SupplierReturnShipInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(
            loaded,
            WmsPermissions.ShippingExecute,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        var replay = await TryReplayAsync(loaded!, "ship", input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.Status != SupplierReturnStatus.Packed)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.ship_state",
                $"A supplier return in {loaded.Status} cannot be shipped."));
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            var sequence = 1;
            var ledgerEntries = new List<InventoryLedgerEntryRequest>();
            foreach (var line in loaded.Lines.OrderBy(value => value.LineNumber))
            {
                var quantity = line.RemainingToShip;
                if (quantity <= 0m)
                {
                    continue;
                }

                var stock = await FindStagingStockAsync(loaded, line, cancellationToken)
                    ?? throw new InvalidOperationException(
                        $"Staged stock for supplier-return line {line.LineNumber} was not found.");
                if (stock.GetAvailableQuantity().Value < quantity)
                {
                    throw new InvalidOperationException(
                        $"Staged stock for supplier-return line {line.LineNumber} is no longer available.");
                }

                stock.RemoveQuantity(new Quantity(quantity));
                await unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
                var movement = Movement.CreateShip(
                    line.ItemId,
                    loaded.StagingLocationId,
                    new Quantity(quantity),
                    userId,
                    line.LotId,
                    line.SerialNumber,
                    loaded.ReturnNumber,
                    "supplier return shipment",
                    clock.UtcNow.UtcDateTime,
                    line.SerialNumberId,
                    InventoryStatusSystemIds.ReturnPending,
                    line.LicensePlateId);
                context.Movements.Add(movement);
                var serial = line.SerialNumberId.HasValue
                    ? await context.SerialNumbers.SingleOrDefaultAsync(
                        value => value.Id == line.SerialNumberId.Value,
                        cancellationToken)
                    : null;
                serial?.RecordShipment(loaded.ReturnNumber, clock.UtcNow.UtcDateTime);
                line.RecordShipped(quantity);
                ledgerEntries.Add(new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Ship,
                    new InventoryBalanceKey(
                        loaded.WarehouseId,
                        loaded.StagingLocationId,
                        line.ItemId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        InventoryStatusSystemIds.ReturnPending,
                        line.BaseUnitOfMeasure),
                    -quantity,
                    ReferenceType: WmsAuditEntityTypes.SupplierReturn,
                    ReferenceId: loaded.ReturnNumber,
                    ReferenceLine: line.LineNumber,
                    Reason: "supplier return shipment",
                    ActorUserId: userId,
                    OccurredAtUtc: movement.Timestamp,
                    IdempotencyKey: $"supplier-return:{loaded.Id}:ship:{sequence}",
                    TransactionGroupId: $"supplier-return:{loaded.Id}:ship",
                    EntrySequence: sequence++,
                    MovementId: movement.Id > 0 ? movement.Id : null));
            }

            await inventoryLedgerService.RecordAsync(ledgerEntries, cancellationToken);
            loaded.MarkShipped(
                userId,
                input.CarrierCode,
                input.TrackingNumber,
                input.ShippingDocumentReference,
                clock.UtcNow.UtcDateTime);
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                "ship",
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnShipped,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["carrierCode"] = loaded.CarrierCode,
                        ["trackingNumber"] = loaded.TrackingNumber,
                        ["shippingDocumentReference"] = loaded.ShippingDocumentReference,
                        ["shippedQuantity"] = loaded.ShippedQuantity
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(loaded));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Concurrency(
                "supplier_return.ship_concurrency",
                exception.Message));
        }
        catch (Exception exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            logger.LogError(exception, "Supplier return shipment failed for {ReturnId}", input.SupplierReturnId);
            return Result.Failure<SupplierReturnDto>(WmsErrors.FromException(
                exception,
                "supplier_return.ship_failed",
                "The supplier return could not be shipped."));
        }
    }

    public Task<Result<SupplierReturnDto>> AcknowledgeAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteSimpleCommandAsync(
            input,
            "acknowledge",
            WmsPermissions.SupplierReturnsManage,
            SupplierReturnStatus.Shipped,
            (value, actor, now) => value.Acknowledge(actor, now),
            WmsAuditActions.SupplierReturnAcknowledged,
            userId,
            cancellationToken);

    public Task<Result<SupplierReturnDto>> CloseAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteSimpleCommandAsync(
            input,
            "close",
            WmsPermissions.SupplierReturnsManage,
            SupplierReturnStatus.Acknowledged,
            (value, actor, now) => value.Close(actor, now),
            WmsAuditActions.SupplierReturnClosed,
            userId,
            cancellationToken);

    public async Task<Result<SupplierReturnDto>> ExceptionAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(
            loaded,
            WmsPermissions.SupplierReturnsManage,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                "supplier_return.exception_reason_required",
                "A supplier-return exception reason is required."));
        }

        var replay = await TryReplayAsync(loaded!, "exception", input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.IsTerminal || loaded.Status == SupplierReturnStatus.Exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.exception_state",
                $"A supplier return in {loaded.Status} cannot be moved to exception."));
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            loaded.MarkException(userId, input.Reason, clock.UtcNow.UtcDateTime);
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                "exception",
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnException,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["reason"] = input.Reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(loaded));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Concurrency(
                "supplier_return.exception_concurrency",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                "supplier_return.exception_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.exception_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            await RollbackIfNeededAsync(transactionStarted);
            logger.LogError(exception, "Supplier return exception transition failed for {ReturnId}", input.SupplierReturnId);
            return Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                "supplier_return.exception_conflict",
                "The supplier return was changed by another operation."));
        }
    }

    public async Task<Result<SupplierReturnDto>> CancelAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(
            loaded,
            WmsPermissions.SupplierReturnsManage,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        var replay = await TryReplayAsync(loaded!, "cancel", input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.Status is not (SupplierReturnStatus.Draft or SupplierReturnStatus.Approved))
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                "supplier_return.cancel_state",
                $"A supplier return in {loaded.Status} cannot be cancelled after release."));
        }

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;
            if (loaded.Status == SupplierReturnStatus.Approved)
            {
                var sequence = 1;
                var entries = new List<InventoryLedgerEntryRequest>();
                foreach (var line in loaded.Lines.Where(value => value.ReservedBaseQuantity > 0m))
                {
                    var stock = await FindSourceStockAsync(line, cancellationToken)
                        ?? throw new InvalidOperationException(
                            $"Reserved source stock for supplier-return line {line.LineNumber} was not found.");
                    stock.ReleaseReservation(new Quantity(line.ReservedBaseQuantity));
                    await unitOfWork.Stock.UpdateAsync(stock, cancellationToken);
                    entries.Add(new InventoryLedgerEntryRequest(
                        InventoryTransactionType.Release,
                        new InventoryBalanceKey(
                            loaded.WarehouseId,
                            line.SourceLocationId,
                            line.ItemId,
                            line.LotId,
                            line.SerialNumberId,
                            line.SerialNumber,
                            line.LicensePlateId,
                            line.InventoryStatusId,
                            line.BaseUnitOfMeasure),
                        0m,
                        -line.ReservedBaseQuantity,
                        ReferenceType: WmsAuditEntityTypes.SupplierReturn,
                        ReferenceId: loaded.ReturnNumber,
                        ReferenceLine: line.LineNumber,
                        Reason: input.Reason ?? "supplier return cancelled",
                        ActorUserId: userId,
                        OccurredAtUtc: clock.UtcNow.UtcDateTime,
                        IdempotencyKey: $"supplier-return:{loaded.Id}:cancel:{sequence}",
                        TransactionGroupId: $"supplier-return:{loaded.Id}:cancel",
                        EntrySequence: sequence++));
                }

                if (entries.Count > 0)
                {
                    await inventoryLedgerService.RecordAsync(entries, cancellationToken);
                }
            }

            loaded.Cancel(
                userId,
                clock.UtcNow.UtcDateTime,
                input.Reason ?? "supplier return cancelled");
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                "cancel",
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierReturnCancelled,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["reason"] = input.Reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(loaded));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackIfNeededAsync(transactionStarted);
            logger.LogError(exception, "Supplier return cancellation failed for {ReturnId}", input.SupplierReturnId);
            return Result.Failure<SupplierReturnDto>(WmsErrors.FromException(
                exception,
                "supplier_return.cancel_failed",
                "The supplier return could not be cancelled."));
        }
    }

    private async Task<Result<SupplierReturnDto>> ExecuteSimpleCommandAsync(
        SupplierReturnCommandInput input,
        string operation,
        string permission,
        SupplierReturnStatus expectedStatus,
        Action<SupplierReturn, string, DateTime> transition,
        string auditAction,
        string userId,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(input.SupplierReturnId, cancellationToken);
        var authorization = await AuthorizeLoadedAsync(loaded, permission, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierReturnDto>();
        }

        var replay = await TryReplayAsync(loaded!, operation, input, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        if (loaded!.Status != expectedStatus)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                $"supplier_return.{operation}_state",
                $"A supplier return in {loaded.Status} cannot be {operation}d."));
        }

        try
        {
            transition(loaded, userId, clock.UtcNow.UtcDateTime);
            context.SupplierReturnCommands.Add(new SupplierReturnCommand(
                loaded.Id,
                operation,
                input.IdempotencyKey,
                Hash(input),
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.SupplierReturn,
                    loaded.ReturnNumber,
                    loaded.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Status.ToString(),
                        ["reason"] = input.Reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(loaded));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.Validation(
                $"supplier_return.{operation}_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.BusinessRule(
                $"supplier_return.{operation}_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                $"supplier_return.{operation}_conflict",
                exception.Message));
        }
    }

    private async Task<Result> AuthorizeLoadedAsync(
        SupplierReturn? loaded,
        string permission,
        CancellationToken cancellationToken)
    {
        if (loaded is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "supplier_return.not_found",
                "The supplier return was not found."));
        }

        return await warehouseAccessService.AuthorizeAsync(
            permission,
            loaded.WarehouseId,
            cancellationToken);
    }

    private async Task<Result<SupplierReturnDto>?> TryReplayAsync<TInput>(
        SupplierReturn loaded,
        string operation,
        TInput input,
        CancellationToken cancellationToken)
    {
        var command = await context.SupplierReturnCommands
            .SingleOrDefaultAsync(
                value => value.SupplierReturnId == loaded.Id &&
                         value.Operation == operation &&
                         value.IdempotencyKey == GetIdempotencyKey(input),
                cancellationToken);
        if (command is null)
        {
            return null;
        }

        return command.RequestHash == Hash(input)
            ? Result.Success(Map(loaded))
            : Result.Failure<SupplierReturnDto>(WmsErrors.Conflict(
                "supplier_return.idempotency_conflict",
                "The idempotency key was already used with a different supplier-return request."));
    }

    private async Task ValidateRootReferencesAsync(
        SupplierReturn root,
        CancellationToken cancellationToken)
    {
        if (root.PurchaseOrderId.HasValue && !await context.PurchaseOrders.AnyAsync(
                value => value.Id == root.PurchaseOrderId.Value &&
                         value.WarehouseId == root.WarehouseId &&
                         value.SupplierId == root.SupplierId,
                cancellationToken))
        {
            throw new InvalidOperationException("The referenced purchase order does not belong to the supplier and warehouse.");
        }

        if (root.AdvanceShippingNoticeId.HasValue && !await context.AdvanceShippingNotices.AnyAsync(
                value => value.Id == root.AdvanceShippingNoticeId.Value &&
                         value.WarehouseId == root.WarehouseId &&
                         value.SupplierId == root.SupplierId,
                cancellationToken))
        {
            throw new InvalidOperationException("The referenced ASN does not belong to the supplier and warehouse.");
        }

        if (root.ReceiptId.HasValue && !await context.Receipts.AnyAsync(
                value => value.Id == root.ReceiptId.Value &&
                         value.WarehouseId == root.WarehouseId &&
                         value.SupplierId == root.SupplierId,
                cancellationToken))
        {
            throw new InvalidOperationException("The referenced receipt does not belong to the supplier and warehouse.");
        }

        if (root.QualityInspectionId.HasValue && !await context.QualityInspections.AnyAsync(
                value => value.Id == root.QualityInspectionId.Value &&
                         value.WarehouseId == root.WarehouseId &&
                         value.SupplierId == root.SupplierId,
                cancellationToken))
        {
            throw new InvalidOperationException("The referenced quality inspection does not belong to the supplier and warehouse.");
        }
    }

    private async Task ValidateLineReferencesAsync(
        SupplierReturn root,
        SupplierReturnLine line,
        bool supervisorOverride,
        CancellationToken cancellationToken)
    {
        if (line.PurchaseOrderLineId.HasValue)
        {
            var purchaseOrderLine = await context.PurchaseOrderLines
                .Include(value => value.PurchaseOrder)
                .SingleOrDefaultAsync(value => value.Id == line.PurchaseOrderLineId.Value, cancellationToken);
            if (purchaseOrderLine is null ||
                purchaseOrderLine.ItemId != line.ItemId ||
                purchaseOrderLine.PurchaseOrder.WarehouseId != root.WarehouseId ||
                purchaseOrderLine.PurchaseOrder.SupplierId != root.SupplierId ||
                (root.PurchaseOrderId.HasValue && purchaseOrderLine.PurchaseOrderId != root.PurchaseOrderId.Value))
            {
                throw new InvalidOperationException(
                    $"Purchase-order lineage for supplier-return line {line.LineNumber} is invalid.");
            }
        }

        if (line.AdvanceShippingNoticeLineId.HasValue)
        {
            var asnLine = await context.AdvanceShippingNoticeLines
                .Include(value => value.AdvanceShippingNotice)
                .SingleOrDefaultAsync(value => value.Id == line.AdvanceShippingNoticeLineId.Value, cancellationToken);
            if (asnLine is null ||
                asnLine.ItemId != line.ItemId ||
                asnLine.AdvanceShippingNotice.WarehouseId != root.WarehouseId ||
                asnLine.AdvanceShippingNotice.SupplierId != root.SupplierId ||
                (root.AdvanceShippingNoticeId.HasValue &&
                 asnLine.AdvanceShippingNoticeId != root.AdvanceShippingNoticeId.Value))
            {
                throw new InvalidOperationException(
                    $"ASN lineage for supplier-return line {line.LineNumber} is invalid.");
            }
        }

        if (line.ReceiptLineId.HasValue)
        {
            var receiptLine = await context.ReceiptLines
                .Include(value => value.Receipt)
                .SingleOrDefaultAsync(value => value.Id == line.ReceiptLineId.Value, cancellationToken);
            if (receiptLine is null ||
                receiptLine.ItemId != line.ItemId ||
                receiptLine.Receipt.WarehouseId != root.WarehouseId ||
                receiptLine.Receipt.SupplierId != root.SupplierId ||
                (root.ReceiptId.HasValue && receiptLine.ReceiptId != root.ReceiptId.Value))
            {
                throw new InvalidOperationException(
                    $"Receipt lineage for supplier-return line {line.LineNumber} is invalid.");
            }

            if (!supervisorOverride &&
                line.RequestedBaseQuantity > receiptLine.ReceivedBaseQuantity)
            {
                throw new InvalidOperationException(
                    $"Supplier-return line {line.LineNumber} exceeds the quantity recorded on its receipt line.");
            }
        }

        if (line.QualityInspectionDispositionId.HasValue)
        {
            var disposition = await context.QualityInspectionDispositions
                .Include(value => value.Inspection)
                .SingleOrDefaultAsync(
                    value => value.Id == line.QualityInspectionDispositionId.Value,
                    cancellationToken);
            if (disposition is null ||
                disposition.QualityInspectionId != line.QualityInspectionId ||
                disposition.Inspection.WarehouseId != root.WarehouseId ||
                disposition.Inspection.ItemId != line.ItemId ||
                disposition.Inspection.SupplierId != root.SupplierId ||
                disposition.Type != QualityDispositionType.ReturnToVendor ||
                disposition.TargetInventoryStatusId != line.InventoryStatusId)
            {
                throw new InvalidOperationException(
                    $"Quality return-to-vendor disposition for supplier-return line {line.LineNumber} is invalid.");
            }

            if (!supervisorOverride && line.RequestedBaseQuantity > disposition.Quantity)
            {
                throw new InvalidOperationException(
                    $"Supplier-return line {line.LineNumber} exceeds its quality disposition quantity.");
            }
        }

        var sourceLocation = await context.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == line.SourceLocationId, cancellationToken);
        if (sourceLocation is null || sourceLocation.WarehouseId != root.WarehouseId || !sourceLocation.IsActive)
        {
            throw new InvalidOperationException(
                $"Source location for supplier-return line {line.LineNumber} is invalid.");
        }

        var status = await context.InventoryStatuses
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == line.InventoryStatusId, cancellationToken);
        if (status is null || !status.IsActive ||
            (status.WarehouseId.HasValue && status.WarehouseId != root.WarehouseId))
        {
            throw new InvalidOperationException(
                $"Inventory status for supplier-return line {line.LineNumber} is invalid.");
        }
    }

    private Task<Stock?> FindSourceStockAsync(
        SupplierReturnLine line,
        CancellationToken cancellationToken) =>
        context.Stock.SingleOrDefaultAsync(
            stock => stock.ItemId == line.ItemId &&
                     stock.LocationId == line.SourceLocationId &&
                     stock.LotId == line.LotId &&
                     stock.SerialNumberId == line.SerialNumberId &&
                     stock.SerialNumber == line.SerialNumber &&
                     stock.InventoryStatusId == line.InventoryStatusId &&
                     stock.LicensePlateId == line.LicensePlateId,
            cancellationToken);

    private Task<Stock?> FindStagingStockAsync(
        SupplierReturn root,
        SupplierReturnLine line,
        CancellationToken cancellationToken) =>
        context.Stock.SingleOrDefaultAsync(
            stock => stock.ItemId == line.ItemId &&
                     stock.LocationId == root.StagingLocationId &&
                     stock.LotId == line.LotId &&
                     stock.SerialNumberId == line.SerialNumberId &&
                     stock.SerialNumber == line.SerialNumber &&
                     stock.InventoryStatusId == InventoryStatusSystemIds.ReturnPending &&
                     stock.LicensePlateId == line.LicensePlateId,
            cancellationToken);

    private async Task<SupplierReturn?> LoadAsync(
        int supplierReturnId,
        CancellationToken cancellationToken) =>
        await context.SupplierReturns
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == supplierReturnId, cancellationToken);

    private async Task<SupplierReturn?> LoadByNumberAsync(
        int warehouseId,
        string returnNumber,
        CancellationToken cancellationToken) =>
        await context.SupplierReturns
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(
                value => value.WarehouseId == warehouseId && value.ReturnNumber == returnNumber.Trim(),
                cancellationToken);

    private async Task RollbackIfNeededAsync(bool transactionStarted)
    {
        if (transactionStarted)
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }

    private static string GetIdempotencyKey<TInput>(TInput input) => input switch
    {
        SupplierReturnCommandInput command => command.IdempotencyKey,
        SupplierReturnShipInput ship => ship.IdempotencyKey,
        _ => throw new InvalidOperationException("Unsupported supplier-return command input.")
    };

    private static string Hash<TInput>(TInput input)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static SupplierReturnDto Map(SupplierReturn value) => new(
        value.Id,
        value.ReturnNumber,
        value.WarehouseId,
        value.SupplierId,
        value.StagingLocationId,
        value.SourceType,
        value.Reason,
        value.SupplierAuthorizationReference,
        value.ExternalReference,
        value.PurchaseOrderId,
        value.AdvanceShippingNoticeId,
        value.ReceiptId,
        value.QualityInspectionId,
        value.WarehouseWorkId,
        value.Status,
        value.RequestedQuantity,
        value.StagedQuantity,
        value.ShippedQuantity,
        value.CarrierCode,
        value.TrackingNumber,
        value.ShippingDocumentReference,
        value.CreatedAtUtc,
        value.ApprovedAtUtc,
        value.ReleasedAtUtc,
        value.PackedAtUtc,
        value.ShippedAtUtc,
        value.AcknowledgedAtUtc,
        value.ClosedAtUtc,
        value.CancelledAtUtc,
        value.ExceptionReason,
        value.Revision,
        value.Lines.OrderBy(line => line.LineNumber).Select(Map).ToArray());

    private static SupplierReturnLineDto Map(SupplierReturnLine value) => new(
        value.Id,
        value.LineNumber,
        value.ItemId,
        value.ItemSkuSnapshot,
        value.ItemNameSnapshot,
        value.RequestedBaseQuantity,
        value.ApprovedBaseQuantity,
        value.ReservedBaseQuantity,
        value.StagedBaseQuantity,
        value.ShippedBaseQuantity,
        value.RemainingToStage,
        value.RemainingToShip,
        value.BaseUnitOfMeasure,
        value.SourceLocationId,
        value.InventoryStatusId,
        value.LotId,
        value.SerialNumberId,
        value.SerialNumber,
        value.LicensePlateId,
        value.PurchaseOrderLineId,
        value.AdvanceShippingNoticeLineId,
        value.ReceiptLineId,
        value.QualityInspectionId,
        value.QualityInspectionDispositionId,
        value.Reason,
        value.SourceReference,
        value.Revision);
}
