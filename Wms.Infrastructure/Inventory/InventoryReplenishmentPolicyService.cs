using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

public sealed class InventoryReplenishmentPolicyService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InventoryReplenishmentPolicyService> logger)
    : IInventoryReplenishmentPolicyService
{
    private const int MaximumSignalLimit = 1_000;

    public async Task<Result<InventoryReplenishmentPolicyDto>> SaveAsync(
        int? policyId,
        InventoryReplenishmentPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.Validation(
                "inventory.policy_actor_required",
                "An authenticated actor is required."));
        }

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryAdjust,
                input.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryReplenishmentPolicyDto>();
            }

            var item = await context.Items
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == input.ItemId, cancellationToken);
            if (item is null || !item.IsActive)
            {
                return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The requested active item was not found."));
            }

            var warehouse = await context.Warehouses
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            Location? location = null;
            if (input.LocationId.HasValue)
            {
                location = await context.Locations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate =>
                        candidate.Id == input.LocationId.Value &&
                        candidate.WarehouseId == input.WarehouseId &&
                        candidate.IsActive,
                        cancellationToken);
                if (location is null)
                {
                    return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.NotFound(
                        "location.not_found",
                        "The requested active location was not found in the warehouse."));
                }
            }

            var overlapping = await context.InventoryReplenishmentPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.IsActive &&
                    policy.ItemId == input.ItemId &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.LocationId == input.LocationId &&
                    policy.EffectiveFromUtc <
                        (input.EffectiveToUtc ?? DateTime.MaxValue) &&
                    (!policy.EffectiveToUtc.HasValue ||
                     policy.EffectiveToUtc.Value > input.EffectiveFromUtc),
                    cancellationToken);
            if (overlapping)
            {
                return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.Conflict(
                    "inventory.policy_effective_overlap",
                    "Another active policy overlaps the requested effective period."));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);
            InventoryReplenishmentPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.InventoryReplenishmentPolicies
                    .SingleOrDefaultAsync(candidate => candidate.Id == policyId.Value,
                        cancellationToken)
                    ?? throw new KeyNotFoundException($"Policy {policyId.Value} was not found.");

                if (policy.ItemId != input.ItemId ||
                    policy.WarehouseId != input.WarehouseId ||
                    policy.LocationId != input.LocationId)
                {
                    return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.Conflict(
                        "inventory.policy_identity_immutable",
                        "An existing policy cannot be moved to another item, warehouse, or location."));
                }

                policy.Update(
                    input.MinimumQuantity,
                    input.MaximumQuantity,
                    input.SafetyStockQuantity,
                    input.ReorderPointQuantity,
                    input.TargetQuantity,
                    input.QuantityBasis,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.PreferredSource,
                    input.LeadTimeDays);
            }
            else
            {
                policy = new InventoryReplenishmentPolicy(
                    input.ItemId,
                    input.WarehouseId,
                    input.LocationId,
                    input.MinimumQuantity,
                    input.MaximumQuantity,
                    input.SafetyStockQuantity,
                    input.ReorderPointQuantity,
                    input.TargetQuantity,
                    input.QuantityBasis,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc,
                    input.PreferredSource,
                    input.LeadTimeDays);
                await context.InventoryReplenishmentPolicies.AddAsync(policy, cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReplenishmentPolicyChanged,
                    WmsAuditEntityTypes.ReplenishmentPolicy,
                    policyId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["itemId"] = input.ItemId,
                        ["locationId"] = input.LocationId,
                        ["minimumQuantity"] = input.MinimumQuantity,
                        ["maximumQuantity"] = input.MaximumQuantity,
                        ["safetyStockQuantity"] = input.SafetyStockQuantity,
                        ["reorderPointQuantity"] = input.ReorderPointQuantity,
                        ["targetQuantity"] = input.TargetQuantity,
                        ["quantityBasis"] = input.QuantityBasis.ToString(),
                        ["effectiveFromUtc"] = input.EffectiveFromUtc,
                        ["effectiveToUtc"] = input.EffectiveToUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success(MapPolicy(policy, item, warehouse, location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            logger.LogWarning(exception, "Replenishment policy was not found during save");
            return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.NotFound(
                "inventory.policy_not_found",
                "The requested replenishment policy was not found."));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Invalid replenishment policy input");
            return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.Validation(
                "inventory.policy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Replenishment policy save failed");
            return Result.Failure<InventoryReplenishmentPolicyDto>(WmsErrors.FromException(
                exception,
                "inventory.policy_save_failed",
                "The replenishment policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryReplenishmentPolicyDto>>> SearchAsync(
        InventoryReplenishmentPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IReadOnlyList<InventoryReplenishmentPolicyDto>>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = ApplyPolicyScope(context.InventoryReplenishmentPolicies.AsNoTracking(), scope)
                .Where(policy => query.IncludeInactive || policy.IsActive);
            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                policies = policies.Where(policy => policy.ItemId == query.ItemId.Value);
            }

            if (query.LocationId.HasValue)
            {
                policies = policies.Where(policy => policy.LocationId == query.LocationId.Value);
            }

            var rows = await policies
                .Include(policy => policy.Item)
                .Include(policy => policy.Warehouse)
                .Include(policy => policy.Location)
                .OrderBy(policy => policy.Warehouse.Code)
                .ThenBy(policy => policy.Item.Sku)
                .ThenBy(policy => policy.LocationId)
                .ThenByDescending(policy => policy.EffectiveFromUtc)
                .Take(2_000)
                .ToListAsync(cancellationToken);

            return Result.Success<IReadOnlyList<InventoryReplenishmentPolicyDto>>(
                rows.Select(policy => MapPolicy(
                    policy,
                    policy.Item,
                    policy.Warehouse,
                    policy.Location)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Replenishment policy search failed");
            return Result.Failure<IReadOnlyList<InventoryReplenishmentPolicyDto>>(WmsErrors.FromException(
                exception,
                "inventory.policy_search_failed",
                "Replenishment policies could not be loaded."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryReplenishmentSignalDto>>> GetSignalsAsync(
        InventoryReplenishmentSignalQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<IReadOnlyList<InventoryReplenishmentSignalDto>>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var now = clock.UtcNow.UtcDateTime;
            var policies = ApplyPolicyScope(context.InventoryReplenishmentPolicies.AsNoTracking(), scope)
                .Where(policy =>
                    policy.IsActive &&
                    policy.EffectiveFromUtc <= now &&
                    (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > now));
            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                policies = policies.Where(policy => policy.ItemId == query.ItemId.Value);
            }

            if (query.LocationId.HasValue)
            {
                policies = policies.Where(policy => policy.LocationId == query.LocationId.Value);
            }

            var policyRows = await policies
                .Include(policy => policy.Item)
                .Include(policy => policy.Warehouse)
                .Include(policy => policy.Location)
                .ToListAsync(cancellationToken);

            var balances = ApplyBalanceScope(context.InventoryBalances.AsNoTracking(), scope);
            if (query.WarehouseId.HasValue)
            {
                balances = balances.Where(balance => balance.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                balances = balances.Where(balance => balance.ItemId == query.ItemId.Value);
            }

            if (query.LocationId.HasValue)
            {
                balances = balances.Where(balance => balance.LocationId == query.LocationId.Value);
            }

            var aggregateRows = await balances
                .GroupBy(balance => new
                {
                    balance.WarehouseId,
                    balance.ItemId,
                    balance.LocationId
                })
                .Select(group => new BalanceAggregate(
                    group.Key.WarehouseId,
                    group.Key.ItemId,
                    group.Key.LocationId,
                    group.Sum(balance => balance.OnHandQuantity),
                    group.Sum(balance => balance.ReservedQuantity),
                    group.Sum(balance =>
                        balance.OnHandQuantity - balance.ReservedQuantity),
                    group.Sum(balance =>
                        balance.InventoryStatus.IsActive &&
                        balance.InventoryStatus.IsAllocatable &&
                        balance.Item.IsActive &&
                        balance.Location.IsActive &&
                        balance.OnHandQuantity > balance.ReservedQuantity
                            ? balance.OnHandQuantity - balance.ReservedQuantity
                            : 0m)))
                .ToListAsync(cancellationToken);

            var signals = new List<InventoryReplenishmentSignalDto>();
            foreach (var policy in policyRows)
            {
                var aggregate = aggregateRows
                    .Where(row =>
                        row.WarehouseId == policy.WarehouseId &&
                        row.ItemId == policy.ItemId &&
                        (!policy.LocationId.HasValue || row.LocationId == policy.LocationId.Value))
                    .Aggregate(BalanceAggregate.Empty(policy.WarehouseId, policy.ItemId),
                        BalanceAggregate.Add);

                var evaluatedQuantity = policy.QuantityBasis switch
                {
                    InventoryPolicyQuantityBasis.OnHand => aggregate.OnHandQuantity,
                    InventoryPolicyQuantityBasis.AvailableToPromise =>
                        aggregate.AvailableToPromiseQuantity,
                    _ => aggregate.PhysicalAvailableQuantity
                };
                var signalKind = DetermineSignal(policy, evaluatedQuantity);
                if (!signalKind.HasValue)
                {
                    continue;
                }

                signals.Add(new InventoryReplenishmentSignalDto(
                    policy.Id,
                    policy.ItemId,
                    policy.Item.Sku,
                    policy.Item.Name,
                    policy.WarehouseId,
                    policy.Warehouse.Code,
                    policy.LocationId,
                    policy.Location?.Code,
                    policy.QuantityBasis,
                    evaluatedQuantity,
                    aggregate.OnHandQuantity,
                    aggregate.ReservedQuantity,
                    aggregate.PhysicalAvailableQuantity,
                    aggregate.AvailableToPromiseQuantity,
                    0m,
                    0m,
                    policy.MinimumQuantity,
                    policy.SafetyStockQuantity,
                    policy.ReorderPointQuantity,
                    policy.TargetQuantity,
                    policy.MaximumQuantity,
                    signalKind.Value,
                    Math.Max(0m, policy.TargetQuantity - evaluatedQuantity),
                    now));
            }

            var limit = query.Limit switch
            {
                < 1 => 1,
                > MaximumSignalLimit => MaximumSignalLimit,
                _ => query.Limit
            };
            return Result.Success<IReadOnlyList<InventoryReplenishmentSignalDto>>(
                signals
                    .OrderBy(signal => signal.SignalKind)
                    .ThenBy(signal => signal.WarehouseCode)
                    .ThenBy(signal => signal.ItemSku)
                    .ThenBy(signal => signal.LocationCode)
                    .Take(limit)
                    .ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Replenishment signal evaluation failed");
            return Result.Failure<IReadOnlyList<InventoryReplenishmentSignalDto>>(WmsErrors.FromException(
                exception,
                "inventory.signal_evaluation_failed",
                "Replenishment signals could not be evaluated."));
        }
    }

    private static InventoryReplenishmentSignalKind? DetermineSignal(
        InventoryReplenishmentPolicy policy,
        decimal evaluatedQuantity) =>
        evaluatedQuantity <= 0m
            ? InventoryReplenishmentSignalKind.OutOfStock
            : evaluatedQuantity < policy.ReorderPointQuantity
                ? InventoryReplenishmentSignalKind.LowStock
                : evaluatedQuantity > policy.MaximumQuantity
                    ? InventoryReplenishmentSignalKind.Overstock
                    : null;

    private static InventoryReplenishmentPolicyDto MapPolicy(
        InventoryReplenishmentPolicy policy,
        Item item,
        Warehouse warehouse,
        Location? location) =>
        new(
            policy.Id,
            policy.ItemId,
            item.Sku,
            item.Name,
            policy.WarehouseId,
            warehouse.Code,
            policy.LocationId,
            location?.Code,
            policy.MinimumQuantity,
            policy.MaximumQuantity,
            policy.SafetyStockQuantity,
            policy.ReorderPointQuantity,
            policy.TargetQuantity,
            policy.QuantityBasis,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.PreferredSource,
            policy.LeadTimeDays,
            policy.IsActive,
            policy.Revision);

    private static IQueryable<InventoryReplenishmentPolicy> ApplyPolicyScope(
        IQueryable<InventoryReplenishmentPolicy> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(policy => scope.WarehouseIds.Contains(policy.WarehouseId));

    private static IQueryable<InventoryBalance> ApplyBalanceScope(
        IQueryable<InventoryBalance> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(balance => scope.WarehouseIds.Contains(balance.WarehouseId));

    private sealed record BalanceAggregate(
        int WarehouseId,
        int ItemId,
        int LocationId,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal PhysicalAvailableQuantity,
        decimal AvailableToPromiseQuantity)
    {
        public static BalanceAggregate Empty(int warehouseId, int itemId) =>
            new(warehouseId, itemId, 0, 0m, 0m, 0m, 0m);

        public static BalanceAggregate Add(BalanceAggregate left, BalanceAggregate right) =>
            new(
                left.WarehouseId,
                left.ItemId,
                left.LocationId,
                left.OnHandQuantity + right.OnHandQuantity,
                left.ReservedQuantity + right.ReservedQuantity,
                left.PhysicalAvailableQuantity + right.PhysicalAvailableQuantity,
                left.AvailableToPromiseQuantity + right.AvailableToPromiseQuantity);
    }
}
