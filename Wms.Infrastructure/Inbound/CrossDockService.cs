using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inbound;

/// <summary>
/// Cross-dock policy and deterministic matching boundary. This slice records
/// explainable receipt-to-demand plans only. Reservation, stock movement, and
/// executable cross-dock work remain an explicit follow-up boundary so a plan
/// can never be mistaken for a materialized inventory allocation.
/// </summary>
public sealed class CrossDockService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock) : ICrossDockService
{
    private static readonly CrossDockPlanLineStatus[] ActivePlanStatuses =
    [
        CrossDockPlanLineStatus.Matched,
        CrossDockPlanLineStatus.ReservationPending,
        CrossDockPlanLineStatus.WorkPending,
        CrossDockPlanLineStatus.ShortPick
    ];

    private static readonly SalesOrderStatus[] EligibleOrderStatuses =
    [
        SalesOrderStatus.Confirmed,
        SalesOrderStatus.Allocating,
        SalesOrderStatus.PartiallyAllocated,
        SalesOrderStatus.Released
    ];

    public async Task<Result<CrossDockPolicyDto>> SavePolicyAsync(
        int? policyId,
        CrossDockPolicyInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CrossDockPolicyDto>();
        }

        try
        {
            var warehouseExists = await context.Warehouses.AnyAsync(
                warehouse => warehouse.Id == input.WarehouseId && warehouse.IsActive,
                cancellationToken);
            if (!warehouseExists)
            {
                return Result.Failure<CrossDockPolicyDto>(WmsErrors.NotFound(
                    "crossdock.warehouse_not_found",
                    "The cross-dock warehouse was not found or is inactive."));
            }

            await ValidateReferencesAsync(input, cancellationToken);
            CrossDockPolicy policy;
            if (policyId.HasValue)
            {
                policy = await context.CrossDockPolicies.SingleOrDefaultAsync(
                    candidate => candidate.Id == policyId.Value &&
                                 candidate.WarehouseId == input.WarehouseId,
                    cancellationToken)
                    ?? throw new KeyNotFoundException(
                        $"Cross-dock policy '{policyId.Value}' was not found.");

                if (await context.CrossDockPolicies.AnyAsync(
                        candidate => candidate.WarehouseId == input.WarehouseId &&
                                     candidate.PolicyKey == input.PolicyKey.Trim() &&
                                     candidate.Id != policy.Id,
                        cancellationToken))
                {
                    return Result.Failure<CrossDockPolicyDto>(WmsErrors.Conflict(
                        "crossdock.policy_key_conflict",
                        "The cross-dock policy key is already used in this warehouse."));
                }

                policy.Update(
                    input.Name,
                    input.Priority,
                    input.ItemId,
                    input.ItemCategory,
                    input.SupplierId,
                    input.InboundSourceType,
                    input.CustomerId,
                    input.SalesOrderId,
                    input.DestinationLocationId,
                    input.QuantityTolerancePercent,
                    input.MinimumShelfLifeDays,
                    input.RequireExpiry,
                    input.AllowPlanned,
                    input.AllowOpportunistic,
                    input.EffectiveFromUtc ?? DateTime.UnixEpoch,
                    input.EffectiveToUtc,
                    isActive: true);
            }
            else
            {
                if (await context.CrossDockPolicies.AnyAsync(
                        candidate => candidate.WarehouseId == input.WarehouseId &&
                                     candidate.PolicyKey == input.PolicyKey.Trim(),
                        cancellationToken))
                {
                    return Result.Failure<CrossDockPolicyDto>(WmsErrors.Conflict(
                        "crossdock.policy_key_conflict",
                        "The cross-dock policy key is already used in this warehouse."));
                }

                policy = new CrossDockPolicy(
                    input.WarehouseId,
                    input.PolicyKey,
                    input.Name,
                    input.Priority,
                    input.ItemId,
                    input.ItemCategory,
                    input.SupplierId,
                    input.InboundSourceType,
                    input.CustomerId,
                    input.SalesOrderId,
                    input.DestinationLocationId,
                    input.QuantityTolerancePercent,
                    input.MinimumShelfLifeDays,
                    input.RequireExpiry,
                    input.AllowPlanned,
                    input.AllowOpportunistic,
                    input.EffectiveFromUtc,
                    input.EffectiveToUtc);
                context.CrossDockPolicies.Add(policy);
            }

            await context.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CrossDockPolicyChanged,
                    WmsAuditEntityTypes.CrossDockPolicy,
                    policy.PolicyKey,
                    policy.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["policyId"] = policy.Id,
                        ["priority"] = policy.Priority,
                        ["itemId"] = policy.ItemId,
                        ["itemCategory"] = policy.ItemCategory,
                        ["supplierId"] = policy.SupplierId,
                        ["customerId"] = policy.CustomerId,
                        ["salesOrderId"] = policy.SalesOrderId,
                        ["destinationLocationId"] = policy.DestinationLocationId,
                        ["allowPlanned"] = policy.AllowPlanned,
                        ["allowOpportunistic"] = policy.AllowOpportunistic
                    },
                    ActorUserId: userId),
                cancellationToken);
            return Result.Success(Map(policy));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException exception)
        {
            return Result.Failure<CrossDockPolicyDto>(WmsErrors.NotFound(
                "crossdock.policy_not_found",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CrossDockPolicyDto>(WmsErrors.Validation(
                "crossdock.policy_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<CrossDockPolicyDto>(WmsErrors.BusinessRule(
                "crossdock.policy_invalid",
                exception.Message));
        }
    }

    public async Task<Result<IReadOnlyList<CrossDockPolicyDto>>> ListPoliciesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<CrossDockPolicyDto>>();
        }

        var query = context.CrossDockPolicies
            .AsNoTracking()
            .Where(policy => policy.WarehouseId == warehouseId);
        if (!includeInactive)
        {
            query = query.Where(policy => policy.IsActive);
        }

        var policies = await query
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.PolicyKey)
            .ToArrayAsync(cancellationToken);
        return Result.Success<IReadOnlyList<CrossDockPolicyDto>>(
            policies.Select(Map).ToArray());
    }

    public async Task<Result<CrossDockSimulationDto>> SimulateAsync(
        CrossDockSimulationInput input,
        CancellationToken cancellationToken = default)
    {
        var receiptLine = await LoadReceiptLineAsync(input.ReceiptLineId, cancellationToken);
        if (receiptLine is null)
        {
            return Result.Failure<CrossDockSimulationDto>(WmsErrors.NotFound(
                "crossdock.receipt_line_not_found",
                "The receipt line was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            receiptLine.Receipt.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CrossDockSimulationDto>();
        }

        try
        {
            var resolution = await ResolveAsync(
                receiptLine,
                input.Mode,
                input.PolicyId,
                input.AsOfUtc ?? clock.UtcNow.UtcDateTime,
                cancellationToken);
            return Result.Success(Map(resolution));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CrossDockSimulationDto>(WmsErrors.Validation(
                "crossdock.simulation_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<CrossDockSimulationDto>(WmsErrors.BusinessRule(
                "crossdock.simulation_invalid",
                exception.Message));
        }
    }

    public async Task<Result<CrossDockPlanDto>> CreatePlanAsync(
        CrossDockPlanCreateInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var receiptLine = await LoadReceiptLineAsync(input.ReceiptLineId, cancellationToken);
        if (receiptLine is null)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.NotFound(
                "crossdock.receipt_line_not_found",
                "The receipt line was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            receiptLine.Receipt.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CrossDockPlanDto>();
        }

        var creationKey = string.IsNullOrWhiteSpace(input.CreationKey)
            ? $"receipt:{receiptLine.ReceiptId.ToString(CultureInfo.InvariantCulture)}" +
              $":line:{receiptLine.Id.ToString(CultureInfo.InvariantCulture)}" +
              $":mode:{input.Mode}"
            : input.CreationKey.Trim();
        var existing = await context.CrossDockPlans
            .Include(plan => plan.Lines)
            .Include(plan => plan.Receipt)
            .Include(plan => plan.Item)
            .Include(plan => plan.Policy)
            .SingleOrDefaultAsync(
                plan => plan.WarehouseId == receiptLine.Receipt.WarehouseId &&
                        plan.CreationKey == creationKey,
                cancellationToken);
        if (existing is not null)
        {
            return Result.Success(Map(existing));
        }

        var resolution = await ResolveAsync(
            receiptLine,
            input.Mode,
            input.PolicyId,
            input.AsOfUtc ?? clock.UtcNow.UtcDateTime,
            cancellationToken);
        if (resolution.Policy is null)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.BusinessRule(
                "crossdock.no_eligible_policy",
                resolution.Explanation));
        }

        var plan = new CrossDockPlan(
            $"XDOCK-{receiptLine.ReceiptId.ToString(CultureInfo.InvariantCulture)}-" +
            $"{receiptLine.Id.ToString(CultureInfo.InvariantCulture)}-{input.Mode.ToString().ToUpperInvariant()}",
            creationKey,
            receiptLine.Receipt.WarehouseId,
            receiptLine.ReceiptId,
            receiptLine.Id,
            receiptLine.ItemId,
            resolution.Policy.Id,
            input.Mode,
            resolution.ReceivedBaseQuantity,
            resolution.MatchedBaseQuantity,
            resolution.FallbackBaseQuantity,
            receiptLine.ReceivingLocationId ?? receiptLine.Receipt.ReceivingLocationId,
            resolution.DestinationLocationId,
            receiptLine.InventoryStatusId,
            receiptLine.BaseUnitOfMeasure,
            receiptLine.LotNumberSnapshot,
            receiptLine.ExpiryDateSnapshot,
            receiptLine.SerialNumberSnapshot,
            receiptLine.LicensePlateId,
            input.Notes is null
                ? resolution.Explanation
                : $"{resolution.Explanation} Notes: {input.Notes.Trim()}",
            userId);

        foreach (var match in resolution.Matches)
        {
            plan.AddLine(new CrossDockPlanLine(
                match.Sequence,
                match.SalesOrderId,
                match.SalesOrderLineId,
                match.ItemId,
                match.CustomerId,
                match.SalesOrderLineNumber,
                match.DemandBaseQuantity,
                match.MatchedBaseQuantity,
                match.SalesOrderDocumentNumber,
                match.ItemSku,
                match.Reason));
        }

        context.CrossDockPlans.Add(plan);
        await context.SaveChangesAsync(cancellationToken);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.CrossDockPlanCreated,
                WmsAuditEntityTypes.CrossDockPlan,
                plan.PlanNumber,
                plan.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["planId"] = plan.Id,
                    ["receiptId"] = plan.ReceiptId,
                    ["receiptLineId"] = plan.ReceiptLineId,
                    ["policyId"] = plan.PolicyId,
                    ["mode"] = plan.Mode.ToString(),
                    ["matchedBaseQuantity"] = plan.MatchedBaseQuantity,
                    ["fallbackBaseQuantity"] = plan.FallbackBaseQuantity,
                    ["executionBoundary"] = "planning-only"
                },
                ActorUserId: userId),
            cancellationToken);

        var mapped = await LoadPlanAsync(plan.Id, cancellationToken);
        return mapped is null
            ? Result.Failure<CrossDockPlanDto>(WmsErrors.Unexpected(
                "crossdock.plan_reload_failed",
                "The cross-dock plan was created but could not be reloaded."))
            : Result.Success(Map(mapped));
    }

    public async Task<Result<CrossDockPlanDto>> GetAsync(
        int planId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.NotFound(
                "crossdock.plan_not_found",
                "The cross-dock plan was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CrossDockPlanDto>();
        }

        return Result.Success(Map(plan));
    }

    public async Task<Result<CrossDockPlanDto>> CancelAsync(
        int planId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var plan = await LoadPlanAsync(planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.NotFound(
                "crossdock.plan_not_found",
                "The cross-dock plan was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AllocationManage,
            plan.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CrossDockPlanDto>();
        }

        try
        {
            plan.Cancel(userId, reason, clock.UtcNow.UtcDateTime);
            await context.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CrossDockPlanCancelled,
                    WmsAuditEntityTypes.CrossDockPlan,
                    plan.PlanNumber,
                    plan.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["planId"] = plan.Id,
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            return Result.Success(Map(plan));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.Validation(
                "crossdock.cancel_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<CrossDockPlanDto>(WmsErrors.BusinessRule(
                "crossdock.cancel_invalid",
                exception.Message));
        }
    }

    private async Task<ReceiptLine?> LoadReceiptLineAsync(
        int receiptLineId,
        CancellationToken cancellationToken) =>
        await context.ReceiptLines
            .Include(line => line.Receipt)
            .Include(line => line.Item)
            .SingleOrDefaultAsync(line => line.Id == receiptLineId, cancellationToken);

    private async Task<CrossDockPlan?> LoadPlanAsync(
        int planId,
        CancellationToken cancellationToken) =>
        await context.CrossDockPlans
            .Include(plan => plan.Receipt)
            .Include(plan => plan.Item)
            .Include(plan => plan.Policy)
            .Include(plan => plan.Lines)
            .SingleOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

    private async Task ValidateReferencesAsync(
        CrossDockPolicyInput input,
        CancellationToken cancellationToken)
    {
        if (input.ItemId.HasValue && !await context.Items.AnyAsync(
                item => item.Id == input.ItemId.Value && item.IsActive,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Item '{input.ItemId}' was not found or is inactive.");
        }

        if (input.SupplierId.HasValue && !await context.Suppliers.AnyAsync(
                supplier => supplier.Id == input.SupplierId.Value && supplier.IsActive,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Supplier '{input.SupplierId}' was not found or is inactive.");
        }

        if (input.CustomerId.HasValue && !await context.Customers.AnyAsync(
                customer => customer.Id == input.CustomerId.Value && customer.IsActive,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Customer '{input.CustomerId}' was not found or is inactive.");
        }

        if (input.SalesOrderId.HasValue && !await context.SalesOrders.AnyAsync(
                order => order.Id == input.SalesOrderId.Value && order.WarehouseId == input.WarehouseId,
                cancellationToken))
        {
            throw new KeyNotFoundException($"Sales order '{input.SalesOrderId}' was not found in the warehouse.");
        }

        if (input.DestinationLocationId.HasValue)
        {
            var destination = await context.Locations.SingleOrDefaultAsync(
                location => location.Id == input.DestinationLocationId.Value &&
                            location.WarehouseId == input.WarehouseId &&
                            location.IsActive,
                cancellationToken);
            if (destination is null)
            {
                throw new KeyNotFoundException("The cross-dock destination location was not found.");
            }

            if (destination.Type is not (LocationType.Staging or LocationType.Packing or LocationType.Shipping))
            {
                throw new InvalidOperationException(
                    "A cross-dock destination must be staging, packing, or shipping.");
            }
        }
    }

    private async Task<Resolution> ResolveAsync(
        ReceiptLine receiptLine,
        CrossDockMode mode,
        int? policyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var asOf = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        var policyQuery = context.CrossDockPolicies
            .Where(policy => policy.WarehouseId == receiptLine.Receipt.WarehouseId &&
                             policy.IsActive &&
                             policy.EffectiveFromUtc <= asOf &&
                             (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc > asOf));
        var policies = await policyQuery
            .OrderByDescending(policy => policy.Priority)
            .ThenByDescending(policy => policy.ItemId.HasValue)
            .ThenBy(policy => policy.PolicyKey)
            .ToArrayAsync(cancellationToken);

        var policy = policyId.HasValue
            ? policies.SingleOrDefault(candidate => candidate.Id == policyId.Value)
            : policies.FirstOrDefault(candidate =>
                (mode == CrossDockMode.Planned && candidate.AllowPlanned) ||
                (mode == CrossDockMode.Opportunistic && candidate.AllowOpportunistic));
        if (policyId.HasValue && policy is null)
        {
            throw new InvalidOperationException(
                "The selected cross-dock policy is not active, effective, or allowed for this mode.");
        }

        var received = receiptLine.ReceivedBaseQuantity;
        var accepted = receiptLine.AcceptedBaseQuantity;
        var destinationId = await ResolveDestinationLocationIdAsync(
            receiptLine.Receipt.WarehouseId,
            policy,
            cancellationToken);
        if (policy is null)
        {
            return new Resolution(
                receiptLine,
                mode,
                null,
                received,
                0m,
                received,
                destinationId,
                "No active cross-dock policy matched this receipt.",
                []);
        }

        var policyReason = GetPolicyRejectionReason(receiptLine, policy, mode, asOf);
        if (policyReason is not null)
        {
            return new Resolution(
                receiptLine,
                mode,
                policy,
                received,
                0m,
                received,
                destinationId,
                policyReason,
                []);
        }

        var status = await context.InventoryStatuses.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == receiptLine.InventoryStatusId,
            cancellationToken);
        if (status is null || !status.IsAllocatable)
        {
            return new Resolution(
                receiptLine,
                mode,
                policy,
                received,
                0m,
                received,
                destinationId,
                $"Inbound inventory status '{receiptLine.InventoryStatusCodeSnapshot ?? receiptLine.InventoryStatusId.ToString(CultureInfo.InvariantCulture)}' is not allocatable; QC, hold, quarantine, damaged, and other blocked stock cannot bypass its status.",
                []);
        }

        if (!destinationId.HasValue)
        {
            return new Resolution(
                receiptLine,
                mode,
                policy,
                received,
                0m,
                received,
                null,
                "No active staging, packing, or shipping destination is configured for this warehouse or policy.",
                []);
        }

        var claimed = await context.CrossDockPlanLines
            .Where(line => ActivePlanStatuses.Contains(line.Status))
            .GroupBy(line => line.SalesOrderLineId)
            .Select(group => new { SalesOrderLineId = group.Key, Quantity = group.Sum(line => line.MatchedBaseQuantity) })
            .ToDictionaryAsync(value => value.SalesOrderLineId, value => value.Quantity, cancellationToken);

        var candidates = await context.SalesOrderLines
            .Include(line => line.SalesOrder)
            .Where(line => line.ItemId == receiptLine.ItemId &&
                           line.SalesOrder.WarehouseId == receiptLine.Receipt.WarehouseId &&
                           EligibleOrderStatuses.Contains(line.SalesOrder.Status) &&
                           line.OrderedBaseQuantity - line.AllocatedBaseQuantity - line.CancelledBaseQuantity > 0m)
            .OrderByDescending(line => line.SalesOrder.Priority)
            .ThenBy(line => line.SalesOrder.RequestedShipDate)
            .ThenBy(line => line.SalesOrderId)
            .ThenBy(line => line.LineNumber)
            .ToArrayAsync(cancellationToken);

        var matches = new List<Match>();
        var remaining = accepted;
        foreach (var candidate in candidates)
        {
            if (!MatchesDemandFilter(candidate.SalesOrder, policy))
            {
                continue;
            }

            var alreadyPlanned = claimed.GetValueOrDefault(candidate.Id);
            var demand = Math.Max(
                0m,
                candidate.OrderedBaseQuantity - candidate.AllocatedBaseQuantity -
                candidate.CancelledBaseQuantity - alreadyPlanned);
            if (demand <= 0m || remaining <= 0m)
            {
                continue;
            }

            var matched = Math.Min(remaining, demand);
            matches.Add(new Match(
                matches.Count + 1,
                candidate.SalesOrderId,
                candidate.Id,
                candidate.LineNumber,
                candidate.SalesOrder.DocumentNumber,
                candidate.SalesOrder.CustomerId,
                candidate.ItemId,
                candidate.ItemSkuSnapshot,
                demand,
                alreadyPlanned,
                matched,
                $"Matched confirmed demand by priority {candidate.SalesOrder.Priority}, requested ship date {candidate.SalesOrder.RequestedShipDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not set"}, and deterministic order/line sequence."));
            remaining -= matched;
        }

        var matchedQuantity = matches.Sum(match => match.MatchedBaseQuantity);
        var fallbackQuantity = received - matchedQuantity;
        var explanation = matchedQuantity > 0m
            ? fallbackQuantity > 0m
                ? $"Matched {matchedQuantity.ToString("G29", CultureInfo.InvariantCulture)} accepted units to confirmed outbound demand; {fallbackQuantity.ToString("G29", CultureInfo.InvariantCulture)} units remain explicit fallback for normal putaway, exception, or later replan. No reservation or stock movement was created by simulation."
                : "The accepted receipt quantity matched confirmed outbound demand. No reservation or stock movement was created by simulation."
            : "No eligible confirmed outbound demand matched this receipt; normal putaway remains the fallback. No reservation or stock movement was created.";

        return new Resolution(
            receiptLine,
            mode,
            policy,
            received,
            matchedQuantity,
            fallbackQuantity,
            destinationId,
            explanation,
            matches);
    }

    private async Task<int?> ResolveDestinationLocationIdAsync(
        int warehouseId,
        CrossDockPolicy? policy,
        CancellationToken cancellationToken)
    {
        if (policy?.DestinationLocationId is int configured)
        {
            return configured;
        }

        return await context.WarehouseOperationalLocations
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Role == WarehouseOperationalLocationRole.Staging)
            .Join(
                context.Locations,
                value => value.LocationId,
                location => location.Id,
                (_, location) => location)
            .Where(location => location.IsActive &&
                               (location.Type == LocationType.Staging ||
                                location.Type == LocationType.Packing ||
                                location.Type == LocationType.Shipping))
            .Select(location => (int?)location.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? GetPolicyRejectionReason(
        ReceiptLine receiptLine,
        CrossDockPolicy policy,
        CrossDockMode mode,
        DateTime asOfUtc)
    {
        if ((mode == CrossDockMode.Planned && !policy.AllowPlanned) ||
            (mode == CrossDockMode.Opportunistic && !policy.AllowOpportunistic))
        {
            return $"Policy '{policy.PolicyKey}' does not allow {mode} cross-docking.";
        }

        if (policy.ItemId.HasValue && policy.ItemId.Value != receiptLine.ItemId)
        {
            return $"Policy '{policy.PolicyKey}' is restricted to another item.";
        }

        if (!string.IsNullOrWhiteSpace(policy.ItemCategory) &&
            !string.Equals(policy.ItemCategory, receiptLine.Item.Category, StringComparison.OrdinalIgnoreCase))
        {
            return $"Policy '{policy.PolicyKey}' is restricted to item category '{policy.ItemCategory}'.";
        }

        if (policy.SupplierId.HasValue && policy.SupplierId.Value != receiptLine.Receipt.SupplierId)
        {
            return $"Policy '{policy.PolicyKey}' is restricted to another supplier.";
        }

        if (!string.IsNullOrWhiteSpace(policy.InboundSourceType) &&
            !string.Equals(policy.InboundSourceType, receiptLine.Receipt.SourceType, StringComparison.OrdinalIgnoreCase))
        {
            return $"Policy '{policy.PolicyKey}' is restricted to inbound source '{policy.InboundSourceType}'.";
        }

        if (policy.RequireExpiry && !receiptLine.ExpiryDateSnapshot.HasValue)
        {
            return $"Policy '{policy.PolicyKey}' requires an expiry date.";
        }

        if (policy.MinimumShelfLifeDays > 0 && receiptLine.ExpiryDateSnapshot.HasValue &&
            receiptLine.ExpiryDateSnapshot.Value < asOfUtc.AddDays(policy.MinimumShelfLifeDays))
        {
            return $"Receipt expiry does not satisfy policy '{policy.PolicyKey}' minimum shelf life of {policy.MinimumShelfLifeDays} days.";
        }

        return null;
    }

    private static bool MatchesDemandFilter(SalesOrder order, CrossDockPolicy policy) =>
        (!policy.CustomerId.HasValue || policy.CustomerId.Value == order.CustomerId) &&
        (!policy.SalesOrderId.HasValue || policy.SalesOrderId.Value == order.Id);

    private static CrossDockPolicyDto Map(CrossDockPolicy policy) =>
        new(
            policy.Id,
            policy.WarehouseId,
            policy.PolicyKey,
            policy.Name,
            policy.Priority,
            policy.ItemId,
            policy.ItemCategory,
            policy.SupplierId,
            policy.InboundSourceType,
            policy.CustomerId,
            policy.SalesOrderId,
            policy.DestinationLocationId,
            policy.QuantityTolerancePercent,
            policy.MinimumShelfLifeDays,
            policy.RequireExpiry,
            policy.AllowPlanned,
            policy.AllowOpportunistic,
            policy.EffectiveFromUtc,
            policy.EffectiveToUtc,
            policy.IsActive,
            policy.Revision);

    private static CrossDockSimulationDto Map(Resolution resolution) =>
        new(
            resolution.ReceiptLine.ReceiptId,
            resolution.ReceiptLine.Id,
            resolution.ReceiptLine.Receipt.DocumentNumber,
            resolution.ReceiptLine.Receipt.WarehouseId,
            resolution.ReceiptLine.ItemId,
            resolution.ReceiptLine.ItemSkuSnapshot,
            resolution.ReceivedBaseQuantity,
            resolution.ReceiptLine.AcceptedBaseQuantity,
            resolution.MatchedBaseQuantity,
            resolution.FallbackBaseQuantity,
            resolution.ReceiptLine.ReceivingLocationId ?? resolution.ReceiptLine.Receipt.ReceivingLocationId,
            resolution.DestinationLocationId,
            resolution.ReceiptLine.InventoryStatusId,
            resolution.Mode,
            resolution.Policy?.Id,
            resolution.Policy?.PolicyKey,
            resolution.MatchedBaseQuantity > 0m ? "matched" : "fallback",
            resolution.Explanation,
            resolution.Matches.Select(match => new CrossDockMatchDto(
                match.SalesOrderId,
                match.SalesOrderLineId,
                match.SalesOrderLineNumber,
                match.SalesOrderDocumentNumber,
                match.CustomerId,
                match.ItemId,
                match.ItemSku,
                match.DemandBaseQuantity,
                match.AlreadyPlannedBaseQuantity,
                match.MatchedBaseQuantity,
                match.Reason)).ToArray());

    private static CrossDockPlanDto Map(CrossDockPlan plan) =>
        new(
            plan.Id,
            plan.PlanNumber,
            plan.CreationKey,
            plan.WarehouseId,
            plan.ReceiptId,
            plan.ReceiptLineId,
            plan.Receipt.DocumentNumber,
            plan.ItemId,
            plan.Item.Sku,
            plan.PolicyId,
            plan.Policy.PolicyKey,
            plan.Mode,
            plan.ReceivedBaseQuantity,
            plan.MatchedBaseQuantity,
            plan.FallbackBaseQuantity,
            plan.SourceLocationId,
            plan.DestinationLocationId,
            plan.InventoryStatusId,
            plan.BaseUnitOfMeasure,
            plan.LotNumber,
            plan.ExpiryDate,
            plan.SerialNumber,
            plan.LicensePlateId,
            plan.Status,
            plan.Explanation,
            plan.CreatedByUserId,
            plan.CreatedAt,
            plan.CancelledByUserId,
            plan.CancelledAtUtc,
            plan.CancellationReason,
            plan.Revision,
            plan.Lines.OrderBy(line => line.Sequence).Select(line => new CrossDockPlanLineDto(
                line.Id,
                line.Sequence,
                line.SalesOrderId,
                line.SalesOrderLineId,
                line.SalesOrderLineNumber,
                line.SalesOrderDocumentNumber,
                line.CustomerId,
                line.ItemId,
                line.ItemSku,
                line.DemandBaseQuantity,
                line.MatchedBaseQuantity,
                line.Status,
                line.Reason)).ToArray());

    private sealed record Resolution(
        ReceiptLine ReceiptLine,
        CrossDockMode Mode,
        CrossDockPolicy? Policy,
        decimal ReceivedBaseQuantity,
        decimal MatchedBaseQuantity,
        decimal FallbackBaseQuantity,
        int? DestinationLocationId,
        string Explanation,
        IReadOnlyList<Match> Matches);

    private sealed record Match(
        int Sequence,
        int SalesOrderId,
        int SalesOrderLineId,
        int SalesOrderLineNumber,
        string SalesOrderDocumentNumber,
        int CustomerId,
        int ItemId,
        string ItemSku,
        decimal DemandBaseQuantity,
        decimal AlreadyPlannedBaseQuantity,
        decimal MatchedBaseQuantity,
        string Reason);
}
