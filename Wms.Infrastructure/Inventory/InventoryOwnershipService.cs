using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

public sealed class InventoryOwnershipService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IInventoryLedgerService inventoryLedgerService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InventoryOwnershipService> logger) : IInventoryOwnershipService
{
    public async Task<Result<IReadOnlyList<InventoryOwnerDto>>> ListOwnersAsync(
        InventoryOwnerQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryOwnerDto>>();
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var owners = context.InventoryOwners.AsNoTracking();
        if (query.Kind.HasValue)
        {
            owners = owners.Where(owner => owner.Kind == query.Kind.Value);
        }

        if (!query.IncludeInactive)
        {
            owners = owners.Where(owner => owner.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = $"%{query.SearchTerm.Trim().ToUpperInvariant()}%";
            owners = owners.Where(owner =>
                EF.Functions.Like(owner.OwnerCode, pattern) ||
                EF.Functions.Like(owner.DisplayName.ToUpperInvariant(), pattern) ||
                (owner.ExternalOwnerReference != null &&
                 EF.Functions.Like(owner.ExternalOwnerReference, pattern)));
        }

        var rows = await owners
            .OrderBy(owner => owner.OwnerCode)
            .ThenBy(owner => owner.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<InventoryOwnerDto>>(
            rows.Select(Map).ToArray());
    }

    public async Task<Result<InventoryOwnerDto>> GetOwnerAsync(
        int ownerId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryOwnerDto>();
        }

        var owner = await context.InventoryOwners.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ownerId, cancellationToken);
        return owner is null
            ? Result.Failure<InventoryOwnerDto>(WmsErrors.NotFound(
                "inventory.owner_not_found",
                "The inventory owner was not found."))
            : Result.Success(Map(owner));
    }

    public async Task<Result<InventoryOwnerDto>> CreateOwnerAsync(
        InventoryOwnerInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipManage,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryOwnerDto>();
        }

        try
        {
            if (!Enum.IsDefined(input.Kind) || input.Kind == InventoryOwnerKind.CompanyOwned)
            {
                return Result.Failure<InventoryOwnerDto>(WmsErrors.Validation(
                    "inventory.owner_kind_invalid",
                    "Company-owned inventory is the built-in owner; only supplier, customer, or external owners can be created."));
            }

            var ownerCode = input.OwnerCode.Trim().ToUpperInvariant();
            if (await context.InventoryOwners.AnyAsync(
                    owner => owner.OwnerCode == ownerCode,
                    cancellationToken))
            {
                return Result.Failure<InventoryOwnerDto>(WmsErrors.Conflict(
                    "inventory.owner_code_duplicate",
                    $"Inventory owner code '{ownerCode}' is already in use."));
            }

            var linkedEntityValidation = await ValidateOwnerLinkAsync(input, cancellationToken);
            if (linkedEntityValidation.IsFailure)
            {
                return linkedEntityValidation.ToFailure<InventoryOwnerDto>();
            }

            var owner = new InventoryOwner(
                ownerCode,
                input.Kind,
                input.DisplayName,
                input.LocalizedName,
                input.SupplierId,
                input.CustomerId,
                input.ExternalOwnerReference);
            context.InventoryOwners.Add(owner);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryOwnerCreated,
                    WmsAuditEntityTypes.InventoryOwner,
                    owner.OwnerCode,
                    After: new Dictionary<string, object?>
                    {
                        ["kind"] = owner.Kind.ToString(),
                        ["ownerCode"] = owner.OwnerCode,
                        ["supplierId"] = owner.SupplierId,
                        ["customerId"] = owner.CustomerId,
                        ["externalOwnerReference"] = owner.ExternalOwnerReference
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(owner));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryOwnerDto>(WmsErrors.Validation(
                "inventory.owner_invalid",
                exception.Message));
        }
    }

    public async Task<Result<InventoryOwnerDto>> DeactivateOwnerAsync(
        int ownerId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipManage,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryOwnerDto>();
        }

        var owner = await context.InventoryOwners
            .SingleOrDefaultAsync(candidate => candidate.Id == ownerId, cancellationToken);
        if (owner is null)
        {
            return Result.Failure<InventoryOwnerDto>(WmsErrors.NotFound(
                "inventory.owner_not_found",
                "The inventory owner was not found."));
        }

        if (owner.IsActive)
        {
            owner.Deactivate();
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryOwnerDeactivated,
                    WmsAuditEntityTypes.InventoryOwner,
                    owner.OwnerCode,
                    After: new Dictionary<string, object?>
                    {
                        ["isActive"] = false,
                        ["ownerCode"] = owner.OwnerCode
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Map(owner));
    }

    public async Task<Result<InventoryOwnershipTransferDto>> TransferAsync(
        InventoryOwnershipTransferInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryOwnershipTransferDto>();
        }

        if (input.WarehouseId <= 0 || input.ItemId <= 0 || input.LocationId <= 0 ||
            input.InventoryStatusId <= 0 || input.Quantity <= 0m ||
            string.IsNullOrWhiteSpace(input.IdempotencyKey) ||
            string.IsNullOrWhiteSpace(input.BaseUnitOfMeasure))
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.Validation(
                "inventory.ownership_transfer_invalid",
                "Warehouse, item, location, status, quantity, unit, and idempotency key are required."));
        }

        string sourceOwnerCode;
        string destinationOwnerCode;
        try
        {
            sourceOwnerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                input.SourceOwnerKind,
                input.SourceInventoryOwnerId,
                input.SourceOwnerCodeSnapshot);
            destinationOwnerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                input.DestinationOwnerKind,
                input.DestinationInventoryOwnerId,
                input.DestinationOwnerCodeSnapshot);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.Validation(
                "inventory.ownership_transfer_owner_invalid",
                exception.Message));
        }

        if (input.SourceOwnerKind == input.DestinationOwnerKind &&
            input.SourceInventoryOwnerId == input.DestinationInventoryOwnerId)
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.Validation(
                "inventory.ownership_transfer_same_owner",
                "An ownership transfer must change the owner dimension."));
        }

        var requestHash = ComputeHash(input, sourceOwnerCode, destinationOwnerCode);
        var existing = await context.InventoryOwnershipTransfers
            .SingleOrDefaultAsync(
                transfer => transfer.IdempotencyKey == input.IdempotencyKey.Trim(),
                cancellationToken);
        if (existing is not null)
        {
            return existing.RequestHash == requestHash
                ? Result.Success(Map(existing))
                : Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.Conflict(
                    "inventory.ownership_transfer_idempotency_conflict",
                    "The idempotency key was already used for a different ownership transfer."));
        }

        var ownerValidation = await ValidateOwnerDimensionAsync(
            input.SourceOwnerKind,
            input.SourceInventoryOwnerId,
            sourceOwnerCode,
            cancellationToken);
        if (ownerValidation.IsFailure)
        {
            return ownerValidation.ToFailure<InventoryOwnershipTransferDto>();
        }

        ownerValidation = await ValidateOwnerDimensionAsync(
            input.DestinationOwnerKind,
            input.DestinationInventoryOwnerId,
            destinationOwnerCode,
            cancellationToken);
        if (ownerValidation.IsFailure)
        {
            return ownerValidation.ToFailure<InventoryOwnershipTransferDto>();
        }

        var warehouse = await context.Warehouses
            .SingleOrDefaultAsync(value => value.Id == input.WarehouseId && value.IsActive, cancellationToken);
        var item = await context.Items
            .SingleOrDefaultAsync(value => value.Id == input.ItemId && value.IsActive, cancellationToken);
        var location = await context.Locations
            .SingleOrDefaultAsync(value => value.Id == input.LocationId &&
                                           value.WarehouseId == input.WarehouseId &&
                                           value.IsActive,
                cancellationToken);
        var statusExists = await context.InventoryStatuses
            .AnyAsync(value => value.Id == input.InventoryStatusId, cancellationToken);
        if (warehouse is null || item is null || location is null || !statusExists)
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.Validation(
                "inventory.ownership_transfer_dimension_invalid",
                "The warehouse, item, location, or inventory status is invalid."));
        }

        var serialNumber = NormalizeOptional(input.SerialNumber);
        var baseUnit = input.BaseUnitOfMeasure.Trim().ToUpperInvariant();
        var sourceStock = await context.Stock.SingleOrDefaultAsync(stock =>
            stock.ItemId == input.ItemId &&
            stock.LocationId == input.LocationId &&
            stock.LotId == input.LotId &&
            stock.SerialNumberId == input.SerialNumberId &&
            stock.SerialNumber == serialNumber &&
            stock.InventoryStatusId == input.InventoryStatusId &&
            stock.LicensePlateId == input.LicensePlateId &&
            stock.OwnerKind == input.SourceOwnerKind &&
            stock.InventoryOwnerId == input.SourceInventoryOwnerId &&
            stock.OwnerCodeSnapshot == sourceOwnerCode,
            cancellationToken);
        if (sourceStock is null ||
            sourceStock.GetAvailableQuantity().Value < input.Quantity)
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.BusinessRule(
                "inventory.ownership_transfer_source_unavailable",
                "The source owner dimension does not have enough unreserved stock."));
        }

        if (input.LicensePlateId.HasValue && await context.Stock.AnyAsync(stock =>
                stock.LicensePlateId == input.LicensePlateId &&
                stock.Id != sourceStock.Id,
                cancellationToken))
        {
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.BusinessRule(
                "inventory.ownership_transfer_lpn_must_be_whole",
                "Ownership transfer of a license plate requires the whole license plate to be transferred as one owner dimension."));
        }

        var destinationStock = await context.Stock.SingleOrDefaultAsync(stock =>
            stock.ItemId == input.ItemId &&
            stock.LocationId == input.LocationId &&
            stock.LotId == input.LotId &&
            stock.SerialNumberId == input.SerialNumberId &&
            stock.SerialNumber == serialNumber &&
            stock.InventoryStatusId == input.InventoryStatusId &&
            stock.LicensePlateId == input.LicensePlateId &&
            stock.OwnerKind == input.DestinationOwnerKind &&
            stock.InventoryOwnerId == input.DestinationInventoryOwnerId &&
            stock.OwnerCodeSnapshot == destinationOwnerCode,
            cancellationToken);

        var transferNumber = $"OWN-{input.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        var transfer = new InventoryOwnershipTransfer(
            transferNumber,
            input.IdempotencyKey.Trim(),
            requestHash,
            input.WarehouseId,
            input.ItemId,
            input.Quantity,
            baseUnit,
            input.LocationId,
            input.InventoryStatusId,
            input.SourceOwnerKind,
            input.SourceInventoryOwnerId,
            sourceOwnerCode,
            input.DestinationOwnerKind,
            input.DestinationInventoryOwnerId,
            destinationOwnerCode,
            userId,
            clock.UtcNow.UtcDateTime,
            input.LotId,
            input.SerialNumberId,
            serialNumber,
            input.LicensePlateId,
            input.Reason);

        var transactionStarted = false;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            transactionStarted = true;

            sourceStock.RemoveQuantity(new Quantity(input.Quantity));
            if (destinationStock is null)
            {
                destinationStock = new Stock(
                    input.ItemId,
                    input.LocationId,
                    new Quantity(input.Quantity),
                    input.LotId,
                    serialNumber,
                    input.SerialNumberId,
                    input.InventoryStatusId,
                    input.LicensePlateId,
                    input.DestinationOwnerKind,
                    input.DestinationInventoryOwnerId,
                    destinationOwnerCode);
                context.Stock.Add(destinationStock);
            }
            else
            {
                destinationStock.AddQuantity(new Quantity(input.Quantity));
            }

            var movement = Movement.CreateTransfer(
                input.ItemId,
                input.LocationId,
                input.LocationId,
                new Quantity(input.Quantity),
                userId,
                input.LotId,
                serialNumber,
                input.IdempotencyKey,
                input.Reason,
                clock.UtcNow.UtcDateTime,
                input.SerialNumberId,
                input.InventoryStatusId,
                input.LicensePlateId,
                input.LicensePlateId);
            movement.SetOwnership(
                input.SourceOwnerKind,
                input.SourceInventoryOwnerId,
                sourceOwnerCode);
            context.Movements.Add(movement);
            context.InventoryOwnershipTransfers.Add(transfer);

            var transactionGroupId = $"ownership-transfer:{transfer.TransferNumber}";
            await inventoryLedgerService.RecordAsync(
                [
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.OwnershipTransfer,
                        new InventoryBalanceKey(
                            input.WarehouseId,
                            input.LocationId,
                            input.ItemId,
                            input.LotId,
                            input.SerialNumberId,
                            serialNumber,
                            input.LicensePlateId,
                            input.InventoryStatusId,
                            baseUnit,
                            input.SourceOwnerKind,
                            input.SourceInventoryOwnerId,
                            sourceOwnerCode),
                        -input.Quantity,
                        ActorUserId: userId,
                        ReferenceType: WmsAuditEntityTypes.InventoryOwnershipTransfer,
                        ReferenceId: transfer.TransferNumber,
                        Reason: input.Reason ?? "approved inventory ownership transfer",
                        OccurredAtUtc: transfer.CreatedAtUtc,
                        IdempotencyKey: $"{transactionGroupId}:source",
                        TransactionGroupId: transactionGroupId,
                        MovementId: movement.Id > 0 ? movement.Id : null),
                    new InventoryLedgerEntryRequest(
                        InventoryTransactionType.OwnershipTransfer,
                        new InventoryBalanceKey(
                            input.WarehouseId,
                            input.LocationId,
                            input.ItemId,
                            input.LotId,
                            input.SerialNumberId,
                            serialNumber,
                            input.LicensePlateId,
                            input.InventoryStatusId,
                            baseUnit,
                            input.DestinationOwnerKind,
                            input.DestinationInventoryOwnerId,
                            destinationOwnerCode),
                        input.Quantity,
                        ActorUserId: userId,
                        ReferenceType: WmsAuditEntityTypes.InventoryOwnershipTransfer,
                        ReferenceId: transfer.TransferNumber,
                        Reason: input.Reason ?? "approved inventory ownership transfer",
                        OccurredAtUtc: transfer.CreatedAtUtc,
                        IdempotencyKey: $"{transactionGroupId}:destination",
                        TransactionGroupId: transactionGroupId,
                        MovementId: movement.Id > 0 ? movement.Id : null)
                ],
                cancellationToken);

            transfer.Complete(userId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryOwnershipTransferred,
                    WmsAuditEntityTypes.InventoryOwnershipTransfer,
                    transfer.TransferNumber,
                    transfer.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["sourceOwner"] = sourceOwnerCode,
                        ["destinationOwner"] = destinationOwnerCode,
                        ["quantity"] = input.Quantity,
                        ["itemId"] = input.ItemId,
                        ["locationId"] = input.LocationId
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(transfer));
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (transactionStarted)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            }

            logger.LogError(exception, "Inventory ownership transfer failed for {TransferNumber}", transferNumber);
            return Result.Failure<InventoryOwnershipTransferDto>(WmsErrors.FromException(
                exception,
                "inventory.ownership_transfer_failed",
                "The inventory ownership transfer could not be completed."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryOwnershipTransferDto>>> ListTransfersAsync(
        int? warehouseId = null,
        int? itemId = null,
        InventoryOwnershipTransferStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryOwnershipTransferDto>>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var transfers = context.InventoryOwnershipTransfers.AsNoTracking();
        if (!scope.HasGlobalAccess)
        {
            transfers = transfers.Where(transfer => scope.WarehouseIds.Contains(transfer.WarehouseId));
        }

        if (warehouseId.HasValue)
        {
            transfers = transfers.Where(transfer => transfer.WarehouseId == warehouseId.Value);
        }

        if (itemId.HasValue)
        {
            transfers = transfers.Where(transfer => transfer.ItemId == itemId.Value);
        }

        if (status.HasValue)
        {
            transfers = transfers.Where(transfer => transfer.Status == status.Value);
        }

        var rows = await transfers
            .OrderByDescending(transfer => transfer.CreatedAtUtc)
            .ThenByDescending(transfer => transfer.Id)
            .Take(500)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<InventoryOwnershipTransferDto>>(
            rows.Select(Map).ToArray());
    }

    private async Task<Result> ValidateOwnerLinkAsync(
        InventoryOwnerInput input,
        CancellationToken cancellationToken)
    {
        if (input.Kind == InventoryOwnerKind.SupplierConsignment)
        {
            if (!input.SupplierId.HasValue || !await context.Suppliers.AnyAsync(
                    supplier => supplier.Id == input.SupplierId.Value && supplier.IsActive,
                    cancellationToken))
            {
                return Result.Failure(WmsErrors.Validation(
                    "inventory.owner_supplier_invalid",
                    "A supplier-consigned owner must reference an active supplier."));
            }
        }
        else if (input.Kind == InventoryOwnerKind.CustomerOwned)
        {
            if (!input.CustomerId.HasValue || !await context.Customers.AnyAsync(
                    customer => customer.Id == input.CustomerId.Value && customer.IsActive,
                    cancellationToken))
            {
                return Result.Failure(WmsErrors.Validation(
                    "inventory.owner_customer_invalid",
                    "A customer-owned owner must reference an active customer."));
            }
        }

        return Result.Success();
    }

    private async Task<Result> ValidateOwnerDimensionAsync(
        InventoryOwnerKind kind,
        int? ownerId,
        string ownerCode,
        CancellationToken cancellationToken)
    {
        if (kind == InventoryOwnerKind.CompanyOwned)
        {
            return Result.Success();
        }

        var owner = await context.InventoryOwners.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == ownerId, cancellationToken);
        return owner is not null && owner.IsActive && owner.Kind == kind && owner.OwnerCode == ownerCode
            ? Result.Success()
            : Result.Failure(WmsErrors.Validation(
                "inventory.owner_invalid",
                "The selected inventory owner was not found, is inactive, or does not match its snapshot."));
    }

    private static string ComputeHash(
        InventoryOwnershipTransferInput input,
        string sourceOwnerCode,
        string destinationOwnerCode)
    {
        var canonical = string.Join(
            "|",
            input.IdempotencyKey.Trim(),
            input.WarehouseId.ToString(CultureInfo.InvariantCulture),
            input.ItemId.ToString(CultureInfo.InvariantCulture),
            input.Quantity.ToString("G29", CultureInfo.InvariantCulture),
            input.BaseUnitOfMeasure.Trim().ToUpperInvariant(),
            input.LocationId.ToString(CultureInfo.InvariantCulture),
            input.InventoryStatusId.ToString(CultureInfo.InvariantCulture),
            input.SourceOwnerKind,
            input.SourceInventoryOwnerId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            sourceOwnerCode,
            input.DestinationOwnerKind,
            input.DestinationInventoryOwnerId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            destinationOwnerCode,
            input.LotId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            input.SerialNumberId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            NormalizeOptional(input.SerialNumber) ?? "-",
            input.LicensePlateId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            input.Reason?.Trim() ?? "-");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static InventoryOwnerDto Map(InventoryOwner owner) => new(
        owner.Id,
        owner.OwnerCode,
        owner.Kind,
        owner.DisplayName,
        owner.LocalizedName,
        owner.SupplierId,
        owner.CustomerId,
        owner.ExternalOwnerReference,
        owner.IsActive,
        owner.Revision);

    private static InventoryOwnershipTransferDto Map(InventoryOwnershipTransfer transfer) => new(
        transfer.Id,
        transfer.TransferNumber,
        transfer.IdempotencyKey,
        transfer.WarehouseId,
        transfer.ItemId,
        transfer.Quantity,
        transfer.BaseUnitOfMeasure,
        transfer.LocationId,
        transfer.InventoryStatusId,
        transfer.SourceOwnerKind,
        transfer.SourceInventoryOwnerId,
        transfer.SourceOwnerCodeSnapshot,
        transfer.DestinationOwnerKind,
        transfer.DestinationInventoryOwnerId,
        transfer.DestinationOwnerCodeSnapshot,
        transfer.LotId,
        transfer.SerialNumberId,
        transfer.SerialNumber,
        transfer.LicensePlateId,
        transfer.Reason,
        transfer.Status,
        transfer.CreatedByUserId,
        transfer.CreatedAtUtc,
        transfer.CompletedByUserId,
        transfer.CompletedAtUtc,
        transfer.ExceptionReason,
        transfer.Revision);
}
