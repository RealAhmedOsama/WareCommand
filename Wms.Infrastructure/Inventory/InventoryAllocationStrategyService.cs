using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Time;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Resolves effective allocation policy and produces a deterministic candidate
/// order. The resolver never mutates inventory; reservations remain the only
/// caller that can turn the order into ledger-backed allocations.
/// </summary>
public sealed class InventoryAllocationStrategyService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InventoryAllocationStrategyService> logger)
    : IInventoryAllocationStrategyService
{
    private static readonly InventoryTransactionType[] ReceiptTransactionTypes =
    [
        InventoryTransactionType.OpeningBalance,
        InventoryTransactionType.Receipt,
        InventoryTransactionType.Putaway,
        InventoryTransactionType.Return
    ];

    public async Task<Result<InventoryAllocationStrategyPolicyDto>> SaveAsync(
        int? policyId,
        InventoryAllocationStrategyPolicyInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.Validation(
                "inventory.allocation_strategy_actor_required",
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
                return authorization.ToFailure<InventoryAllocationStrategyPolicyDto>();
            }

            var warehouse = await context.Warehouses
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested active warehouse was not found."));
            }

            Item? item = null;
            if (input.ItemId.HasValue)
            {
                item = await context.Items
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == input.ItemId.Value && candidate.IsActive,
                        cancellationToken);
                if (item is null)
                {
                    return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.NotFound(
                        "item.not_found",
                        "The requested active item was not found."));
                }
            }

            Location? fixedLocation = null;
            if (input.FixedLocationId.HasValue)
            {
                fixedLocation = await context.Locations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == input.FixedLocationId.Value &&
                                    candidate.WarehouseId == input.WarehouseId &&
                                    candidate.IsActive,
                        cancellationToken);
                if (fixedLocation is null)
                {
                    return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.NotFound(
                        "location.not_found",
                        "The requested fixed location was not found in the warehouse."));
                }
            }

            var normalizedKey = NormalizeRequired(input.PolicyKey, 80, nameof(input.PolicyKey));
            var normalizedCategory = NormalizeOptional(input.ItemCategory);
            var normalizedDemandType = NormalizeOptional(input.DemandType);
            var duplicateKey = await context.InventoryAllocationStrategyPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.PolicyKey == normalizedKey,
                    cancellationToken);
            if (duplicateKey)
            {
                return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.Conflict(
                    "inventory.allocation_strategy_key_exists",
                    "The policy key is already used in this warehouse."));
            }

            var overlap = await context.InventoryAllocationStrategyPolicies
                .AsNoTracking()
                .AnyAsync(policy =>
                    policy.Id != (policyId ?? 0) &&
                    policy.WarehouseId == input.WarehouseId &&
                    policy.ItemId == input.ItemId &&
                    policy.ItemCategory == normalizedCategory &&
                    policy.DemandType == normalizedDemandType &&
                    policy.IsActive &&
                    policy.EffectiveFromUtc <
                        (NormalizeUtc(input.EffectiveToUtc) ?? DateTime.MaxValue) &&
                    (!policy.EffectiveToUtc.HasValue ||
                     policy.EffectiveToUtc.Value > NormalizeUtc(input.EffectiveFromUtc)),
                    cancellationToken);
            if (overlap)
            {
                return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.Conflict(
                    "inventory.allocation_strategy_effective_overlap",
                    "Another active policy with the same selector scope overlaps the requested effective period."));
            }

            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);
            InventoryAllocationStrategyPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.InventoryAllocationStrategyPolicies
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == policyId.Value,
                        cancellationToken)
                    ?? throw new KeyNotFoundException($"Policy {policyId.Value} was not found.");
                if (policy.WarehouseId != input.WarehouseId ||
                    policy.ItemId != input.ItemId ||
                    !string.Equals(policy.ItemCategory, normalizedCategory, StringComparison.Ordinal) ||
                    !string.Equals(policy.DemandType, normalizedDemandType, StringComparison.Ordinal))
                {
                    return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.Conflict(
                        "inventory.allocation_strategy_identity_immutable",
                        "An existing policy cannot be moved to another warehouse or selector scope."));
                }

                policy.Update(
                    normalizedKey,
                    input.ItemId,
                    normalizedCategory,
                    normalizedDemandType,
                    input.Strategy,
                    input.FixedLocationId,
                    input.PreferWholeLicensePlate,
                    input.MinimumShelfLifeDays,
                    input.MissingExpiryFallback,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc);
            }
            else
            {
                policy = new InventoryAllocationStrategyPolicy(
                    input.WarehouseId,
                    normalizedKey,
                    input.ItemId,
                    normalizedCategory,
                    normalizedDemandType,
                    input.Strategy,
                    input.FixedLocationId,
                    input.PreferWholeLicensePlate,
                    input.MinimumShelfLifeDays,
                    input.MissingExpiryFallback,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc);
                await context.InventoryAllocationStrategyPolicies.AddAsync(
                    policy,
                    cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InventoryAllocationStrategyPolicyChanged,
                    WmsAuditEntityTypes.InventoryAllocationStrategyPolicy,
                    policyId?.ToString(CultureInfo.InvariantCulture) ?? "new",
                    input.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyKey"] = normalizedKey,
                        ["itemId"] = input.ItemId,
                        ["itemCategory"] = normalizedCategory,
                        ["demandType"] = normalizedDemandType,
                        ["strategy"] = input.Strategy.ToString(),
                        ["fixedLocationId"] = input.FixedLocationId,
                        ["preferWholeLicensePlate"] = input.PreferWholeLicensePlate,
                        ["minimumShelfLifeDays"] = input.MinimumShelfLifeDays,
                        ["missingExpiryFallback"] = input.MissingExpiryFallback.ToString(),
                        ["effectiveFromUtc"] = input.EffectiveFromUtc,
                        ["effectiveToUtc"] = input.EffectiveToUtc
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result.Success(MapPolicy(policy, warehouse, item, fixedLocation));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            logger.LogWarning(exception, "Allocation strategy policy was not found during save");
            return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.NotFound(
                "inventory.allocation_strategy_not_found",
                "The requested allocation strategy policy was not found."));
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Invalid allocation strategy policy input");
            return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.Validation(
                "inventory.allocation_strategy_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Allocation strategy policy save failed");
            return Result.Failure<InventoryAllocationStrategyPolicyDto>(WmsErrors.FromException(
                exception,
                "inventory.allocation_strategy_save_failed",
                "The allocation strategy policy could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<InventoryAllocationStrategyPolicyDto>>> SearchAsync(
        InventoryAllocationStrategyPolicyQuery query,
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
                return authorization.ToFailure<IReadOnlyList<InventoryAllocationStrategyPolicyDto>>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var policies = scope.HasGlobalAccess
                ? context.InventoryAllocationStrategyPolicies.AsNoTracking()
                : context.InventoryAllocationStrategyPolicies.AsNoTracking()
                    .Where(policy => scope.WarehouseIds.Contains(policy.WarehouseId));
            if (query.WarehouseId.HasValue)
            {
                policies = policies.Where(policy => policy.WarehouseId == query.WarehouseId.Value);
            }

            if (query.ItemId.HasValue)
            {
                policies = policies.Where(policy => policy.ItemId == query.ItemId.Value);
            }

            var category = NormalizeOptional(query.ItemCategory);
            if (category is not null)
            {
                policies = policies.Where(policy => policy.ItemCategory == category);
            }

            var demandType = NormalizeOptional(query.DemandType);
            if (demandType is not null)
            {
                policies = policies.Where(policy => policy.DemandType == demandType);
            }

            if (!query.IncludeInactive)
            {
                policies = policies.Where(policy => policy.IsActive);
            }

            var rows = await policies
                .Include(policy => policy.Warehouse)
                .Include(policy => policy.Item)
                .Include(policy => policy.FixedLocation)
                .OrderBy(policy => policy.Warehouse.Code)
                .ThenBy(policy => policy.ItemCategory)
                .ThenBy(policy => policy.ItemId)
                .ThenBy(policy => policy.DemandType)
                .ThenByDescending(policy => policy.EffectiveFromUtc)
                .Take(2_000)
                .ToListAsync(cancellationToken);

            return Result.Success<IReadOnlyList<InventoryAllocationStrategyPolicyDto>>(
                rows.Select(policy => MapPolicy(
                    policy,
                    policy.Warehouse,
                    policy.Item,
                    policy.FixedLocation)).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Allocation strategy policy search failed");
            return Result.Failure<IReadOnlyList<InventoryAllocationStrategyPolicyDto>>(WmsErrors.FromException(
                exception,
                "inventory.allocation_strategy_search_failed",
                "Allocation strategy policies could not be loaded."));
        }
    }

    public async Task<InventoryAllocationStrategyResolution> ResolveAsync(
        InventoryReservationRequest request,
        Item item,
        Warehouse warehouse,
        IReadOnlyList<InventoryBalance> candidates,
        DateOnly businessDate,
        InventoryAllocationStrategySnapshot? pinnedSnapshot = null,
        DateTime? asOfUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(warehouse);
        ArgumentNullException.ThrowIfNull(candidates);

        var snapshot = pinnedSnapshot ?? await ResolveSnapshotAsync(
            request,
            item,
            warehouse.Id,
            asOfUtc ?? UtcNow(),
            cancellationToken);
        var receiptDates = await LoadReceiptDatesAsync(
            warehouse.Id,
            item.Id,
            candidates,
            cancellationToken);
        var rejected = new Dictionary<int, string>();
        var eligible = new List<InventoryBalance>(candidates.Count);
        foreach (var balance in candidates)
        {
            if (balance.Lot is not null &&
                !balance.Lot.IsAllocationEligible(businessDate))
            {
                rejected[balance.Id] = "The lot is expired, blocked, or outside its allocation window.";
                continue;
            }

            if (snapshot.Strategy == InventoryAllocationStrategyKind.FixedLocation &&
                balance.LocationId != snapshot.FixedLocationId)
            {
                rejected[balance.Id] =
                    $"Rejected by fixed-location policy; expected location {snapshot.FixedLocationId}.";
                continue;
            }

            if (snapshot.MinimumShelfLifeDays > 0 &&
                balance.Lot?.ExpiryDate is DateTime expiryDate &&
                DateOnly.FromDateTime(expiryDate) < businessDate.AddDays(snapshot.MinimumShelfLifeDays))
            {
                rejected[balance.Id] =
                    $"Rejected because expiry {DateOnly.FromDateTime(expiryDate):yyyy-MM-dd} does not meet the {snapshot.MinimumShelfLifeDays}-day minimum shelf life.";
                continue;
            }

            eligible.Add(balance);
        }

        var ordered = Order(
            eligible,
            snapshot,
            receiptDates,
            request.RequestedQuantity).ToArray();
        var reasons = new Dictionary<int, string>();
        foreach (var balance in ordered)
        {
            reasons[balance.Id] = BuildSelectionReason(
                balance,
                snapshot,
                receiptDates.GetValueOrDefault(balance.Id, balance.CreatedAt));
        }

        return new InventoryAllocationStrategyResolution(
            snapshot,
            ordered,
            rejected,
            reasons);
    }

    public async Task<Result<InventoryAllocationSimulationResultDto>> SimulateAsync(
        InventoryAllocationSimulationInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RequestedQuantity <= 0m)
        {
            return Result.Failure<InventoryAllocationSimulationResultDto>(WmsErrors.Validation(
                "inventory.allocation_strategy_quantity_invalid",
                "Requested quantity must be greater than zero."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryAllocationSimulationResultDto>();
        }

        var item = await context.Items
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == input.ItemId,
                cancellationToken);
        var warehouse = await context.Warehouses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                cancellationToken);
        if (item is null || warehouse is null)
        {
            return Result.Failure<InventoryAllocationSimulationResultDto>(WmsErrors.NotFound(
                "inventory.allocation_strategy_scope_not_found",
                "The requested active item or warehouse was not found."));
        }

        var query = context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Location)
            .Include(balance => balance.Lot)
            .Include(balance => balance.LicensePlate)
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Item)
            .Include(balance => balance.InventoryStatus)
            .Where(balance => balance.WarehouseId == input.WarehouseId &&
                              balance.ItemId == input.ItemId &&
                              balance.BaseUnitOfMeasure == item.UnitOfMeasure);
        query = ApplySelector(query, input.Selector);
        var balances = await query.ToListAsync(cancellationToken);
        var businessDate = WmsBusinessTime.GetBusinessDate(
            input.AsOfUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(input.AsOfUtc.Value, DateTimeKind.Utc))
                : clock.UtcNow,
            warehouse.TimeZone);
        var resolution = await ResolveAsync(
            new InventoryReservationRequest(
                input.DemandType,
                input.DemandId,
                input.DemandLine,
                input.WarehouseId,
                input.ItemId,
                input.RequestedQuantity,
                Selector: input.Selector,
                ActorUserId: actorUserId),
            item,
            warehouse,
            balances,
            businessDate,
            asOfUtc: input.AsOfUtc,
            cancellationToken: cancellationToken);

        var remaining = input.RequestedQuantity;
        var selectedQuantities = new Dictionary<int, decimal>();
        foreach (var balance in resolution.OrderedCandidates.Where(balance =>
                     GetEligibilityReason(
                         balance,
                         item,
                         input.WarehouseId,
                         businessDate) is null))
        {
            if (remaining <= 0m)
            {
                break;
            }

            var quantity = Math.Min(Math.Max(0m, balance.AvailableQuantity), remaining);
            if (quantity <= 0m)
            {
                continue;
            }

            selectedQuantities[balance.Id] = quantity;
            remaining -= quantity;
        }

        var rankById = resolution.OrderedCandidates
            .Select((balance, index) => new { balance.Id, Rank = index + 1 })
            .ToDictionary(value => value.Id, value => value.Rank);
        var receiptDates = await LoadReceiptDatesAsync(
            input.WarehouseId,
            input.ItemId,
            balances,
            cancellationToken);
        var candidates = balances
            .OrderBy(balance => rankById.GetValueOrDefault(balance.Id, int.MaxValue))
            .ThenBy(balance => balance.Id)
            .Select(balance =>
            {
                var eligibilityReason = GetEligibilityReason(
                    balance,
                    item,
                    input.WarehouseId,
                    businessDate);
                var strategyRejected = resolution.RejectedReasons.TryGetValue(
                    balance.Id,
                    out var rejectedReason);
                var selected = selectedQuantities.ContainsKey(balance.Id);
                var rejected = eligibilityReason is not null || strategyRejected;
                var explanation = selected
                    ? resolution.SelectionReasons[balance.Id]
                    : eligibilityReason ?? (strategyRejected
                        ? rejectedReason ?? "The balance was rejected by the effective allocation strategy."
                        : "The balance was not selected after higher-ranked eligible candidates satisfied the demand.");
                return new InventoryAllocationCandidateDto(
                    balance.Id,
                    balance.LocationId,
                    balance.LotId,
                    balance.LicensePlateId,
                    balance.AvailableQuantity,
                    balance.Lot?.ExpiryDate,
                    receiptDates.GetValueOrDefault(balance.Id, balance.CreatedAt),
                    selected,
                    selected ? "selected" : rejected ? "rejected" : "not_selected",
                    explanation,
                    rankById.GetValueOrDefault(balance.Id));
            })
            .ToArray();

        return Result.Success(new InventoryAllocationSimulationResultDto(
            input.WarehouseId,
            input.ItemId,
            input.RequestedQuantity,
            input.RequestedQuantity - remaining,
            remaining,
            resolution.Snapshot,
            candidates));
    }

    private async Task<InventoryAllocationStrategySnapshot> ResolveSnapshotAsync(
        InventoryReservationRequest request,
        Item item,
        int warehouseId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var policies = await context.InventoryAllocationStrategyPolicies
            .AsNoTracking()
            .Where(policy =>
                policy.WarehouseId == warehouseId &&
                policy.IsActive &&
                policy.EffectiveFromUtc <= asOfUtc &&
                (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > asOfUtc))
            .ToListAsync(cancellationToken);
        var policy = policies
            .Where(candidate =>
                (!candidate.ItemId.HasValue || candidate.ItemId == item.Id) &&
                (candidate.ItemCategory is null ||
                 string.Equals(candidate.ItemCategory, item.Category, StringComparison.OrdinalIgnoreCase)) &&
                (candidate.DemandType is null ||
                 string.Equals(candidate.DemandType, request.DemandType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(candidate => Specificity(candidate, item, request))
            .ThenByDescending(candidate => candidate.EffectiveFromUtc)
            .ThenByDescending(candidate => candidate.Revision)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefault();

        if (policy is null)
        {
            var strategy = item.UseFefo
                ? InventoryAllocationStrategyKind.Fefo
                : InventoryAllocationStrategyKind.Fifo;
            return new InventoryAllocationStrategySnapshot(
                null,
                item.UseFefo ? "legacy:item-fefo" : "legacy:fifo",
                strategy,
                0,
                null,
                false,
                0,
                InventoryAllocationMissingExpiryFallback.Last);
        }

        return ToSnapshot(policy);
    }

    private async Task<Dictionary<int, DateTime>> LoadReceiptDatesAsync(
        int warehouseId,
        int itemId,
        IReadOnlyList<InventoryBalance> candidates,
        CancellationToken cancellationToken)
    {
        var transactions = await context.InventoryTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.WarehouseId == warehouseId &&
                transaction.ItemId == itemId &&
                ReceiptTransactionTypes.Contains(transaction.Type) &&
                transaction.QuantityDelta > 0m)
            .OrderBy(transaction => transaction.OccurredAtUtc)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
        var result = new Dictionary<int, DateTime>();
        foreach (var balance in candidates)
        {
            var receipt = transactions.FirstOrDefault(transaction => SameDimension(transaction, balance));
            result[balance.Id] = receipt?.OccurredAtUtc ?? balance.CreatedAt;
        }

        return result;
    }

    private static IEnumerable<InventoryBalance> Order(
        IEnumerable<InventoryBalance> candidates,
        InventoryAllocationStrategySnapshot snapshot,
        IReadOnlyDictionary<int, DateTime> receiptDates,
        decimal requestedQuantity)
    {
        Func<InventoryBalance, DateTime> receipt = balance =>
            receiptDates.GetValueOrDefault(balance.Id, balance.CreatedAt);
        Func<InventoryBalance, DateTime> expiry = balance =>
            balance.Lot?.ExpiryDate ??
            (snapshot.MissingExpiryFallback == InventoryAllocationMissingExpiryFallback.ReceiptDate
                ? receipt(balance)
                : DateTime.MaxValue);

        IOrderedEnumerable<InventoryBalance> ordered = snapshot.Strategy switch
        {
            InventoryAllocationStrategyKind.Fefo => candidates
                .OrderBy(expiry)
                .ThenBy(receipt)
                .ThenBy(balance => balance.Location?.Priority ?? int.MaxValue)
                .ThenBy(balance => balance.Id),
            InventoryAllocationStrategyKind.Lifo => candidates
                .OrderByDescending(receipt)
                .ThenBy(balance => balance.Location?.Priority ?? int.MaxValue)
                .ThenBy(balance => balance.Id),
            InventoryAllocationStrategyKind.FixedLocation => candidates
                .OrderBy(receipt)
                .ThenBy(balance => balance.Location?.Priority ?? int.MaxValue)
                .ThenBy(balance => balance.Id),
            InventoryAllocationStrategyKind.LocationPriority or
            InventoryAllocationStrategyKind.Nearest or
            InventoryAllocationStrategyKind.RoutePriority => candidates
                .OrderBy(balance => balance.Location?.Priority ?? int.MaxValue)
                .ThenBy(receipt)
                .ThenBy(balance => balance.Id),
            _ => candidates
                .OrderBy(receipt)
                .ThenBy(balance => balance.Location?.Priority ?? int.MaxValue)
                .ThenBy(balance => balance.Id)
        };

        if (!snapshot.PreferWholeLicensePlate)
        {
            return ordered;
        }

        var orderedCandidates = ordered.ToArray();
        var wholeGroups = orderedCandidates
            .Where(balance => balance.LicensePlateId.HasValue)
            .GroupBy(balance => balance.LicensePlateId!.Value)
            .Where(group => group.Sum(balance => Math.Max(0m, balance.AvailableQuantity)) >= requestedQuantity)
            .Select(group => new
            {
                Group = group,
                FirstIndex = orderedCandidates
                    .Select((balance, index) => new { balance, index })
                    .First(value => value.balance.LicensePlateId == group.Key)
                    .index
            })
            .OrderBy(value => value.FirstIndex)
            .FirstOrDefault();
        if (wholeGroups is null)
        {
            return orderedCandidates;
        }

        var wholeLpn = wholeGroups.Group
            .OrderBy(balance => Array.IndexOf(orderedCandidates, balance))
            .ToArray();
        var loose = orderedCandidates
            .Where(balance => balance.LicensePlateId != wholeGroups.Group.Key)
            .ToArray();
        return wholeLpn.Concat(loose);
    }

    private static string BuildSelectionReason(
        InventoryBalance balance,
        InventoryAllocationStrategySnapshot snapshot,
        DateTime receiptDate)
    {
        var strategy = snapshot.Strategy switch
        {
            InventoryAllocationStrategyKind.Fefo => "FEFO expiry date",
            InventoryAllocationStrategyKind.Lifo => "LIFO receipt date",
            InventoryAllocationStrategyKind.FixedLocation => "fixed location",
            InventoryAllocationStrategyKind.LocationPriority => "location priority",
            InventoryAllocationStrategyKind.Nearest => "nearest location priority",
            InventoryAllocationStrategyKind.RoutePriority => "route location priority",
            _ => "FIFO receipt date"
        };
        var expiry = balance.Lot?.ExpiryDate is DateTime value
            ? $"expiry {DateOnly.FromDateTime(value):yyyy-MM-dd}"
            : snapshot.MissingExpiryFallback == InventoryAllocationMissingExpiryFallback.ReceiptDate
                ? "missing expiry fell back to receipt date"
                : "missing expiry ranked last";
        var wholeLpn = snapshot.PreferWholeLicensePlate && balance.LicensePlateId.HasValue
            ? "; whole-LPN preference"
            : string.Empty;
        return $"Selected by {strategy}; receipt {receiptDate:O}; {expiry}{wholeLpn}; balance ID tie-break.";
    }

    private static int Specificity(
        InventoryAllocationStrategyPolicy policy,
        Item item,
        InventoryReservationRequest request)
    {
        var score = 0;
        if (policy.ItemId == item.Id)
        {
            score += 8;
        }

        if (policy.ItemCategory is not null &&
            string.Equals(policy.ItemCategory, item.Category, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
        }

        if (policy.DemandType is not null &&
            string.Equals(policy.DemandType, request.DemandType, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        return score;
    }

    private static bool SameDimension(InventoryTransaction transaction, InventoryBalance balance) =>
        transaction.WarehouseId == balance.WarehouseId &&
        transaction.LocationId == balance.LocationId &&
        transaction.ItemId == balance.ItemId &&
        transaction.LotId == balance.LotId &&
        transaction.SerialNumberId == balance.SerialNumberId &&
        string.Equals(transaction.SerialNumber, balance.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
        transaction.LicensePlateId == balance.LicensePlateId &&
        transaction.InventoryStatusId == balance.InventoryStatusId &&
        string.Equals(transaction.BaseUnitOfMeasure, balance.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase);

    private static string? GetEligibilityReason(
        InventoryBalance balance,
        Item item,
        int warehouseId,
        DateOnly businessDate)
    {
        if (balance.AvailableQuantity <= 0m)
        {
            return "The balance has no unreserved quantity available.";
        }

        if (balance.Warehouse is null || !balance.Warehouse.IsActive)
        {
            return "The warehouse is inactive or unavailable.";
        }

        if (balance.Location is null ||
            balance.Location.WarehouseId != warehouseId ||
            !balance.Location.IsActive ||
            !balance.Location.IsPickable)
        {
            return "The location is inactive, belongs to another warehouse, or is not pickable.";
        }

        if (balance.InventoryStatus is null ||
            !balance.InventoryStatus.IsActive ||
            !balance.InventoryStatus.IsAvailable ||
            !balance.InventoryStatus.IsAllocatable)
        {
            return "The inventory status is not active, available, or allocatable.";
        }

        if (item.RequiresLot && balance.Lot is null)
        {
            return "The item requires a lot and this balance has no lot.";
        }

        if (balance.Lot is not null && !balance.Lot.IsAllocationEligible(businessDate))
        {
            return "The lot is expired, blocked, or outside its allocation window.";
        }

        if (item.RequiresSerial && balance.Serial is null)
        {
            return "The item requires a serial number and this balance has none.";
        }

        if (balance.Serial is not null &&
            (!balance.Serial.IsAllocationEligible ||
             balance.Serial.CurrentWarehouseId != warehouseId ||
             balance.Serial.CurrentLocationId != balance.LocationId))
        {
            return "The serial is not allocation-eligible at the current warehouse location.";
        }

        if (balance.LicensePlate is not null &&
            (!balance.LicensePlate.IsActive ||
             balance.LicensePlate.Status is not (LicensePlateStatus.Open or LicensePlateStatus.Returned) ||
             balance.LicensePlate.WarehouseId != warehouseId ||
             balance.LicensePlate.CurrentLocationId != balance.LocationId))
        {
            return "The license plate is inactive, closed, or not at the current warehouse location.";
        }

        return null;
    }

    private static IQueryable<InventoryBalance> ApplySelector(
        IQueryable<InventoryBalance> query,
        InventoryReservationSelector? selector)
    {
        if (selector?.LocationId is > 0)
        {
            query = query.Where(balance => balance.LocationId == selector.LocationId.Value);
        }

        if (selector?.LotId is > 0)
        {
            query = query.Where(balance => balance.LotId == selector.LotId.Value);
        }

        if (selector?.SerialNumberId is > 0)
        {
            query = query.Where(balance => balance.SerialNumberId == selector.SerialNumberId.Value);
        }

        if (!string.IsNullOrWhiteSpace(selector?.SerialNumber))
        {
            var serial = selector.SerialNumber.Trim().ToUpperInvariant();
            query = query.Where(balance => balance.SerialNumber == serial);
        }

        if (selector?.LicensePlateId is > 0)
        {
            query = query.Where(balance => balance.LicensePlateId == selector.LicensePlateId.Value);
        }

        if (selector?.InventoryStatusId is > 0)
        {
            query = query.Where(balance => balance.InventoryStatusId == selector.InventoryStatusId.Value);
        }

        return query;
    }

    private static InventoryAllocationStrategySnapshot ToSnapshot(
        InventoryAllocationStrategyPolicy policy) =>
        new(
            policy.Id,
            policy.PolicyKey,
            policy.Strategy,
            policy.Revision,
            policy.FixedLocationId,
            policy.PreferWholeLicensePlate,
            policy.MinimumShelfLifeDays,
            policy.MissingExpiryFallback);

    private static InventoryAllocationStrategyPolicyDto MapPolicy(
        InventoryAllocationStrategyPolicy policy,
        Warehouse warehouse,
        Item? item,
        Location? fixedLocation) =>
        new(
            policy.Id,
            policy.WarehouseId,
            warehouse.Code,
            policy.PolicyKey,
            policy.ItemId,
            item?.Sku,
            policy.ItemCategory,
            policy.DemandType,
            policy.Strategy,
            policy.FixedLocationId,
            fixedLocation?.Code,
            policy.PreferWholeLicensePlate,
            policy.MinimumShelfLifeDays,
            policy.MissingExpiryFallback,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private DateTime UtcNow() =>
        DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);

    private static DateTime NormalizeUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
