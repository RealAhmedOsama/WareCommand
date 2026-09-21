using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.InventoryStatuses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.InventoryStatuses;

public sealed class InventoryStatusService(
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InventoryStatusService> logger,
    IInventoryLedgerService? inventoryLedgerService = null) : IInventoryStatusService
{
    public async Task<Result<IReadOnlyList<InventoryStatusDto>>> GetStatusesAsync(
        int? warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statuses = await unitOfWork.InventoryStatuses.GetForWarehouseAsync(
                warehouseId,
                includeInactive,
                cancellationToken);
            return Result.Success<IReadOnlyList<InventoryStatusDto>>(
                statuses.Select(ToDto).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory status lookup failed for warehouse {WarehouseId}", warehouseId);
            return Result.Failure<IReadOnlyList<InventoryStatusDto>>(WmsErrors.FromException(
                exception,
                "inventory_status.list_failed",
                "Inventory statuses could not be loaded."));
        }
    }

    public async Task<Result<InventoryStatusDto>> CreateAsync(
        InventoryStatusDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (input.WarehouseId is <= 0)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.Validation(
                    "inventory_status.warehouse_invalid",
                    "A valid warehouse is required for a warehouse-specific status."));
            }

            var existing = await unitOfWork.InventoryStatuses.GetByCodeAsync(
                input.Code,
                input.WarehouseId,
                cancellationToken);
            if (existing is not null)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.Conflict(
                    "inventory_status.code_exists",
                    $"Inventory status code '{input.Code.Trim().ToUpperInvariant()}' already exists."));
            }

            var status = new InventoryStatus(
                input.Code,
                input.Name,
                input.LocalizedName,
                input.IsAvailable,
                input.IsAllocatable,
                input.IsPickable,
                input.IsShippable,
                input.IsCountable,
                input.WarehouseId,
                isSystem: false,
                input.DefaultLocationType,
                input.ForceForLocationType);
            await unitOfWork.InventoryStatuses.AddAsync(status, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryStatusConfigured,
                    WmsAuditEntityTypes.InventoryStatus,
                    status.Code,
                    status.WarehouseId,
                    After: ToAuditMetadata(status),
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(status));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryStatusDto>(WmsErrors.Validation(
                "inventory_status.invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory status creation failed");
            return Result.Failure<InventoryStatusDto>(WmsErrors.FromException(
                exception,
                "inventory_status.create_failed",
                "The inventory status could not be created."));
        }
    }

    public async Task<Result<InventoryStatusDto>> UpdateAsync(
        int statusId,
        InventoryStatusDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await unitOfWork.InventoryStatuses.GetByIdAsync(statusId, cancellationToken);
            if (status is null)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.NotFound(
                    "inventory_status.not_found",
                    $"Inventory status {statusId} was not found."));
            }

            if (status.IsSystem)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.BusinessRule(
                    "inventory_status.system_update_forbidden",
                    $"System inventory status '{status.Code}' cannot be redefined."));
            }

            if (!string.Equals(
                    status.Code,
                    input.Code.Trim().ToUpperInvariant(),
                    StringComparison.Ordinal) ||
                status.WarehouseId != input.WarehouseId)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.Validation(
                    "inventory_status.identity_immutable",
                    "Status code and warehouse scope cannot be changed after creation."));
            }

            status.Update(
                input.Name,
                input.LocalizedName,
                input.IsAvailable,
                input.IsAllocatable,
                input.IsPickable,
                input.IsShippable,
                input.IsCountable,
                input.DefaultLocationType,
                input.ForceForLocationType);
            await unitOfWork.InventoryStatuses.UpdateAsync(status, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryStatusConfigured,
                    WmsAuditEntityTypes.InventoryStatus,
                    status.Id.ToString(CultureInfo.InvariantCulture),
                    status.WarehouseId,
                    After: ToAuditMetadata(status),
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(status));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryStatusDto>(WmsErrors.Validation(
                "inventory_status.invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<InventoryStatusDto>(WmsErrors.BusinessRule(
                "inventory_status.update_forbidden",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory status update failed for {StatusId}", statusId);
            return Result.Failure<InventoryStatusDto>(WmsErrors.FromException(
                exception,
                "inventory_status.update_failed",
                "The inventory status could not be updated."));
        }
    }

    public async Task<Result<InventoryStatusDto>> SetActiveAsync(
        int statusId,
        bool isActive,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await unitOfWork.InventoryStatuses.GetByIdAsync(statusId, cancellationToken);
            if (status is null)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.NotFound(
                    "inventory_status.not_found",
                    $"Inventory status {statusId} was not found."));
            }

            if (status.IsSystem && !isActive)
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.BusinessRule(
                    "inventory_status.system_disable_forbidden",
                    $"System inventory status '{status.Code}' cannot be disabled."));
            }

            if (!isActive && await unitOfWork.InventoryStatuses.IsInUseAsync(statusId, cancellationToken))
            {
                return Result.Failure<InventoryStatusDto>(WmsErrors.Conflict(
                    "inventory_status.in_use",
                    $"Inventory status '{status.Code}' is in use and cannot be disabled."));
            }

            var before = status.IsActive;
            if (isActive)
            {
                status.Activate();
            }
            else
            {
                status.Deactivate();
            }

            if (before != status.IsActive)
            {
                await unitOfWork.InventoryStatuses.UpdateAsync(status, cancellationToken);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.InventoryStatusConfigured,
                        WmsAuditEntityTypes.InventoryStatus,
                        status.Id.ToString(CultureInfo.InvariantCulture),
                        status.WarehouseId,
                        Before: new Dictionary<string, object?> { ["isActive"] = before },
                        After: new Dictionary<string, object?>
                        {
                            ["isActive"] = status.IsActive,
                            ["code"] = status.Code
                        },
                        ActorUserId: userId),
                    cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(ToDto(status));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<InventoryStatusDto>(WmsErrors.BusinessRule(
                "inventory_status.update_forbidden",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory status activation change failed for {StatusId}", statusId);
            return Result.Failure<InventoryStatusDto>(WmsErrors.FromException(
                exception,
                "inventory_status.activation_failed",
                "The inventory status activation could not be changed."));
        }
    }

    public async Task<Result<InventoryStatusTransitionDto>> ConfigureTransitionAsync(
        InventoryStatusTransitionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (input.FromStatusId <= 0 || input.ToStatusId <= 0 ||
                input.FromStatusId == input.ToStatusId)
            {
                return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.Validation(
                    "inventory_status.transition_invalid",
                    "A transition must reference two different positive status IDs."));
            }

            var fromStatus = await unitOfWork.InventoryStatuses.GetByIdAsync(
                input.FromStatusId,
                cancellationToken);
            var toStatus = await unitOfWork.InventoryStatuses.GetByIdAsync(
                input.ToStatusId,
                cancellationToken);
            if (fromStatus is null || toStatus is null)
            {
                return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.NotFound(
                    "inventory_status.transition_status_not_found",
                    "Both transition statuses must exist."));
            }

            if (!fromStatus.IsActive || !toStatus.IsActive)
            {
                return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.BusinessRule(
                    "inventory_status.transition_status_inactive",
                    "Inactive statuses cannot be used in an active transition."));
            }

            if (fromStatus.WarehouseId.HasValue &&
                toStatus.WarehouseId.HasValue &&
                fromStatus.WarehouseId != toStatus.WarehouseId)
            {
                return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.BusinessRule(
                    "inventory_status.transition_warehouse_mismatch",
                    "A transition cannot connect statuses from different warehouses."));
            }

            var transition = await unitOfWork.InventoryStatuses.GetTransitionAsync(
                input.FromStatusId,
                input.ToStatusId,
                cancellationToken);
            if (transition is null)
            {
                transition = new InventoryStatusTransition(
                    input.FromStatusId,
                    input.ToStatusId,
                    input.RequiresReason);
                await unitOfWork.InventoryStatuses.AddTransitionAsync(transition, cancellationToken);
            }
            else
            {
                transition.Update(input.RequiresReason, isActive: true);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryStatusTransitionConfigured,
                    WmsAuditEntityTypes.InventoryStatusTransition,
                    $"{fromStatus.Code}->{toStatus.Code}",
                    fromStatus.WarehouseId ?? toStatus.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["fromStatus"] = fromStatus.Code,
                        ["toStatus"] = toStatus.Code,
                        ["requiresReason"] = input.RequiresReason,
                        ["isActive"] = true
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(transition, fromStatus, toStatus));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.Validation(
                "inventory_status.transition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Inventory status transition configuration failed from {FromStatusId} to {ToStatusId}",
                input.FromStatusId,
                input.ToStatusId);
            return Result.Failure<InventoryStatusTransitionDto>(WmsErrors.FromException(
                exception,
                "inventory_status.transition_failed",
                "The inventory status transition could not be configured."));
        }
    }

    public async Task<Result<InventoryStatusChangeResult>> ChangeStockStatusAsync(
        int stockId,
        decimal quantity,
        int targetStatusId,
        string reason,
        string? referenceNumber,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Validation(
                "inventory_status.quantity_invalid",
                "Status change quantity must be greater than zero."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Validation(
                "inventory_status.reason_required",
                "A reason is required for every inventory status change."));
        }

        var normalizedReference = string.IsNullOrWhiteSpace(referenceNumber)
            ? $"STATUS-{Guid.NewGuid():N}"
            : referenceNumber.Trim();
        if (normalizedReference.Length > 100)
        {
            return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Validation(
                "inventory_status.reference_too_long",
                "The status change reference cannot exceed 100 characters."));
        }

        var transactionStarted = false;
        try
        {
            var source = await unitOfWork.Stock.GetByIdAsync(stockId, cancellationToken);
            if (source is null)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.NotFound(
                    "stock.not_found",
                    $"Stock record {stockId} was not found."));
            }

            var sourceStatus = source.InventoryStatus ?? await unitOfWork.InventoryStatuses.GetByIdAsync(
                source.InventoryStatusId,
                cancellationToken);
            if (sourceStatus is null)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Dependency(
                    "inventory_status.source_missing",
                    "The stock record references a missing inventory status.",
                    isRetryable: false));
            }

            var targetStatus = await unitOfWork.InventoryStatuses.GetByIdAsync(
                targetStatusId,
                cancellationToken);
            if (targetStatus is null)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.NotFound(
                    "inventory_status.target_not_found",
                    $"Inventory status {targetStatusId} was not found."));
            }

            if (!sourceStatus.IsActive || !targetStatus.IsActive)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.inactive",
                    "Source and target inventory statuses must be active."));
            }

            if (targetStatus.WarehouseId.HasValue &&
                targetStatus.WarehouseId != source.Location.WarehouseId)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.warehouse_mismatch",
                    "The target inventory status is not configured for this warehouse."));
            }

            if (sourceStatus.WarehouseId.HasValue &&
                sourceStatus.WarehouseId != source.Location.WarehouseId)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.source_warehouse_mismatch",
                    "The source inventory status is not configured for this warehouse."));
            }

            if (sourceStatus.Id == targetStatus.Id)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Validation(
                    "inventory_status.same_status",
                    "Stock is already in the requested inventory status."));
            }

            var transition = await unitOfWork.InventoryStatuses.GetTransitionAsync(
                sourceStatus.Id,
                targetStatus.Id,
                cancellationToken);
            if (transition is null || !transition.IsActive)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.transition_forbidden",
                    $"Inventory status cannot transition from '{sourceStatus.Code}' to '{targetStatus.Code}'."));
            }

            if (transition.RequiresReason && string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Validation(
                    "inventory_status.reason_required",
                    "A reason is required for this inventory status transition."));
            }

            var availableQuantity = source.GetAvailableQuantity().Value;
            if (quantity > availableQuantity)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.insufficient_available",
                    $"Only {availableQuantity} unreserved units can change status."));
            }

            var isSerialIdentity = source.SerialNumberId.HasValue ||
                                   !string.IsNullOrWhiteSpace(source.SerialNumber);
            if (isSerialIdentity && quantity != source.QuantityAvailable.Value)
            {
                return Result.Failure<InventoryStatusChangeResult>(WmsErrors.BusinessRule(
                    "inventory_status.serial_partial_forbidden",
                    "Serial-controlled stock must change status as a complete serial unit."));
            }

            var locationStock = await unitOfWork.Stock.GetByLocationIdAsync(
                source.LocationId,
                cancellationToken);
            var destination = locationStock.FirstOrDefault(candidate =>
                candidate.Id != source.Id &&
                candidate.ItemId == source.ItemId &&
                candidate.LotId == source.LotId &&
                candidate.InventoryStatusId == targetStatus.Id &&
                candidate.LicensePlateId == source.LicensePlateId &&
                HasSameSerialIdentity(candidate, source) &&
                candidate.OwnerKind == source.OwnerKind &&
                candidate.InventoryOwnerId == source.InventoryOwnerId &&
                candidate.OwnerCodeSnapshot == source.OwnerCodeSnapshot);

            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

            source.AdjustQuantity(
                new Quantity(source.QuantityAvailable.Value - quantity),
                reason);
            await unitOfWork.Stock.UpdateAsync(source, cancellationToken);

            if (destination is null)
            {
                destination = new Stock(
                    source.ItemId,
                    source.LocationId,
                    new Quantity(quantity),
                    source.LotId,
                    source.SerialNumber,
                    source.SerialNumberId,
                    targetStatus.Id,
                    source.LicensePlateId,
                    source.OwnerKind,
                    source.InventoryOwnerId,
                    source.OwnerCodeSnapshot);
                await unitOfWork.Stock.AddAsync(destination, cancellationToken);
            }
            else
            {
                destination.AddQuantity(new Quantity(quantity));
                await unitOfWork.Stock.UpdateAsync(destination, cancellationToken);
            }

            var movementQuantity = new Quantity(quantity);
            var timestamp = clock.UtcNow.UtcDateTime;
            var outboundMovement = Movement.CreateStatusChange(
                source.ItemId,
                source.LocationId,
                movementQuantity,
                userId,
                sourceStatus.Id,
                targetStatus.Id,
                InventoryStatusMovementLeg.Outbound,
                source.LotId,
                source.SerialNumber,
                normalizedReference,
                reason,
                timestamp,
                source.SerialNumberId,
                source.LicensePlateId);
            outboundMovement.SetOwnership(
                source.OwnerKind,
                source.InventoryOwnerId,
                source.OwnerCodeSnapshot);
            var inboundMovement = Movement.CreateStatusChange(
                source.ItemId,
                source.LocationId,
                movementQuantity,
                userId,
                sourceStatus.Id,
                targetStatus.Id,
                InventoryStatusMovementLeg.Inbound,
                source.LotId,
                source.SerialNumber,
                normalizedReference,
                reason,
                timestamp,
                source.SerialNumberId,
                source.LicensePlateId);
            inboundMovement.SetOwnership(
                source.OwnerKind,
                source.InventoryOwnerId,
                source.OwnerCodeSnapshot);
            await unitOfWork.Movements.AddAsync(outboundMovement, cancellationToken);
            await unitOfWork.Movements.AddAsync(inboundMovement, cancellationToken);

            if (inventoryLedgerService is not null)
            {
                var item = source.Item ?? await unitOfWork.Items.GetByIdAsync(
                    source.ItemId,
                    cancellationToken) ?? throw new InvalidOperationException(
                    $"Item {source.ItemId} was not found.");
                var transactionGroupId = $"movement:{Guid.NewGuid():N}";
                var sourceKey = new InventoryBalanceKey(
                    source.Location.WarehouseId,
                    source.LocationId,
                    source.ItemId,
                    source.LotId,
                    source.SerialNumberId,
                    source.SerialNumber,
                    source.LicensePlateId,
                    sourceStatus.Id,
                    item.UnitOfMeasure,
                    source.OwnerKind,
                    source.InventoryOwnerId,
                    source.OwnerCodeSnapshot);
                var destinationKey = new InventoryBalanceKey(
                    source.Location.WarehouseId,
                    source.LocationId,
                    source.ItemId,
                    source.LotId,
                    source.SerialNumberId,
                    source.SerialNumber,
                    source.LicensePlateId,
                    targetStatus.Id,
                    item.UnitOfMeasure,
                    source.OwnerKind,
                    source.InventoryOwnerId,
                    source.OwnerCodeSnapshot);
                await inventoryLedgerService.RecordAsync(
                    new[]
                    {
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.StatusChange,
                            sourceKey,
                            -quantity,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: normalizedReference,
                            Reason: reason,
                            OccurredAtUtc: timestamp,
                            CorrelationId: transactionGroupId,
                            IdempotencyKey: $"{transactionGroupId}:1",
                            TransactionGroupId: transactionGroupId,
                            EntrySequence: 1,
                            MovementId: outboundMovement.Id > 0 ? outboundMovement.Id : null),
                        new InventoryLedgerEntryRequest(
                            InventoryTransactionType.StatusChange,
                            destinationKey,
                            quantity,
                            ActorUserId: userId,
                            ReferenceType: "Movement",
                            ReferenceId: normalizedReference,
                            Reason: reason,
                            OccurredAtUtc: timestamp,
                            CorrelationId: transactionGroupId,
                            IdempotencyKey: $"{transactionGroupId}:2",
                            TransactionGroupId: transactionGroupId,
                            EntrySequence: 2,
                            MovementId: inboundMovement.Id > 0 ? inboundMovement.Id : null)
                    },
                    cancellationToken);
            }
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryStatusChanged,
                    WmsAuditEntityTypes.Stock,
                    source.Id.ToString(CultureInfo.InvariantCulture),
                    source.Location.WarehouseId,
                    Before: new Dictionary<string, object?>
                    {
                        ["status"] = sourceStatus.Code,
                        ["quantityAvailable"] = source.QuantityAvailable.Value + quantity
                    },
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = targetStatus.Code,
                        ["quantityChanged"] = quantity,
                        ["quantityRemaining"] = source.QuantityAvailable.Value,
                        ["reason"] = reason,
                        ["referenceNumber"] = normalizedReference
                    },
                    ActorUserId: userId),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;

            return Result.Success(new InventoryStatusChangeResult(
                source.Id,
                destination.Id == 0 ? null : destination.Id,
                outboundMovement.Id,
                inboundMovement.Id,
                quantity,
                ToDto(sourceStatus),
                ToDto(targetStatus),
                normalizedReference));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await RollbackQuietlyAsync();
            }

            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (transactionStarted)
            {
                await RollbackQuietlyAsync();
            }

            logger.LogWarning(exception, "Concurrent inventory status change rejected for stock {StockId}", stockId);
            return Result.Failure<InventoryStatusChangeResult>(WmsErrors.Concurrency(
                "inventory_status.concurrency_conflict",
                "Stock changed while the status was being updated. Reload it and retry."));
        }
        catch (Exception exception)
        {
            if (transactionStarted)
            {
                await RollbackQuietlyAsync();
            }

            logger.LogError(exception, "Inventory status change failed for stock {StockId}", stockId);
            return Result.Failure<InventoryStatusChangeResult>(WmsErrors.FromException(
                exception,
                "inventory_status.change_failed",
                "The inventory status could not be changed."));
        }
    }

    public async Task<Result<InventoryStatus>> ResolveInboundStatusAsync(
        Location location,
        bool qualityInspectionRequired,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statuses = (await unitOfWork.InventoryStatuses.GetForWarehouseAsync(
                    location.WarehouseId,
                    includeInactive: false,
                    cancellationToken))
                .ToArray();
            var forced = statuses
                .Where(status => status.ForceForLocationType && status.AppliesTo(location.Type))
                .OrderByDescending(status => status.WarehouseId.HasValue)
                .FirstOrDefault();
            if (forced is not null)
            {
                return Result.Success(forced);
            }

            var code = qualityInspectionRequired
                ? InventoryStatusCodes.QualityPending
                : InventoryStatusCodes.Available;
            var resolved = statuses
                .Where(status => status.Code == code)
                .OrderByDescending(status => status.WarehouseId.HasValue)
                .FirstOrDefault();
            return resolved is null
                ? Result.Failure<InventoryStatus>(WmsErrors.Dependency(
                    "inventory_status.inbound_missing",
                    $"No active inbound status '{code}' is configured.",
                    isRetryable: false))
                : Result.Success(resolved);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inbound inventory status resolution failed for location {LocationId}", location.Id);
            return Result.Failure<InventoryStatus>(WmsErrors.FromException(
                exception,
                "inventory_status.inbound_resolution_failed",
                "The inbound inventory status could not be resolved."));
        }
    }

    public async Task<Result<InventoryStatus>> ResolveDestinationStatusAsync(
        InventoryStatus sourceStatus,
        Location destination,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!sourceStatus.IsActive)
            {
                return Result.Failure<InventoryStatus>(WmsErrors.BusinessRule(
                    "inventory_status.source_inactive",
                    $"Inventory status '{sourceStatus.Code}' is inactive."));
            }

            var statuses = (await unitOfWork.InventoryStatuses.GetForWarehouseAsync(
                    destination.WarehouseId,
                    includeInactive: false,
                    cancellationToken))
                .ToArray();
            var forced = statuses
                .Where(status => status.ForceForLocationType && status.AppliesTo(destination.Type))
                .OrderByDescending(status => status.WarehouseId.HasValue)
                .FirstOrDefault();
            return Result.Success(forced ?? sourceStatus);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Destination inventory status resolution failed for location {LocationId}",
                destination.Id);
            return Result.Failure<InventoryStatus>(WmsErrors.FromException(
                exception,
                "inventory_status.destination_resolution_failed",
                "The destination inventory status could not be resolved."));
        }
    }

    public async Task<Result> ValidateOperationAsync(
        Stock stock,
        InventoryStatusOperation operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = stock.InventoryStatus ?? await unitOfWork.InventoryStatuses.GetByIdAsync(
                stock.InventoryStatusId,
                cancellationToken);
            if (status is null)
            {
                return Result.Failure(WmsErrors.Dependency(
                    "inventory_status.missing",
                    "The stock record references a missing inventory status.",
                    isRetryable: false));
            }

            if (!status.IsActive)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "inventory_status.inactive",
                    $"Inventory status '{status.Code}' is inactive."));
            }

            var allowed = operation switch
            {
                InventoryStatusOperation.Allocate => status.IsAllocatable,
                InventoryStatusOperation.Pick => status.IsPickable,
                InventoryStatusOperation.Ship => status.IsShippable,
                InventoryStatusOperation.Count => status.IsCountable,
                _ => false
            };
            if (allowed)
            {
                return Result.Success();
            }

            var operationName = operation.ToString().ToLowerInvariant();
            return Result.Failure(WmsErrors.BusinessRule(
                $"inventory_status.{operationName}_blocked",
                $"Inventory status '{status.Code}' does not permit {operationName} operations."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory status operation validation failed for stock {StockId}", stock.Id);
            return Result.Failure(WmsErrors.FromException(
                exception,
                "inventory_status.validation_failed",
                "Inventory status operation validation failed."));
        }
    }

    private async Task RollbackQuietlyAsync()
    {
        try
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(rollbackException, "Inventory status transaction rollback failed");
        }
    }

    private static bool HasSameSerialIdentity(Stock left, Stock right)
    {
        if (left.SerialNumberId.HasValue || right.SerialNumberId.HasValue)
        {
            return left.SerialNumberId == right.SerialNumberId;
        }

        return string.Equals(
            left.SerialNumber?.Trim(),
            right.SerialNumber?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static InventoryStatusDto ToDto(InventoryStatus status) => new(
        status.Id,
        status.Code,
        status.Name,
        status.LocalizedName,
        status.WarehouseId,
        status.Warehouse?.Code,
        status.IsAvailable,
        status.IsAllocatable,
        status.IsPickable,
        status.IsShippable,
        status.IsCountable,
        status.IsActive,
        status.IsSystem,
        status.DefaultLocationType,
        status.ForceForLocationType);

    private static InventoryStatusTransitionDto ToDto(
        InventoryStatusTransition transition,
        InventoryStatus fromStatus,
        InventoryStatus toStatus) => new(
        transition.Id,
        transition.FromInventoryStatusId,
        fromStatus.Code,
        transition.ToInventoryStatusId,
        toStatus.Code,
        transition.RequiresReason,
        transition.IsActive);

    private static Dictionary<string, object?> ToAuditMetadata(InventoryStatus status) => new()
    {
        ["code"] = status.Code,
        ["name"] = status.Name,
        ["localizedName"] = status.LocalizedName,
        ["warehouseId"] = status.WarehouseId,
        ["isAvailable"] = status.IsAvailable,
        ["isAllocatable"] = status.IsAllocatable,
        ["isPickable"] = status.IsPickable,
        ["isShippable"] = status.IsShippable,
        ["isCountable"] = status.IsCountable,
        ["isActive"] = status.IsActive,
        ["isSystem"] = status.IsSystem,
        ["defaultLocationType"] = status.DefaultLocationType?.ToString(),
        ["forceForLocationType"] = status.ForceForLocationType
    };
}
