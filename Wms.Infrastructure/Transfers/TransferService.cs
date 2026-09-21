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
using Wms.Application.Transfers;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Transfers;

public sealed class TransferService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IInventoryLedgerService inventoryLedgerService,
    IClock clock,
    ILogger<TransferService> logger) : ITransferService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<TransferOrderPageDto>> ListAsync(
        TransferQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.SourceWarehouseId ?? query.DestinationWarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<TransferOrderPageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var transfers = context.TransferOrders.AsNoTracking().Include(value => value.Lines).AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            transfers = transfers.Where(value =>
                scope.WarehouseIds.Contains(value.SourceWarehouseId) ||
                scope.WarehouseIds.Contains(value.DestinationWarehouseId));
        }

        if (query.SourceWarehouseId.HasValue)
        {
            transfers = transfers.Where(value => value.SourceWarehouseId == query.SourceWarehouseId.Value);
        }

        if (query.DestinationWarehouseId.HasValue)
        {
            transfers = transfers.Where(value => value.DestinationWarehouseId == query.DestinationWarehouseId.Value);
        }

        if (query.Status.HasValue)
        {
            transfers = transfers.Where(value => value.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            transfers = transfers.Where(value =>
                value.TransferNumber.Contains(term) ||
                (value.ExternalReference ?? string.Empty).Contains(term));
        }

        var totalCount = await transfers.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var rows = await transfers
            .OrderByDescending(value => value.Priority)
            .ThenByDescending(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new TransferOrderPageDto(
            rows.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<TransferOrderDto>> GetAsync(
        int transferOrderId,
        CancellationToken cancellationToken = default)
    {
        var transfer = await LoadAsync(transferOrderId, cancellationToken);
        if (transfer is null)
        {
            return Result.Failure<TransferOrderDto>(WmsErrors.NotFound(
                "transfer.not_found",
                "The transfer order was not found."));
        }

        var authorization = await AuthorizeBothWarehousesAsync(
            WmsPermissions.InventoryRead,
            transfer,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<TransferOrderDto>()
            : Result.Success(Map(transfer));
    }

    public async Task<Result<TransferOrderDto>> CreateAsync(
        TransferOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var authorization = await AuthorizeBothWarehousesAsync(
            WmsPermissions.InventoryAdjust,
            input.SourceWarehouseId,
            input.DestinationWarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<TransferOrderDto>();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure<TransferOrderDto>(WmsErrors.Validation(
                "transfer.actor_required",
                "An authenticated actor is required."));
        }

        if (string.IsNullOrWhiteSpace(input.CreationIdempotencyKey))
        {
            return Result.Failure<TransferOrderDto>(WmsErrors.Validation(
                "transfer.idempotency_required",
                "Every transfer creation requires an idempotency key."));
        }

        var requestHash = Hash(input);
        var existing = await context.TransferOrders
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(
                value => value.CreationIdempotencyKey == input.CreationIdempotencyKey.Trim(),
                cancellationToken);
        if (existing is not null)
        {
            return existing.CreationRequestHash == requestHash
                ? Result.Success(Map(existing))
                : Result.Failure<TransferOrderDto>(WmsErrors.Conflict(
                    "transfer.idempotency_conflict",
                    "The creation idempotency key was already used for another transfer."));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                await ValidateTransferReferencesAsync(input, cancellationToken);
                var transfer = new TransferOrder(
                    input.TransferNumber,
                    input.CreationIdempotencyKey,
                    requestHash,
                    input.SourceWarehouseId,
                    input.DestinationWarehouseId,
                    input.TransitLocationId,
                    userId,
                    clock.UtcNow.UtcDateTime,
                    input.Priority,
                    input.ExternalReference,
                    input.Notes);
                context.TransferOrders.Add(transfer);
                await context.SaveChangesAsync(cancellationToken);

                for (var index = 0; index < input.Lines.Count; index++)
                {
                    var line = input.Lines[index];
                    transfer.AddLine(new TransferOrderLine(
                        index + 1,
                        line.ItemId,
                        line.RequestedQuantity,
                        line.BaseUnitOfMeasure,
                        line.SourceLocationId,
                        line.DestinationLocationId,
                        line.LotId,
                        line.SerialNumberId,
                        line.SerialNumber,
                        line.LicensePlateId,
                        line.SourceInventoryStatusId,
                        line.DestinationInventoryStatusId,
                        line.Notes,
                        line.OwnerKind,
                        line.InventoryOwnerId,
                        line.OwnerCodeSnapshot));
                }

                await context.SaveChangesAsync(cancellationToken);

                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.TransferCreated,
                        WmsAuditEntityTypes.Transfer,
                        transfer.Id.ToString(CultureInfo.InvariantCulture),
                        transfer.SourceWarehouseId,
                        ActorUserId: userId,
                        After: new Dictionary<string, object?>
                        {
                            ["transferNumber"] = transfer.TransferNumber,
                            ["sourceWarehouseId"] = transfer.SourceWarehouseId,
                            ["destinationWarehouseId"] = transfer.DestinationWarehouseId,
                            ["lineCount"] = transfer.Lines.Count
                        }),
                    cancellationToken);
                return Map(transfer);
            },
            "transfer.create_failed",
            "The transfer order could not be created.",
            cancellationToken);
    }

    public Task<Result<TransferOrderDto>> ConfirmAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "confirm",
            input.IdempotencyKey,
            input,
            userId,
            transfer =>
            {
                transfer.Confirm(clock.UtcNow.UtcDateTime);
                return Task.CompletedTask;
            },
            WmsAuditActions.TransferConfirmed,
            cancellationToken);

    public Task<Result<TransferOrderDto>> ReleaseAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "release",
            input.IdempotencyKey,
            input,
            userId,
            transfer =>
            {
                transfer.Release(clock.UtcNow.UtcDateTime);
                return Task.CompletedTask;
            },
            WmsAuditActions.TransferReleased,
            cancellationToken);

    public Task<Result<TransferOrderDto>> ShipAsync(
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "ship",
            input.IdempotencyKey,
            input,
            userId,
            transfer => ShipLineAsync(transfer, input, userId, cancellationToken),
            WmsAuditActions.TransferShipped,
            cancellationToken);

    public Task<Result<TransferOrderDto>> ReceiveAsync(
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "receive",
            input.IdempotencyKey,
            input,
            userId,
            transfer => ReceiveLineAsync(transfer, input, userId, cancellationToken),
            WmsAuditActions.TransferReceived,
            cancellationToken);

    public Task<Result<TransferOrderDto>> CloseAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "close",
            input.IdempotencyKey,
            input,
            userId,
            transfer =>
            {
                transfer.Close(clock.UtcNow.UtcDateTime);
                return Task.CompletedTask;
            },
            WmsAuditActions.TransferClosed,
            cancellationToken);

    public Task<Result<TransferOrderDto>> CancelAsync(
        TransferCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            input.TransferOrderId,
            "cancel",
            input.IdempotencyKey,
            input,
            userId,
            transfer =>
            {
                transfer.Cancel(userId, input.Reason ?? "transfer cancelled", clock.UtcNow.UtcDateTime);
                return Task.CompletedTask;
            },
            WmsAuditActions.TransferCancelled,
            cancellationToken);

    public async Task<Result<InternalMovementDto>> MoveAsync(
        InternalMovementInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InternalMovementDto>();
        }

        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            return Result.Failure<InternalMovementDto>(WmsErrors.Validation(
                "internal_movement.idempotency_required",
                "Every internal movement requires an idempotency key."));
        }

        var requestHash = Hash(input);
        var existing = await context.InternalMovements.AsNoTracking().SingleOrDefaultAsync(
            value => value.WarehouseId == input.WarehouseId &&
                     value.IdempotencyKey == input.IdempotencyKey.Trim(),
            cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == requestHash
                ? Result.Success(Map(existing))
                : Result.Failure<InternalMovementDto>(WmsErrors.Conflict(
                    "internal_movement.idempotency_conflict",
                    "The internal movement idempotency key was already used for another request."));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var source = await context.Locations.SingleOrDefaultAsync(
                    value => value.Id == input.SourceLocationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The internal movement source location was not found.");
                var destination = await context.Locations.SingleOrDefaultAsync(
                    value => value.Id == input.DestinationLocationId,
                    cancellationToken)
                    ?? throw new InvalidOperationException("The internal movement destination location was not found.");
                if (source.WarehouseId != input.WarehouseId || destination.WarehouseId != input.WarehouseId)
                {
                    throw new InvalidOperationException("Internal movements must remain within one warehouse.");
                }

                var item = await context.Items.SingleOrDefaultAsync(value => value.Id == input.ItemId, cancellationToken)
                    ?? throw new InvalidOperationException("The internal movement item was not found.");
                EnsureUnit(item, input.BaseUnitOfMeasure);
                var identity = await ResolveIdentityAsync(
                    input.ItemId,
                    input.LotId,
                    input.SerialNumberId,
                    input.SerialNumber,
                    input.LicensePlateId,
                    cancellationToken);
                var sourceStock = await FindStockAsync(
                    input.ItemId,
                    input.SourceLocationId,
                    identity.LotId,
                    identity.SerialNumberId,
                    identity.SerialNumber,
                    input.InventoryStatusId,
                    input.LicensePlateId,
                    cancellationToken,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot)
                    ?? throw new InvalidOperationException("The source stock dimension was not found.");
                EnsureMovableStock(sourceStock, input.Quantity);
                var destinationStock = await FindStockAsync(
                    input.ItemId,
                    input.DestinationLocationId,
                    identity.LotId,
                    identity.SerialNumberId,
                    identity.SerialNumber,
                    input.InventoryStatusId,
                    input.LicensePlateId,
                    cancellationToken,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot);

                var movement = Movement.CreateTransfer(
                    input.ItemId,
                    input.SourceLocationId,
                    input.DestinationLocationId,
                    new Quantity(input.Quantity),
                    userId,
                    identity.LotId,
                    identity.SerialNumber,
                    input.IdempotencyKey,
                    input.Reason,
                    clock.UtcNow.UtcDateTime,
                    identity.SerialNumberId,
                    input.InventoryStatusId,
                    input.LicensePlateId,
                    input.LicensePlateId);
                movement.SetOwnership(input.OwnerKind, input.InventoryOwnerId, input.OwnerCodeSnapshot);
                context.Movements.Add(movement);
                MoveStock(sourceStock, destinationStock, input, identity);
                MoveSerial(identity.Serial, input.WarehouseId, destination.Id, input.LicensePlateId);
                await MoveLicensePlateIfWholeAsync(
                    input.LicensePlateId,
                    input.SourceLocationId,
                    input.DestinationLocationId,
                    input.Quantity,
                    input.WarehouseId,
                    cancellationToken);

                var operation = new InternalMovement(
                    input.IdempotencyKey,
                    requestHash,
                    input.WarehouseId,
                    input.ItemId,
                    input.Quantity,
                    item.UnitOfMeasure,
                    input.SourceLocationId,
                    input.DestinationLocationId,
                    userId,
                    clock.UtcNow.UtcDateTime,
                    identity.LotId,
                    identity.SerialNumberId,
                    identity.SerialNumber,
                    input.LicensePlateId,
                    input.InventoryStatusId,
                    input.Reason,
                    input.OwnerKind,
                    input.InventoryOwnerId,
                    input.OwnerCodeSnapshot);
                operation.Complete(clock.UtcNow.UtcDateTime);
                context.InternalMovements.Add(operation);
                await RecordTransferLedgerAsync(
                    InventoryTransactionType.Transfer,
                    input.WarehouseId,
                    input.SourceLocationId,
                    input.DestinationLocationId,
                    input.ItemId,
                    identity,
                    input.InventoryStatusId,
                    input.Quantity,
                    item.UnitOfMeasure,
                    "InternalMovement",
                    input.IdempotencyKey,
                    null,
                    userId,
                    cancellationToken,
                    idempotencyPrefix: $"internal:{input.WarehouseId}:{input.IdempotencyKey}",
                    ownerKind: input.OwnerKind,
                    inventoryOwnerId: input.InventoryOwnerId,
                    ownerCodeSnapshot: input.OwnerCodeSnapshot);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.InternalMovementCompleted,
                        WmsAuditEntityTypes.Transfer,
                        input.IdempotencyKey,
                        input.WarehouseId,
                        ActorUserId: userId,
                        After: new Dictionary<string, object?>
                        {
                            ["itemId"] = input.ItemId,
                            ["quantity"] = input.Quantity,
                            ["sourceLocationId"] = input.SourceLocationId,
                            ["destinationLocationId"] = input.DestinationLocationId
                        }),
                    cancellationToken);
                return Map(operation);
            },
            "internal_movement.failed",
            "The internal movement could not be completed.",
            cancellationToken);
    }

    private async Task ShipLineAsync(
        TransferOrder transfer,
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken)
    {
        var line = transfer.Lines.SingleOrDefault(value => value.Id == input.TransferLineId)
            ?? throw new InvalidOperationException("The transfer line was not found.");
        if (input.Quantity <= 0 || input.Quantity > line.RemainingToShip)
        {
            throw new InvalidOperationException("The shipment quantity must be positive and within the remaining transfer quantity.");
        }

        var source = await context.Locations.SingleAsync(value => value.Id == line.SourceLocationId, cancellationToken);
        var transit = await context.Locations.SingleAsync(value => value.Id == transfer.TransitLocationId, cancellationToken);
        if (source.WarehouseId != transfer.SourceWarehouseId ||
            transit.WarehouseId != transfer.SourceWarehouseId ||
            transit.Type != LocationType.Transit)
        {
            throw new InvalidOperationException("The transfer source and transit locations are invalid.");
        }

        var item = await context.Items.SingleAsync(value => value.Id == line.ItemId, cancellationToken);
        EnsureUnit(item, line.BaseUnitOfMeasure);
        var identity = await ResolveIdentityAsync(
            line.ItemId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            cancellationToken);
        var sourceStock = await FindStockAsync(
            line.ItemId,
            line.SourceLocationId,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            line.SourceInventoryStatusId,
            line.LicensePlateId,
            cancellationToken,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot)
            ?? throw new InvalidOperationException("The source stock dimension was not found.");
        EnsureMovableStock(sourceStock, input.Quantity);
        var transitStock = await FindStockAsync(
            line.ItemId,
            transit.Id,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            InventoryStatusSystemIds.InTransit,
            line.LicensePlateId,
            cancellationToken,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot);

        var movement = Movement.CreateTransfer(
            line.ItemId,
            source.Id,
            transit.Id,
            new Quantity(input.Quantity),
            userId,
            identity.LotId,
            identity.SerialNumber,
            transfer.TransferNumber,
            input.Reason ?? "transfer shipped to transit",
            clock.UtcNow.UtcDateTime,
            identity.SerialNumberId,
            InventoryStatusSystemIds.InTransit,
            line.LicensePlateId,
            line.LicensePlateId);
        movement.SetOwnership(line.OwnerKind, line.InventoryOwnerId, line.OwnerCodeSnapshot);
        context.Movements.Add(movement);
        sourceStock.RemoveQuantity(new Quantity(input.Quantity));
        if (sourceStock.QuantityAvailable.Value == 0m && sourceStock.QuantityReserved.Value == 0m)
        {
            context.Stock.Remove(sourceStock);
        }

        if (transitStock is null)
        {
            context.Stock.Add(new Stock(
                line.ItemId,
                transit.Id,
                new Quantity(input.Quantity),
                identity.LotId,
                identity.SerialNumber,
                identity.SerialNumberId,
                InventoryStatusSystemIds.InTransit,
                line.LicensePlateId,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot));
        }
        else
        {
            transitStock.AddQuantity(new Quantity(input.Quantity));
        }

        MoveSerial(identity.Serial, transfer.SourceWarehouseId, transit.Id, line.LicensePlateId);
        await MoveLicensePlateIfWholeAsync(
            line.LicensePlateId,
            source.Id,
            transit.Id,
            input.Quantity,
            transfer.SourceWarehouseId,
            cancellationToken);
        await RecordTransferLedgerAsync(
            InventoryTransactionType.Transfer,
            transfer.SourceWarehouseId,
            source.Id,
            transit.Id,
            line.ItemId,
            identity,
            line.SourceInventoryStatusId,
            input.Quantity,
            item.UnitOfMeasure,
            "TransferOrder",
            transfer.TransferNumber,
            line.Id,
            userId,
            cancellationToken,
            destinationStatusId: InventoryStatusSystemIds.InTransit,
            idempotencyPrefix: $"transfer:{transfer.Id}:{input.IdempotencyKey}:ship",
            ownerKind: line.OwnerKind,
            inventoryOwnerId: line.InventoryOwnerId,
            ownerCodeSnapshot: line.OwnerCodeSnapshot);
        transfer.RecordShipment(line, input.Quantity, clock.UtcNow.UtcDateTime);
    }

    private async Task ReceiveLineAsync(
        TransferOrder transfer,
        TransferQuantityCommandInput input,
        string userId,
        CancellationToken cancellationToken)
    {
        var line = transfer.Lines.SingleOrDefault(value => value.Id == input.TransferLineId)
            ?? throw new InvalidOperationException("The transfer line was not found.");
        if (input.Quantity <= 0 || input.Quantity > line.RemainingToReceive)
        {
            throw new InvalidOperationException("The receipt quantity must be positive and within the remaining shipped quantity.");
        }

        var transit = await context.Locations.SingleAsync(value => value.Id == transfer.TransitLocationId, cancellationToken);
        var destination = await context.Locations.SingleAsync(value => value.Id == line.DestinationLocationId, cancellationToken);
        if (transit.WarehouseId != transfer.SourceWarehouseId ||
            transit.Type != LocationType.Transit ||
            destination.WarehouseId != transfer.DestinationWarehouseId)
        {
            throw new InvalidOperationException("The transfer transit and destination locations are invalid.");
        }

        var item = await context.Items.SingleAsync(value => value.Id == line.ItemId, cancellationToken);
        var identity = await ResolveIdentityAsync(
            line.ItemId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            cancellationToken);
        var transitStock = await FindStockAsync(
            line.ItemId,
            transit.Id,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            InventoryStatusSystemIds.InTransit,
            line.LicensePlateId,
            cancellationToken,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot)
            ?? throw new InvalidOperationException("The transfer quantity is not present in transit.");
        EnsureMovableStock(transitStock, input.Quantity);
        var destinationStock = await FindStockAsync(
            line.ItemId,
            destination.Id,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            line.DestinationInventoryStatusId,
            line.LicensePlateId,
            cancellationToken,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot);

        var movement = Movement.CreateTransfer(
            line.ItemId,
            transit.Id,
            destination.Id,
            new Quantity(input.Quantity),
            userId,
            identity.LotId,
            identity.SerialNumber,
            transfer.TransferNumber,
            input.Reason ?? "transfer received at destination",
            clock.UtcNow.UtcDateTime,
            identity.SerialNumberId,
            line.DestinationInventoryStatusId,
            line.LicensePlateId,
            line.LicensePlateId);
        movement.SetOwnership(line.OwnerKind, line.InventoryOwnerId, line.OwnerCodeSnapshot);
        context.Movements.Add(movement);
        transitStock.RemoveQuantity(new Quantity(input.Quantity));
        if (transitStock.QuantityAvailable.Value == 0m && transitStock.QuantityReserved.Value == 0m)
        {
            context.Stock.Remove(transitStock);
        }

        if (destinationStock is null)
        {
            context.Stock.Add(new Stock(
                line.ItemId,
                destination.Id,
                new Quantity(input.Quantity),
                identity.LotId,
                identity.SerialNumber,
                identity.SerialNumberId,
            line.DestinationInventoryStatusId,
                line.LicensePlateId,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot));
        }
        else
        {
            destinationStock.AddQuantity(new Quantity(input.Quantity));
        }

        MoveSerial(identity.Serial, transfer.DestinationWarehouseId, destination.Id, line.LicensePlateId);
        await MoveLicensePlateIfWholeAsync(
            line.LicensePlateId,
            transit.Id,
            destination.Id,
            input.Quantity,
            transfer.DestinationWarehouseId,
            cancellationToken);
        await RecordTransferLedgerAsync(
            InventoryTransactionType.Transfer,
            transfer.SourceWarehouseId,
            transit.Id,
            destination.Id,
            line.ItemId,
            identity,
            InventoryStatusSystemIds.InTransit,
            input.Quantity,
            item.UnitOfMeasure,
            "TransferOrder",
            transfer.TransferNumber,
            line.Id,
            userId,
            cancellationToken,
            destinationStatusId: line.DestinationInventoryStatusId,
            destinationWarehouseId: transfer.DestinationWarehouseId,
            idempotencyPrefix: $"transfer:{transfer.Id}:{input.IdempotencyKey}:receive",
            ownerKind: line.OwnerKind,
            inventoryOwnerId: line.InventoryOwnerId,
            ownerCodeSnapshot: line.OwnerCodeSnapshot);
        transfer.RecordReceipt(line, input.Quantity, clock.UtcNow.UtcDateTime);
    }

    private async Task RecordTransferLedgerAsync(
        InventoryTransactionType type,
        int sourceWarehouseId,
        int sourceLocationId,
        int destinationLocationId,
        int itemId,
        IdentitySnapshot identity,
        int sourceStatusId,
        decimal quantity,
        string baseUnitOfMeasure,
        string referenceType,
        string referenceId,
        int? referenceLine,
        string userId,
        CancellationToken cancellationToken,
        int? destinationStatusId = null,
        int? destinationWarehouseId = null,
        string? idempotencyPrefix = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        var targetWarehouseId = destinationWarehouseId ?? sourceWarehouseId;
        var groupId = idempotencyPrefix ?? $"movement:{Guid.NewGuid():N}";
        var sourceKey = new InventoryBalanceKey(
            sourceWarehouseId,
            sourceLocationId,
            itemId,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            identity.LicensePlateId,
            sourceStatusId,
            baseUnitOfMeasure,
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        var destinationKey = new InventoryBalanceKey(
            targetWarehouseId,
            destinationLocationId,
            itemId,
            identity.LotId,
            identity.SerialNumberId,
            identity.SerialNumber,
            identity.LicensePlateId,
            destinationStatusId ?? sourceStatusId,
            baseUnitOfMeasure,
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        await inventoryLedgerService.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    type,
                    sourceKey,
                    -quantity,
                    ActorUserId: userId,
                    ReferenceType: referenceType,
                    ReferenceId: referenceId,
                    ReferenceLine: referenceLine,
                    Reason: "transfer outbound leg",
                    OccurredAtUtc: clock.UtcNow.UtcDateTime,
                    IdempotencyKey: $"{groupId}:source",
                    TransactionGroupId: groupId,
                    EntrySequence: 1),
                new InventoryLedgerEntryRequest(
                    type,
                    destinationKey,
                    quantity,
                    ActorUserId: userId,
                    ReferenceType: referenceType,
                    ReferenceId: referenceId,
                    ReferenceLine: referenceLine,
                    Reason: "transfer inbound leg",
                    OccurredAtUtc: clock.UtcNow.UtcDateTime,
                    IdempotencyKey: $"{groupId}:destination",
                    TransactionGroupId: groupId,
                    EntrySequence: 2)
            ],
            cancellationToken);
    }

    private async Task<Result<TransferOrderDto>> ExecuteCommandAsync(
        int transferOrderId,
        string operation,
        string idempotencyKey,
        object request,
        string userId,
        Func<TransferOrder, Task> mutation,
        string auditAction,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<TransferOrderDto>(WmsErrors.Validation(
                "transfer.idempotency_required",
                "Every transfer command requires an idempotency key."));
        }

        var transfer = await LoadAsync(transferOrderId, cancellationToken);
        if (transfer is null)
        {
            return Result.Failure<TransferOrderDto>(WmsErrors.NotFound(
                "transfer.not_found",
                "The transfer order was not found."));
        }

        var authorization = await AuthorizeBothWarehousesAsync(
            WmsPermissions.InventoryAdjust,
            transfer,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<TransferOrderDto>();
        }

        var requestHash = Hash(request);
        var existing = await context.TransferCommands.AsNoTracking().SingleOrDefaultAsync(
            value => value.TransferOrderId == transferOrderId &&
                     value.Operation == operation &&
                     value.IdempotencyKey == idempotencyKey.Trim(),
            cancellationToken);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                return Result.Failure<TransferOrderDto>(WmsErrors.Conflict(
                    "transfer.idempotency_conflict",
                    "The transfer command idempotency key was already used for another request."));
            }

            var replay = await LoadAsync(transferOrderId, cancellationToken);
            return replay is null
                ? Result.Failure<TransferOrderDto>(WmsErrors.NotFound("transfer.not_found", "The transfer order was not found."))
                : Result.Success(Map(replay));
        }

        return await ExecuteMutationAsync(
            async () =>
            {
                var current = await LoadAsync(transferOrderId, cancellationToken)
                    ?? throw new InvalidOperationException("The transfer order was not found.");
                await mutation(current);
                context.TransferCommands.Add(new TransferCommand(
                    current.Id,
                    operation,
                    idempotencyKey,
                    requestHash,
                    userId,
                    clock.UtcNow.UtcDateTime));
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        auditAction,
                        WmsAuditEntityTypes.Transfer,
                        current.Id.ToString(CultureInfo.InvariantCulture),
                        current.SourceWarehouseId,
                        ActorUserId: userId,
                        After: new Dictionary<string, object?>
                        {
                            ["operation"] = operation,
                            ["transferNumber"] = current.TransferNumber,
                            ["status"] = current.Status.ToString()
                        }),
                    cancellationToken);
                return Map(current);
            },
            $"transfer.{operation}_failed",
            "The transfer command could not be completed.",
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
            return Result.Failure<T>(WmsErrors.Concurrency("transfer.concurrency_conflict", exception.Message));
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
            logger.LogError(exception, "Transfer operation failed with {ErrorCode}", errorCode);
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

    private async Task<TransferOrder?> LoadAsync(int id, CancellationToken cancellationToken) =>
        await context.TransferOrders.Include(value => value.Lines).SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    private async Task<Result> AuthorizeBothWarehousesAsync(
        string permission,
        TransferOrder transfer,
        CancellationToken cancellationToken) =>
        await AuthorizeBothWarehousesAsync(
            permission,
            transfer.SourceWarehouseId,
            transfer.DestinationWarehouseId,
            cancellationToken);

    private async Task<Result> AuthorizeBothWarehousesAsync(
        string permission,
        int sourceWarehouseId,
        int destinationWarehouseId,
        CancellationToken cancellationToken)
    {
        var sourceAuthorization = await warehouseAccessService.AuthorizeAsync(permission, sourceWarehouseId, cancellationToken);
        if (sourceAuthorization.IsFailure)
        {
            return sourceAuthorization;
        }

        return await warehouseAccessService.AuthorizeAsync(permission, destinationWarehouseId, cancellationToken);
    }

    private async Task ValidateTransferReferencesAsync(
        TransferOrderInput input,
        CancellationToken cancellationToken)
    {
        if (input.Lines.Count == 0)
        {
            throw new ArgumentException("At least one transfer line is required.", nameof(input));
        }

        if (!await context.Warehouses.AnyAsync(value => value.Id == input.SourceWarehouseId, cancellationToken) ||
            !await context.Warehouses.AnyAsync(value => value.Id == input.DestinationWarehouseId, cancellationToken))
        {
            throw new InvalidOperationException("Both transfer warehouses must exist.");
        }

        var transit = await context.Locations.SingleOrDefaultAsync(value => value.Id == input.TransitLocationId, cancellationToken)
            ?? throw new InvalidOperationException("The transfer transit location was not found.");
        if (transit.WarehouseId != input.SourceWarehouseId || transit.Type != LocationType.Transit)
        {
            throw new InvalidOperationException("The transit location must be a Transit location in the source warehouse.");
        }

        foreach (var line in input.Lines)
        {
            var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot);
            if (line.OwnerKind != InventoryOwnerKind.CompanyOwned &&
                !await context.InventoryOwners.AnyAsync(owner =>
                    owner.Id == line.InventoryOwnerId &&
                    owner.IsActive &&
                    owner.Kind == line.OwnerKind &&
                    owner.OwnerCode == ownerCode,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "The transfer line inventory owner was not found, is inactive, or does not match its snapshot.");
            }

            var item = await context.Items.SingleOrDefaultAsync(value => value.Id == line.ItemId, cancellationToken)
                ?? throw new InvalidOperationException($"Item {line.ItemId} was not found.");
            EnsureUnit(item, line.BaseUnitOfMeasure);
            if (line.SourceInventoryStatusId == InventoryStatusSystemIds.InTransit ||
                line.DestinationInventoryStatusId == InventoryStatusSystemIds.InTransit)
            {
                throw new InvalidOperationException("IN_TRANSIT is controlled by the transfer workflow and cannot be supplied on a line.");
            }

            var source = await context.Locations.SingleOrDefaultAsync(value => value.Id == line.SourceLocationId, cancellationToken)
                ?? throw new InvalidOperationException("A transfer source location was not found.");
            var destination = await context.Locations.SingleOrDefaultAsync(value => value.Id == line.DestinationLocationId, cancellationToken)
                ?? throw new InvalidOperationException("A transfer destination location was not found.");
            if (source.WarehouseId != input.SourceWarehouseId || destination.WarehouseId != input.DestinationWarehouseId)
            {
                throw new InvalidOperationException("Transfer line locations must belong to their respective warehouses.");
            }

            if (line.SerialNumberId.HasValue)
            {
                var serial = await context.SerialNumbers.SingleOrDefaultAsync(value => value.Id == line.SerialNumberId.Value, cancellationToken)
                    ?? throw new InvalidOperationException("The transfer serial number was not found.");
                if (serial.ItemId != line.ItemId ||
                    (!string.IsNullOrWhiteSpace(line.SerialNumber) &&
                     !string.Equals(serial.Number, line.SerialNumber.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("The transfer serial identity does not match the item line.");
                }
            }
        }
    }

    private async Task<IdentitySnapshot> ResolveIdentityAsync(
        int itemId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        CancellationToken cancellationToken)
    {
        SerialNumber? serial = null;
        if (serialNumberId.HasValue)
        {
            serial = await context.SerialNumbers.SingleOrDefaultAsync(value => value.Id == serialNumberId.Value, cancellationToken)
                ?? throw new InvalidOperationException("The serial number was not found.");
            if (serial.ItemId != itemId)
            {
                throw new InvalidOperationException("The serial number does not belong to the item.");
            }

            if (!string.IsNullOrWhiteSpace(serialNumber) &&
                !string.Equals(serial.Number, serialNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The serial number text does not match the serial identity.");
            }
        }

        return new IdentitySnapshot(
            lotId,
            serial?.Id ?? serialNumberId,
            serial?.Number ?? NormalizeOptional(serialNumber),
            licensePlateId,
            serial);
    }

    private async Task<Stock?> FindStockAsync(
        int itemId,
        int locationId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int inventoryStatusId,
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
        var query = context.Stock.Where(value =>
            value.ItemId == itemId &&
            value.LocationId == locationId &&
            value.LotId == lotId &&
            value.SerialNumberId == serialNumberId &&
            value.InventoryStatusId == inventoryStatusId &&
            value.LicensePlateId == licensePlateId &&
            value.OwnerKind == ownerKind &&
            value.InventoryOwnerId == inventoryOwnerId &&
            value.OwnerCodeSnapshot == normalizedOwnerCode);
        query = serialNumber is null
            ? query.Where(value => value.SerialNumber == null)
            : query.Where(value => value.SerialNumber == serialNumber);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private static void EnsureMovableStock(Stock stock, decimal quantity)
    {
        if (stock.QuantityReserved.Value > 0m || stock.GetAvailableQuantity().Value < quantity)
        {
            throw new InvalidOperationException("The source stock is reserved or does not have enough available quantity.");
        }
    }

    private void MoveStock(
        Stock sourceStock,
        Stock? destinationStock,
        InternalMovementInput input,
        IdentitySnapshot identity)
    {
        var quantity = new Quantity(input.Quantity);
        sourceStock.RemoveQuantity(quantity);
        if (sourceStock.QuantityAvailable.Value == 0m && sourceStock.QuantityReserved.Value == 0m)
        {
            context.Stock.Remove(sourceStock);
        }

        if (destinationStock is null)
        {
            context.Stock.Add(new Stock(
                input.ItemId,
                input.DestinationLocationId,
                quantity,
                identity.LotId,
                identity.SerialNumber,
                identity.SerialNumberId,
                input.InventoryStatusId,
                input.LicensePlateId,
                input.OwnerKind,
                input.InventoryOwnerId,
                input.OwnerCodeSnapshot));
        }
        else
        {
            destinationStock.AddQuantity(quantity);
        }
    }

    private void MoveSerial(SerialNumber? serial, int warehouseId, int locationId, int? licensePlateId)
    {
        serial?.MoveTo(warehouseId, locationId, null, clock.UtcNow.UtcDateTime, licensePlateId);
    }

    private async Task MoveLicensePlateIfWholeAsync(
        int? licensePlateId,
        int sourceLocationId,
        int destinationLocationId,
        decimal quantity,
        int destinationWarehouseId,
        CancellationToken cancellationToken)
    {
        if (!licensePlateId.HasValue)
        {
            return;
        }

        var totalAtSource = await context.Stock
            .Where(value => value.LicensePlateId == licensePlateId.Value && value.LocationId == sourceLocationId)
            .Select(value => value.QuantityAvailable.Value)
            .SumAsync(cancellationToken);
        if (totalAtSource != quantity)
        {
            throw new InvalidOperationException("A license plate transfer must move the whole license plate in one operation.");
        }

        var licensePlate = await context.LicensePlates.SingleOrDefaultAsync(value => value.Id == licensePlateId.Value, cancellationToken)
            ?? throw new InvalidOperationException("The transfer license plate was not found.");
        licensePlate.TransferTo(destinationWarehouseId, destinationLocationId);
    }

    private static void EnsureUnit(Item item, string unitOfMeasure)
    {
        if (!string.Equals(item.UnitOfMeasure, unitOfMeasure.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The transfer unit must be {item.UnitOfMeasure}.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Hash(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static TransferOrderDto Map(TransferOrder transfer) => new(
        transfer.Id,
        transfer.TransferNumber,
        transfer.SourceWarehouseId,
        transfer.DestinationWarehouseId,
        transfer.TransitLocationId,
        transfer.Priority,
        transfer.ExternalReference,
        transfer.Notes,
        transfer.Status,
        transfer.RequestedQuantity,
        transfer.ShippedQuantity,
        transfer.ReceivedQuantity,
        transfer.Lines.Select(line => new TransferOrderLineDto(
            line.Id,
            line.Sequence,
            line.ItemId,
            line.RequestedQuantity,
            line.ShippedQuantity,
            line.ReceivedQuantity,
            line.RemainingToShip,
            line.RemainingToReceive,
            line.BaseUnitOfMeasure,
            line.SourceLocationId,
            line.DestinationLocationId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            line.SourceInventoryStatusId,
            line.DestinationInventoryStatusId,
            line.Status,
            line.Revision,
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot)).ToArray(),
        transfer.CreatedAt,
        transfer.ConfirmedAtUtc,
        transfer.ReleasedAtUtc,
        transfer.ShippedAtUtc,
        transfer.ReceivedAtUtc,
        transfer.ClosedAtUtc,
        transfer.CancelledAtUtc,
        transfer.Revision);

    private static InternalMovementDto Map(InternalMovement movement) => new(
        movement.Id,
        movement.IdempotencyKey,
        movement.WarehouseId,
        movement.ItemId,
        movement.Quantity,
        movement.BaseUnitOfMeasure,
        movement.SourceLocationId,
        movement.DestinationLocationId,
        movement.LotId,
        movement.SerialNumberId,
        movement.SerialNumber,
        movement.LicensePlateId,
        movement.InventoryStatusId,
        movement.Status,
        movement.CreatedAt,
        movement.CompletedAtUtc,
        movement.Revision,
        movement.OwnerKind,
        movement.InventoryOwnerId,
        movement.OwnerCodeSnapshot);

    private sealed record IdentitySnapshot(
        int? LotId,
        int? SerialNumberId,
        string? SerialNumber,
        int? LicensePlateId,
        SerialNumber? Serial);
}
