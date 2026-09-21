using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Time;
using Wms.Application.InventoryStatuses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Owns the auditable disposition/recall request boundary. Stock movement is
/// delegated to the existing inventory-status service, which writes the
/// source/destination legacy stock, movement legs, and canonical ledger legs
/// together. This service never deletes stock or history.
/// </summary>
public sealed class InventoryDispositionService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    IInventoryStatusService inventoryStatusService,
    ILogger<InventoryDispositionService> logger) : IInventoryDispositionService
{
    private const int MaximumCandidateLimit = 5_000;

    public async Task<Result<InventoryDispositionPolicyDto>> SavePolicyAsync(
        int? policyId,
        InventoryDispositionPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.Validation(
                "inventory.disposition.actor_required",
                "An authenticated actor is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryDispositionPolicyDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            if (input.ItemId.HasValue &&
                !await context.Items.AnyAsync(
                    item => item.Id == input.ItemId.Value && item.IsActive,
                    cancellationToken))
            {
                return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The requested active item was not found."));
            }

            var policyKey = input.PolicyKey.Trim().ToUpperInvariant();
            var duplicate = await context.InventoryDispositionPolicies
                .AsNoTracking()
                .AnyAsync(
                    policy => policy.Id != (policyId ?? 0) &&
                              policy.WarehouseId == input.WarehouseId &&
                              policy.PolicyKey == policyKey,
                    cancellationToken);
            if (duplicate)
            {
                return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.Conflict(
                    "inventory.disposition_policy_key_conflict",
                    "A disposition policy with this key already exists in the warehouse."));
            }

            var effectiveFromUtc = NormalizeUtc(input.EffectiveFromUtc);
            var effectiveToUtc = input.EffectiveToUtc.HasValue
                ? NormalizeUtc(input.EffectiveToUtc.Value)
                : (DateTime?)null;
            InventoryDispositionPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.InventoryDispositionPolicies
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == policyId.Value,
                        cancellationToken)
                    ?? throw new KeyNotFoundException();
                if (policy.WarehouseId != input.WarehouseId)
                {
                    return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.Conflict(
                        "inventory.disposition_policy_identity_immutable",
                        "A disposition policy cannot be moved to another warehouse."));
                }

                policy.Update(
                    input.Name,
                    input.Priority,
                    input.ItemId,
                    input.ItemCategory,
                    input.WarningDays,
                    input.MinimumShelfLifeDays,
                    input.RequireApprovalForScrap,
                    input.RequireWitnessForDestruction,
                    effectiveFromUtc,
                    effectiveToUtc,
                    input.IsActive);
            }
            else
            {
                policy = new InventoryDispositionPolicy(
                    input.WarehouseId,
                    policyKey,
                    input.Name,
                    input.Priority,
                    input.ItemId,
                    input.ItemCategory,
                    input.WarningDays,
                    input.MinimumShelfLifeDays,
                    input.RequireApprovalForScrap,
                    input.RequireWitnessForDestruction,
                    effectiveFromUtc,
                    effectiveToUtc,
                    input.IsActive);
                await context.InventoryDispositionPolicies.AddAsync(policy, cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryDispositionPolicyChanged,
                    WmsAuditEntityTypes.InventoryDispositionPolicy,
                    policyId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyKey"] = policy.PolicyKey,
                        ["priority"] = policy.Priority,
                        ["itemId"] = policy.ItemId,
                        ["itemCategory"] = policy.ItemCategory,
                        ["warningDays"] = policy.WarningDays,
                        ["minimumShelfLifeDays"] = policy.MinimumShelfLifeDays,
                        ["requireApprovalForScrap"] = policy.RequireApprovalForScrap,
                        ["requireWitnessForDestruction"] = policy.RequireWitnessForDestruction,
                        ["effectiveFromUtc"] = policy.EffectiveFromUtc,
                        ["effectiveToUtc"] = policy.EffectiveToUtc,
                        ["isActive"] = policy.IsActive
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapPolicy(policy, warehouse));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.NotFound(
                "inventory.disposition_policy_not_found",
                "The requested disposition policy was not found."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.Validation(
                "inventory.disposition_policy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition policy save failed");
            return Result.Failure<InventoryDispositionPolicyDto>(WmsErrors.FromException(
                exception,
                "inventory.disposition_policy_save_failed",
                "The inventory disposition policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryDispositionPolicyDto>>> SearchPoliciesAsync(
        InventoryDispositionPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryDispositionPolicyDto>>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = context.InventoryDispositionPolicies
                .AsNoTracking()
                .Include(policy => policy.Warehouse)
                .AsQueryable();
            if (!scope.HasGlobalAccess)
            {
                policies = policies.Where(policy => scope.WarehouseIds.Contains(policy.WarehouseId));
            }

            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                policies = policies.Where(policy => !policy.ItemId.HasValue ||
                                                     policy.ItemId == query.ItemId.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.ItemCategory))
            {
                var category = query.ItemCategory.Trim();
                policies = policies.Where(policy => policy.ItemCategory == null ||
                                                     policy.ItemCategory == category);
            }

            if (!query.IncludeInactive)
            {
                policies = policies.Where(policy => policy.IsActive);
            }

            var rows = await policies
                .OrderBy(policy => policy.WarehouseId)
                .ThenByDescending(policy => policy.Priority)
                .ThenBy(policy => policy.PolicyKey)
                .Take(MaximumCandidateLimit)
                .ToListAsync(cancellationToken);
            return Result.Success<IReadOnlyList<InventoryDispositionPolicyDto>>(
                rows.Select(policy => MapPolicy(policy, policy.Warehouse)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition policy search failed");
            return Result.Failure<IReadOnlyList<InventoryDispositionPolicyDto>>(WmsErrors.FromException(
                exception,
                "inventory.disposition_policy_search_failed",
                "Inventory disposition policies could not be loaded."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryExpiryCandidateDto>>> GetExpiryCandidatesAsync(
        InventoryExpiryCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Limit is < 1 or > MaximumCandidateLimit)
        {
            return Result.Failure<IReadOnlyList<InventoryExpiryCandidateDto>>(WmsErrors.Validation(
                "inventory.expiry.limit_invalid",
                $"Expiry candidate limit must be between 1 and {MaximumCandidateLimit}."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<InventoryExpiryCandidateDto>>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == query.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<IReadOnlyList<InventoryExpiryCandidateDto>>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            var asOfUtc = NormalizeUtc(query.AsOfUtc ?? clock.UtcNow.UtcDateTime);
            var businessDate = WmsBusinessTime.GetBusinessDate(
                new DateTimeOffset(asOfUtc),
                warehouse.TimeZone);
            var policies = await context.InventoryDispositionPolicies
                .AsNoTracking()
                .Where(policy => policy.WarehouseId == warehouse.Id && policy.IsActive)
                .ToListAsync(cancellationToken);
            var balances = await context.InventoryBalances
                .AsNoTracking()
                .Include(balance => balance.Warehouse)
                .Include(balance => balance.Location)
                .Include(balance => balance.Item)
                .Include(balance => balance.Lot)
                .Include(balance => balance.InventoryStatus)
                .Where(balance => balance.WarehouseId == warehouse.Id &&
                                  balance.OnHandQuantity > balance.ReservedQuantity &&
                                  balance.LotId.HasValue)
                .ToListAsync(cancellationToken);

            var result = balances
                .Where(balance => balance.Lot?.ExpiryDate.HasValue == true)
                .Select(balance =>
                {
                    var item = balance.Item;
                    var lot = balance.Lot!;
                    var policy = ResolvePolicy(policies, item, asOfUtc);
                    var warningDays = policy?.WarningDays ?? warehouse.ExpiryWarningDays;
                    var minimumShelfLifeDays = policy?.MinimumShelfLifeDays ?? 0;
                    var expiryDate = DateOnly.FromDateTime(lot.ExpiryDate!.Value);
                    var daysUntilExpiry = expiryDate.DayNumber - businessDate.DayNumber;
                    var isExpired = daysUntilExpiry < 0;
                    var belowMinimumShelfLife = daysUntilExpiry < minimumShelfLifeDays;
                    return new InventoryExpiryCandidateDto(
                        balance.Id,
                        balance.WarehouseId,
                        balance.Warehouse.Code,
                        balance.LocationId,
                        balance.Location.Code,
                        balance.ItemId,
                        item.Sku,
                        item.Name,
                        lot.Id,
                        lot.Number,
                        expiryDate,
                        businessDate,
                        daysUntilExpiry,
                        balance.AvailableQuantity,
                        balance.InventoryStatusId,
                        balance.InventoryStatus.Code,
                        isExpired,
                        belowMinimumShelfLife,
                        warningDays,
                        minimumShelfLifeDays,
                        policy?.Id);
                })
                .Where(candidate => candidate.IsExpired ||
                                    candidate.DaysUntilExpiry <= candidate.WarningDays ||
                                    candidate.BelowMinimumShelfLife)
                .OrderBy(candidate => candidate.ExpiryDate)
                .ThenBy(candidate => candidate.StockBalanceId)
                .Take(query.Limit)
                .ToArray();

            return Result.Success<IReadOnlyList<InventoryExpiryCandidateDto>>(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory expiry candidate evaluation failed for warehouse {WarehouseId}",
                query.WarehouseId);
            return Result.Failure<IReadOnlyList<InventoryExpiryCandidateDto>>(WmsErrors.FromException(
                exception,
                "inventory.expiry_candidates_failed",
                "Expiry candidates could not be evaluated."));
        }
    }

    public async Task<Result<InventoryDispositionDto>> CreateAsync(
        InventoryDispositionCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.actor_required",
                "An authenticated actor is required."));
        }

        if (input.Quantity <= 0m)
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.quantity_invalid",
                "Disposition quantity must be greater than zero."));
        }

        if (!Enum.IsDefined(input.Kind))
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.kind_invalid",
                "The disposition kind is not supported."));
        }

        var key = input.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 250)
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.idempotency_required",
                "A disposition idempotency key between 1 and 250 characters is required."));
        }

        try
        {
            var stock = await context.Stock
                .Include(value => value.Location)
                .Include(value => value.Item)
                .Include(value => value.Lot)
                .Include(value => value.Serial)
                .Include(value => value.LicensePlate)
                .Include(value => value.InventoryStatus)
                .SingleOrDefaultAsync(value => value.Id == input.StockId, cancellationToken);
            if (stock is null)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.NotFound(
                    "stock.not_found",
                    "The requested stock record was not found."));
            }

            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                stock.Location.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryDispositionDto>();
            }

            var existing = await context.InventoryDispositions
                .SingleOrDefaultAsync(
                    disposition => disposition.WarehouseId == stock.Location.WarehouseId &&
                                   disposition.IdempotencyKey == key,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.StockId != input.StockId ||
                    existing.Kind != input.Kind ||
                    existing.RequestedQuantity != input.Quantity)
                {
                    return Result.Failure<InventoryDispositionDto>(WmsErrors.Conflict(
                        "inventory.disposition.idempotency_conflict",
                        "The idempotency key was already used for a different disposition."));
                }

                return Result.Success(MapDisposition(existing));
            }

            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                    "inventory.disposition.reason_required",
                    "A reason is required for every disposition."));
            }

            var availableQuantity = stock.GetAvailableQuantity().Value;
            if (input.Quantity > availableQuantity)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.BusinessRule(
                    "inventory.disposition.insufficient_available",
                    $"Only {availableQuantity:0.####} unreserved units are available for disposition."));
            }

            var targetStatusId = ResolveTargetStatus(input.Kind);
            if (stock.InventoryStatusId == targetStatusId)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                    "inventory.disposition.same_status",
                    "The stock is already in the requested disposition status."));
            }

            var targetStatus = await context.InventoryStatuses
                .AsNoTracking()
                .SingleOrDefaultAsync(status => status.Id == targetStatusId, cancellationToken);
            if (targetStatus is null || !targetStatus.IsActive)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.Dependency(
                    "inventory.disposition.target_status_missing",
                    "The target inventory disposition status is not available.",
                    isRetryable: false));
            }

            var transitionExists = await context.InventoryStatusTransitions
                .AsNoTracking()
                .AnyAsync(
                    transition => transition.FromInventoryStatusId == stock.InventoryStatusId &&
                                  transition.ToInventoryStatusId == targetStatusId &&
                                  transition.IsActive,
                    cancellationToken);
            if (!transitionExists)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.BusinessRule(
                    "inventory.disposition.transition_forbidden",
                    "The configured inventory status transition does not allow this disposition."));
            }

            var policies = await context.InventoryDispositionPolicies
                .AsNoTracking()
                .Where(policy => policy.WarehouseId == stock.Location.WarehouseId && policy.IsActive)
                .ToListAsync(cancellationToken);
            var policy = ResolvePolicy(policies, stock.Item, NormalizeUtc(clock.UtcNow.UtcDateTime));
            var approvalRequired = input.Kind switch
            {
                InventoryDispositionKind.Scrap => policy?.RequireApprovalForScrap ?? true,
                InventoryDispositionKind.Destruction => true,
                _ => false
            };
            if (input.Kind == InventoryDispositionKind.Destruction &&
                (policy?.RequireWitnessForDestruction ?? true) &&
                string.IsNullOrWhiteSpace(input.WitnessUserId))
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                    "inventory.disposition.witness_required",
                    "A witness is required before destruction can be requested."));
            }

            var disposition = new InventoryDisposition(
                $"DISP-{Guid.NewGuid():N}",
                key,
                stock.Location.WarehouseId,
                stock.Id,
                stock.ItemId,
                stock.LocationId,
                stock.LotId,
                stock.SerialNumberId,
                stock.LicensePlateId,
                stock.InventoryStatusId,
                targetStatusId,
                stock.Item.UnitOfMeasure,
                input.Kind,
                input.Quantity,
                input.Reason,
                actorUserId,
                input.ReferenceNumber,
                input.WitnessUserId,
                approvalRequired,
                clock.UtcNow.UtcDateTime);
            await context.InventoryDispositions.AddAsync(disposition, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryDispositionCreated,
                    WmsAuditEntityTypes.InventoryDisposition,
                    disposition.DispositionNumber,
                    disposition.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["stockId"] = disposition.StockId,
                        ["kind"] = disposition.Kind.ToString(),
                        ["quantity"] = disposition.RequestedQuantity,
                        ["status"] = disposition.Status.ToString(),
                        ["approvalRequired"] = disposition.ApprovalRequired,
                        ["reason"] = disposition.Reason,
                        ["referenceNumber"] = disposition.ReferenceNumber
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapDisposition(disposition));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition creation failed for stock {StockId}", input.StockId);
            return Result.Failure<InventoryDispositionDto>(WmsErrors.FromException(
                exception,
                "inventory.disposition_create_failed",
                "The inventory disposition could not be created."));
        }
    }

    public async Task<Result<InventoryDispositionDto>> ApproveAsync(
        int dispositionId,
        string actorUserId,
        InventoryDispositionApprovalInput? input = null,
        CancellationToken cancellationToken = default)
    {
        if (dispositionId <= 0 || string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.approval_invalid",
                "A positive disposition ID and authenticated actor are required."));
        }

        try
        {
            var disposition = await context.InventoryDispositions
                .SingleOrDefaultAsync(value => value.Id == dispositionId, cancellationToken);
            if (disposition is null)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.NotFound(
                    "inventory.disposition_not_found",
                    "The requested inventory disposition was not found."));
            }

            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                disposition.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryDispositionDto>();
            }

            if (disposition.Status == InventoryDispositionStatus.Approved ||
                disposition.Status == InventoryDispositionStatus.Completed)
            {
                return Result.Success(MapDisposition(disposition));
            }

            disposition.Approve(actorUserId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryDispositionApproved,
                    WmsAuditEntityTypes.InventoryDisposition,
                    disposition.DispositionNumber,
                    disposition.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = disposition.Status.ToString(),
                        ["approvalNote"] = input?.Note
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapDisposition(disposition));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Conflict(
                "inventory.disposition.approval_state_conflict",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition approval failed for {DispositionId}", dispositionId);
            return Result.Failure<InventoryDispositionDto>(WmsErrors.FromException(
                exception,
                "inventory.disposition_approve_failed",
                "The inventory disposition could not be approved."));
        }
    }

    public async Task<Result<InventoryDispositionDto>> ExecuteAsync(
        int dispositionId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (dispositionId <= 0 || string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryDispositionDto>(WmsErrors.Validation(
                "inventory.disposition.execution_invalid",
                "A positive disposition ID and authenticated actor are required."));
        }

        InventoryDisposition? disposition = null;
        try
        {
            disposition = await context.InventoryDispositions
                .SingleOrDefaultAsync(value => value.Id == dispositionId, cancellationToken);
            if (disposition is null)
            {
                return Result.Failure<InventoryDispositionDto>(WmsErrors.NotFound(
                    "inventory.disposition_not_found",
                    "The requested inventory disposition was not found."));
            }

            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                disposition.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryDispositionDto>();
            }

            if (disposition.Status == InventoryDispositionStatus.Completed)
            {
                return Result.Success(MapDisposition(disposition));
            }

            disposition.MarkExecuting();
            await context.SaveChangesAsync(cancellationToken);

            var statusChange = await inventoryStatusService.ChangeStockStatusAsync(
                disposition.StockId,
                disposition.RequestedQuantity,
                disposition.TargetInventoryStatusId,
                disposition.Reason,
                disposition.DispositionNumber,
                actorUserId,
                cancellationToken);
            if (statusChange.IsFailure)
            {
                disposition.MarkException(statusChange.Error);
                await context.SaveChangesAsync(cancellationToken);
                return statusChange.ToFailure<InventoryDispositionDto>();
            }

            disposition.MarkCompleted(
                actorUserId,
                statusChange.Value.Quantity,
                statusChange.Value.DestinationStockId,
                statusChange.Value.OutboundMovementId,
                statusChange.Value.InboundMovementId,
                clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryDispositionCompleted,
                    WmsAuditEntityTypes.InventoryDisposition,
                    disposition.DispositionNumber,
                    disposition.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = disposition.Status.ToString(),
                        ["quantity"] = disposition.CompletedQuantity,
                        ["targetStatusId"] = disposition.TargetInventoryStatusId,
                        ["outboundMovementId"] = disposition.OutboundMovementId,
                        ["inboundMovementId"] = disposition.InboundMovementId
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapDisposition(disposition));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            if (disposition is not null && disposition.Status == InventoryDispositionStatus.Executing)
            {
                disposition.MarkException(exception.Message);
                await context.SaveChangesAsync(CancellationToken.None);
            }

            return Result.Failure<InventoryDispositionDto>(WmsErrors.Conflict(
                "inventory.disposition.execution_state_conflict",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition execution failed for {DispositionId}", dispositionId);
            if (disposition is not null && disposition.Status == InventoryDispositionStatus.Executing)
            {
                disposition.MarkException("The disposition execution failed and requires review.");
                await context.SaveChangesAsync(CancellationToken.None);
            }

            return Result.Failure<InventoryDispositionDto>(WmsErrors.FromException(
                exception,
                "inventory.disposition_execute_failed",
                "The inventory disposition could not be executed."));
        }
    }

    public async Task<Result<InventoryRecallCaseDto>> CreateRecallAsync(
        InventoryRecallCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall.actor_required",
                "An authenticated actor is required."));
        }

        if (input.WarehouseId <= 0 ||
            (!input.ItemId.HasValue && !input.LotId.HasValue &&
             !input.SerialNumberId.HasValue && !input.LicensePlateId.HasValue))
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall.selector_required",
                "A warehouse and at least one item, lot, serial, or license plate selector are required."));
        }

        var key = input.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 250)
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall.idempotency_required",
                "A recall idempotency key between 1 and 250 characters is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryRecallCaseDto>();
        }

        try
        {
            var warehouse = await context.Warehouses
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryRecallCaseDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            var existing = await context.InventoryRecallCases
                .SingleOrDefaultAsync(
                    recallCase => recallCase.WarehouseId == input.WarehouseId &&
                                 recallCase.IdempotencyKey == key,
                    cancellationToken);
            if (existing is not null)
            {
                return Result.Success(MapRecall(existing));
            }

            if (string.IsNullOrWhiteSpace(input.Reason))
            {
                return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                    "inventory.recall.reason_required",
                    "A reason is required for every recall case."));
            }

            Lot? lot = null;
            if (input.LotId.HasValue)
            {
                lot = await context.Lots
                    .SingleOrDefaultAsync(value => value.Id == input.LotId.Value, cancellationToken);
                if (lot is null)
                {
                    return Result.Failure<InventoryRecallCaseDto>(WmsErrors.NotFound(
                        "lot.not_found",
                        "The recalled lot was not found."));
                }

                if (input.ItemId.HasValue && input.ItemId.Value != lot.ItemId)
                {
                    return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                        "inventory.recall.selector_mismatch",
                        "The lot does not belong to the selected item."));
                }

                if (lot.Status != LotStatus.Recalled)
                {
                    lot.SetStatus(
                        LotStatus.Recalled,
                        input.Reason,
                        changedAtUtc: clock.UtcNow.UtcDateTime);
                }
            }

            if (input.SerialNumberId.HasValue &&
                !await context.SerialNumbers.AnyAsync(
                    serial => serial.Id == input.SerialNumberId.Value &&
                              (!input.ItemId.HasValue || serial.ItemId == input.ItemId.Value),
                    cancellationToken))
            {
                return Result.Failure<InventoryRecallCaseDto>(WmsErrors.NotFound(
                    "serial.not_found",
                    "The recalled serial number was not found for the selected item."));
            }

            if (input.LicensePlateId.HasValue &&
                !await context.LicensePlates.AnyAsync(
                    plate => plate.Id == input.LicensePlateId.Value &&
                             plate.WarehouseId == input.WarehouseId,
                    cancellationToken))
            {
                return Result.Failure<InventoryRecallCaseDto>(WmsErrors.NotFound(
                    "license_plate.not_found",
                    "The recalled license plate was not found in the selected warehouse."));
            }

            var recallCase = new InventoryRecallCase(
                $"RCL-{Guid.NewGuid():N}",
                key,
                input.WarehouseId,
                input.ItemId ?? lot?.ItemId,
                input.LotId,
                input.SerialNumberId,
                input.LicensePlateId,
                input.Reason,
                actorUserId,
                input.ExternalReference,
                clock.UtcNow.UtcDateTime);
            await context.InventoryRecallCases.AddAsync(recallCase, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryRecallCreated,
                    WmsAuditEntityTypes.InventoryRecallCase,
                    recallCase.CaseNumber,
                    recallCase.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["itemId"] = recallCase.ItemId,
                        ["lotId"] = recallCase.LotId,
                        ["serialNumberId"] = recallCase.SerialNumberId,
                        ["licensePlateId"] = recallCase.LicensePlateId,
                        ["reason"] = recallCase.Reason,
                        ["status"] = recallCase.Status.ToString()
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapRecall(recallCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.BusinessRule(
                "inventory.recall_state_invalid",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory recall creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.FromException(
                exception,
                "inventory.recall_create_failed",
                "The inventory recall case could not be created."));
        }
    }

    public async Task<Result<InventoryRecallCaseDto>> CloseRecallAsync(
        int recallCaseId,
        string actorUserId,
        InventoryRecallCloseInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (recallCaseId <= 0 || string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall.close_invalid",
                "A positive recall case ID and authenticated actor are required."));
        }

        try
        {
            var recallCase = await context.InventoryRecallCases
                .SingleOrDefaultAsync(value => value.Id == recallCaseId, cancellationToken);
            if (recallCase is null)
            {
                return Result.Failure<InventoryRecallCaseDto>(WmsErrors.NotFound(
                    "inventory.recall_not_found",
                    "The requested recall case was not found."));
            }

            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                recallCase.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryRecallCaseDto>();
            }

            if (recallCase.Status == InventoryRecallStatus.Closed)
            {
                return Result.Success(MapRecall(recallCase));
            }

            recallCase.Close(actorUserId, input.Reason, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryRecallClosed,
                    WmsAuditEntityTypes.InventoryRecallCase,
                    recallCase.CaseNumber,
                    recallCase.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = recallCase.Status.ToString(),
                        ["closureReason"] = recallCase.ClosureReason
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapRecall(recallCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Conflict(
                "inventory.recall.close_state_conflict",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.Validation(
                "inventory.recall.close_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory recall close failed for {RecallCaseId}", recallCaseId);
            return Result.Failure<InventoryRecallCaseDto>(WmsErrors.FromException(
                exception,
                "inventory.recall_close_failed",
                "The inventory recall case could not be closed."));
        }
    }

    public async Task<Result<InventoryRecallTraceResultDto>> GetRecallTraceAsync(
        int recallCaseId,
        CancellationToken cancellationToken = default)
    {
        if (recallCaseId <= 0)
        {
            return Result.Failure<InventoryRecallTraceResultDto>(WmsErrors.Validation(
                "inventory.recall.id_invalid",
                "A positive recall case ID is required."));
        }

        try
        {
            var recallCase = await context.InventoryRecallCases
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == recallCaseId, cancellationToken);
            if (recallCase is null)
            {
                return Result.Failure<InventoryRecallTraceResultDto>(WmsErrors.NotFound(
                    "inventory.recall_not_found",
                    "The requested recall case was not found."));
            }

            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                recallCase.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryRecallTraceResultDto>();
            }

            var transactionsQuery = context.InventoryTransactions
                .AsNoTracking()
                .Where(transaction => transaction.WarehouseId == recallCase.WarehouseId);
            transactionsQuery = ApplyRecallSelectors(transactionsQuery, recallCase);
            var transactions = await transactionsQuery
                .OrderBy(transaction => transaction.OccurredAtUtc)
                .ThenBy(transaction => transaction.Id)
                .ToListAsync(cancellationToken);

            var balanceQuery = context.InventoryBalances
                .AsNoTracking()
                .Include(balance => balance.InventoryStatus)
                .Where(balance => balance.WarehouseId == recallCase.WarehouseId);
            balanceQuery = ApplyRecallSelectors(balanceQuery, recallCase);
            var balances = await balanceQuery.ToListAsync(cancellationToken);

            var stockQuery = context.Stock
                .AsNoTracking()
                .Include(stock => stock.InventoryStatus)
                .Include(stock => stock.Location)
                .Where(stock => stock.Location.WarehouseId == recallCase.WarehouseId);
            stockQuery = ApplyRecallSelectors(stockQuery, recallCase);
            var stock = await stockQuery.ToListAsync(cancellationToken);

            var trace = new List<InventoryRecallTraceDto>(transactions.Count + balances.Count + stock.Count);
            trace.AddRange(transactions.Select(transaction => new InventoryRecallTraceDto(
                "ledger",
                transaction.Id,
                null,
                transaction.ItemId,
                transaction.LocationId,
                transaction.LotId,
                transaction.SerialNumberId,
                transaction.LicensePlateId,
                transaction.InventoryStatusId,
                null,
                transaction.QuantityDelta,
                transaction.ReferenceType,
                transaction.ReferenceId,
                transaction.Type,
                transaction.OccurredAtUtc)));
            trace.AddRange(balances
                .Where(balance => balance.OnHandQuantity != 0m || balance.ReservedQuantity != 0m)
                .Select(balance => new InventoryRecallTraceDto(
                    "current-balance",
                    null,
                    balance.Id,
                    balance.ItemId,
                    balance.LocationId,
                    balance.LotId,
                    balance.SerialNumberId,
                    balance.LicensePlateId,
                    balance.InventoryStatusId,
                    balance.InventoryStatus.Code,
                    balance.OnHandQuantity,
                    null,
                    null,
                    null,
                    null)));
            trace.AddRange(stock
                .Where(value => value.QuantityAvailable.Value != 0m || value.QuantityReserved.Value != 0m)
                .Select(value => new InventoryRecallTraceDto(
                    "legacy-stock",
                    null,
                    value.Id,
                    value.ItemId,
                    value.LocationId,
                    value.LotId,
                    value.SerialNumberId,
                    value.LicensePlateId,
                    value.InventoryStatusId,
                    value.InventoryStatus?.Code,
                    value.QuantityAvailable.Value,
                    null,
                    null,
                    null,
                    null)));

            var statusIds = trace
                .Select(value => value.InventoryStatusId)
                .Distinct()
                .ToArray();
            var statusCodes = await context.InventoryStatuses
                .AsNoTracking()
                .Where(status => statusIds.Contains(status.Id))
                .ToDictionaryAsync(status => status.Id, status => status.Code, cancellationToken);
            trace = trace
                .Select(value => value with
                {
                    InventoryStatusCode = value.InventoryStatusCode ??
                        statusCodes.GetValueOrDefault(value.InventoryStatusId)
                })
                .ToList();

            return Result.Success(new InventoryRecallTraceResultDto(MapRecall(recallCase), trace));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory recall trace failed for {RecallCaseId}", recallCaseId);
            return Result.Failure<InventoryRecallTraceResultDto>(WmsErrors.FromException(
                exception,
                "inventory.recall_trace_failed",
                "The inventory recall trace could not be loaded."));
        }
    }

    public async Task<Result<InventoryDispositionMetricsDto>> GetMetricsAsync(
        InventoryDispositionMetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.WarehouseId <= 0)
        {
            return Result.Failure<InventoryDispositionMetricsDto>(WmsErrors.Validation(
                "inventory.disposition_metrics.warehouse_invalid",
                "A positive warehouse ID is required."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryDispositionMetricsDto>();
        }

        try
        {
            var dispositions = context.InventoryDispositions
                .AsNoTracking()
                .Where(disposition => disposition.WarehouseId == query.WarehouseId);
            if (query.FromUtc.HasValue)
            {
                dispositions = dispositions.Where(disposition =>
                    disposition.RequestedAtUtc >= NormalizeUtc(query.FromUtc.Value));
            }

            if (query.ToUtc.HasValue)
            {
                dispositions = dispositions.Where(disposition =>
                    disposition.RequestedAtUtc < NormalizeUtc(query.ToUtc.Value));
            }

            var rows = await dispositions.ToListAsync(cancellationToken);
            var openRecallCases = await context.InventoryRecallCases
                .AsNoTracking()
                .CountAsync(
                    recallCase => recallCase.WarehouseId == query.WarehouseId &&
                                  (recallCase.Status == InventoryRecallStatus.Open ||
                                   recallCase.Status == InventoryRecallStatus.Contained),
                    cancellationToken);
            return Result.Success(new InventoryDispositionMetricsDto(
                query.WarehouseId,
                rows.Count,
                rows.Sum(value => value.RequestedQuantity),
                rows.Sum(value => value.CompletedQuantity),
                openRecallCases,
                rows.GroupBy(value => value.Kind).ToDictionary(group => group.Key, group => group.Count()),
                rows.GroupBy(value => value.Status).ToDictionary(group => group.Key, group => group.Count())));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory disposition metrics failed for warehouse {WarehouseId}",
                query.WarehouseId);
            return Result.Failure<InventoryDispositionMetricsDto>(WmsErrors.FromException(
                exception,
                "inventory.disposition_metrics_failed",
                "Inventory disposition metrics could not be loaded."));
        }
    }

    private static IQueryable<InventoryTransaction> ApplyRecallSelectors(
        IQueryable<InventoryTransaction> query,
        InventoryRecallCase recallCase)
    {
        if (recallCase.ItemId.HasValue)
        {
            query = query.Where(value => value.ItemId == recallCase.ItemId.Value);
        }

        if (recallCase.LotId.HasValue)
        {
            query = query.Where(value => value.LotId == recallCase.LotId.Value);
        }

        if (recallCase.SerialNumberId.HasValue)
        {
            query = query.Where(value => value.SerialNumberId == recallCase.SerialNumberId.Value);
        }

        if (recallCase.LicensePlateId.HasValue)
        {
            query = query.Where(value => value.LicensePlateId == recallCase.LicensePlateId.Value);
        }

        return query;
    }

    private static IQueryable<InventoryBalance> ApplyRecallSelectors(
        IQueryable<InventoryBalance> query,
        InventoryRecallCase recallCase)
    {
        if (recallCase.ItemId.HasValue)
        {
            query = query.Where(value => value.ItemId == recallCase.ItemId.Value);
        }

        if (recallCase.LotId.HasValue)
        {
            query = query.Where(value => value.LotId == recallCase.LotId.Value);
        }

        if (recallCase.SerialNumberId.HasValue)
        {
            query = query.Where(value => value.SerialNumberId == recallCase.SerialNumberId.Value);
        }

        if (recallCase.LicensePlateId.HasValue)
        {
            query = query.Where(value => value.LicensePlateId == recallCase.LicensePlateId.Value);
        }

        return query;
    }

    private static IQueryable<Stock> ApplyRecallSelectors(
        IQueryable<Stock> query,
        InventoryRecallCase recallCase)
    {
        if (recallCase.ItemId.HasValue)
        {
            query = query.Where(value => value.ItemId == recallCase.ItemId.Value);
        }

        if (recallCase.LotId.HasValue)
        {
            query = query.Where(value => value.LotId == recallCase.LotId.Value);
        }

        if (recallCase.SerialNumberId.HasValue)
        {
            query = query.Where(value => value.SerialNumberId == recallCase.SerialNumberId.Value);
        }

        if (recallCase.LicensePlateId.HasValue)
        {
            query = query.Where(value => value.LicensePlateId == recallCase.LicensePlateId.Value);
        }

        return query;
    }

    private static InventoryDispositionPolicy? ResolvePolicy(
        IReadOnlyCollection<InventoryDispositionPolicy> policies,
        Item item,
        DateTime asOfUtc) =>
        policies
            .Where(policy => policy.IsEffective(asOfUtc) && policy.Matches(item.Id, item.Category))
            .OrderByDescending(policy => policy.ItemId.HasValue ? 2 :
                string.IsNullOrWhiteSpace(policy.ItemCategory) ? 0 : 1)
            .ThenByDescending(policy => policy.Priority)
            .ThenByDescending(policy => policy.EffectiveFromUtc)
            .FirstOrDefault();

    private static int ResolveTargetStatus(InventoryDispositionKind kind) => kind switch
    {
        InventoryDispositionKind.Quarantine => InventoryStatusSystemIds.Quarantine,
        InventoryDispositionKind.Hold => InventoryStatusSystemIds.Hold,
        InventoryDispositionKind.Damage => InventoryStatusSystemIds.Damaged,
        InventoryDispositionKind.Expire => InventoryStatusSystemIds.Expired,
        InventoryDispositionKind.Recall => InventoryStatusSystemIds.Quarantine,
        InventoryDispositionKind.Rework => InventoryStatusSystemIds.Quarantine,
        InventoryDispositionKind.ReturnToVendor => InventoryStatusSystemIds.ReturnPending,
        InventoryDispositionKind.Donation => InventoryStatusSystemIds.ScrapPending,
        InventoryDispositionKind.Alternate => InventoryStatusSystemIds.Hold,
        InventoryDispositionKind.Scrap => InventoryStatusSystemIds.ScrapPending,
        InventoryDispositionKind.Destruction => InventoryStatusSystemIds.ScrapPending,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static InventoryDispositionPolicyDto MapPolicy(
        InventoryDispositionPolicy policy,
        Warehouse warehouse) => new(
            policy.Id,
            policy.WarehouseId,
            warehouse.Code,
            policy.PolicyKey,
            policy.Name,
            policy.Priority,
            policy.ItemId,
            policy.ItemCategory,
            policy.WarningDays,
            policy.MinimumShelfLifeDays,
            policy.RequireApprovalForScrap,
            policy.RequireWitnessForDestruction,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private static InventoryDispositionDto MapDisposition(InventoryDisposition disposition) => new(
        disposition.Id,
        disposition.DispositionNumber,
        disposition.IdempotencyKey,
        disposition.WarehouseId,
        disposition.StockId,
        disposition.ItemId,
        disposition.LocationId,
        disposition.LotId,
        disposition.SerialNumberId,
        disposition.LicensePlateId,
        disposition.SourceInventoryStatusId,
        disposition.TargetInventoryStatusId,
        disposition.BaseUnitOfMeasure,
        disposition.Kind,
        disposition.Status,
        disposition.RequestedQuantity,
        disposition.CompletedQuantity,
        disposition.Reason,
        disposition.RequestedByUserId,
        disposition.ApprovedByUserId,
        disposition.ApprovedAtUtc,
        disposition.CompletedByUserId,
        disposition.CompletedAtUtc,
        disposition.RejectionReason,
        disposition.FailureReason,
        disposition.ReferenceNumber,
        disposition.WitnessUserId,
        disposition.ApprovalRequired,
        disposition.RequestedAtUtc,
        disposition.DestinationStockId,
        disposition.OutboundMovementId,
        disposition.InboundMovementId,
        disposition.Revision);

    private static InventoryRecallCaseDto MapRecall(InventoryRecallCase recallCase) => new(
        recallCase.Id,
        recallCase.CaseNumber,
        recallCase.IdempotencyKey,
        recallCase.WarehouseId,
        recallCase.ItemId,
        recallCase.LotId,
        recallCase.SerialNumberId,
        recallCase.LicensePlateId,
        recallCase.Reason,
        recallCase.CreatedByUserId,
        recallCase.ExternalReference,
        recallCase.CreatedAtUtc,
        recallCase.Status,
        recallCase.ClosedByUserId,
        recallCase.ClosedAtUtc,
        recallCase.ClosureReason,
        recallCase.Revision);

    private static DateTime NormalizeUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
