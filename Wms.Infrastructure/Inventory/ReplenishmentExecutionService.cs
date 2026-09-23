using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Turns effective replenishment signals into executable warehouse work. This
/// planner is deliberately stock-neutral: it only reads balances and creates
/// work; the registered replenishment completion handler owns the atomic move.
/// </summary>
public sealed class ReplenishmentExecutionService(
    WmsDbContext context,
    IInventoryReplenishmentPolicyService policyService,
    IWarehouseWorkService warehouseWorkService,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    ILogger<ReplenishmentExecutionService> logger) : IReplenishmentExecutionService
{
    private static readonly LocationType[] SourceLocationTypes =
    [
        LocationType.Storage,
        LocationType.Zone,
        LocationType.Aisle,
        LocationType.Rack,
        LocationType.Bin,
        LocationType.Bulk,
        LocationType.PickFace
    ];

    public async Task<Result<ReplenishmentGenerationResultDto>> GenerateAsync(
        ReplenishmentGenerationQuery query,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<ReplenishmentGenerationResultDto>(WmsErrors.Validation(
                "replenishment.actor_required",
                "An actor is required to generate replenishment work."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReplenishmentGenerationResultDto>();
        }

        try
        {
            var signalsResult = await policyService.GetSignalsAsync(
                new InventoryReplenishmentSignalQuery(
                    query.WarehouseId,
                    Limit: query.PolicyId.HasValue ? 1_000 : query.Limit),
                cancellationToken);
            if (signalsResult.IsFailure)
            {
                return signalsResult.ToFailure<ReplenishmentGenerationResultDto>();
            }

            var signals = signalsResult.Value
                .Where(signal => !query.PolicyId.HasValue || signal.PolicyId == query.PolicyId.Value)
                .Take(NormalizeLimit(query.Limit))
                .ToArray();
            var plans = new List<ReplenishmentWorkPlanDto>(signals.Length);
            var workCreated = 0;
            var workReused = 0;
            var blocked = 0;

            foreach (var signal in signals)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = await PlanSignalAsync(signal, actorUserId, query.DryRun, cancellationToken);
                plans.Add(plan.Plan);
                workCreated += plan.WorkCreated;
                workReused += plan.WorkReused;
                blocked += plan.Blocked;
            }

            return Result.Success(new ReplenishmentGenerationResultDto(
                signals.Length,
                signals.Length,
                workCreated,
                workReused,
                blocked,
                plans));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Replenishment work generation failed");
            return Result.Failure<ReplenishmentGenerationResultDto>(WmsErrors.FromException(
                exception,
                "replenishment.generation_failed",
                "Replenishment work could not be generated."));
        }
    }

    private async Task<PlanResult> PlanSignalAsync(
        InventoryReplenishmentSignalDto signal,
        string actorUserId,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var policy = await context.InventoryReplenishmentPolicies
            .AsNoTracking()
            .Include(value => value.Item)
            .SingleOrDefaultAsync(value => value.Id == signal.PolicyId, cancellationToken);
        if (policy is null)
        {
            return BlockedPlan(signal, null, 0m, "policy-not-found");
        }

        var destination = await ResolveDestinationAsync(policy, cancellationToken);
        if (destination is null)
        {
            return BlockedPlan(signal, null, 0m, "destination-blocked");
        }

        var openWorks = await context.WarehouseWorks
            .AsNoTracking()
            .Include(work => work.Lines)
            .Where(work =>
                work.WarehouseId == signal.WarehouseId &&
                work.Type == WarehouseWorkType.Replenishment &&
                work.Status != WarehouseWorkStatus.Completed &&
                work.Status != WarehouseWorkStatus.Cancelled &&
                work.Lines.Any(line =>
                    line.ItemId == signal.ItemId &&
                    line.DestinationLocationId == destination.Id))
            .OrderBy(work => work.Id)
            .ToArrayAsync(cancellationToken);
        var openQuantity = openWorks
            .SelectMany(work => work.Lines)
            .Where(line => line.ItemId == signal.ItemId &&
                           line.DestinationLocationId == destination.Id)
            .Sum(line => Math.Max(0m, line.PlannedQuantity - line.ActualQuantity));
        if (openWorks.Length > 0)
        {
            var existing = openWorks[0];
            return new PlanResult(
                Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                    signal.PolicyId,
                    signal.ItemId,
                    signal.ItemSku,
                    signal.WarehouseId,
                    destination.Id,
                    signal.ShortfallQuantity,
                    openQuantity,
                    0m,
                    "open-work-already-covers-signal",
                    existing.Id,
                    existing.WorkNumber,
                    [])),
                0,
                1,
                0);
        }

        var requiredQuantity = Math.Max(0m, signal.ShortfallQuantity - openQuantity);
        if (requiredQuantity <= 0m)
        {
            return new PlanResult(
                Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                    signal.PolicyId,
                    signal.ItemId,
                    signal.ItemSku,
                    signal.WarehouseId,
                    destination.Id,
                    signal.ShortfallQuantity,
                    openQuantity,
                    0m,
                    "no-shortage-after-open-work",
                    null,
                    null,
                    [])),
                0,
                0,
                0);
        }

        var capacity = await GetAvailableCapacityAsync(destination, signal.WarehouseId, cancellationToken);
        if (capacity <= 0m)
        {
            return BlockedPlan(signal, destination.Id, openQuantity, "destination-capacity-blocked");
        }

        requiredQuantity = Math.Min(requiredQuantity, capacity);
        var balances = await context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Location)
            .Include(balance => balance.Item)
            .Include(balance => balance.InventoryStatus)
            .Include(balance => balance.Lot)
            .Where(balance =>
                balance.WarehouseId == signal.WarehouseId &&
                balance.ItemId == signal.ItemId &&
                balance.LocationId != destination.Id &&
                balance.OnHandQuantity > balance.ReservedQuantity &&
                balance.Location.IsActive &&
                SourceLocationTypes.Contains(balance.Location.Type) &&
                balance.InventoryStatus.IsActive &&
                balance.InventoryStatus.IsAllocatable)
            .ToArrayAsync(cancellationToken);

        var candidates = policy.Item.UseFefo
            ? balances
                .OrderBy(balance => balance.Lot?.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(balance => balance.Location.Priority)
                .ThenBy(balance => balance.CreatedAt)
                .ThenBy(balance => balance.Id)
            : balances
                .OrderBy(balance => balance.Location.Priority)
                .ThenBy(balance => balance.CreatedAt)
                .ThenBy(balance => balance.Id);

        var lines = new List<ReplenishmentWorkPlanLineDto>();
        var remaining = requiredQuantity;
        foreach (var balance in candidates)
        {
            if (remaining <= 0m)
            {
                break;
            }

            var available = balance.OnHandQuantity - balance.ReservedQuantity;
            if (available <= 0m)
            {
                continue;
            }

            // The execution handler moves a license plate atomically. Do not
            // create an impossible partial-LPN plan.
            if (balance.LicensePlateId.HasValue && available > remaining)
            {
                continue;
            }

            var planned = Math.Min(available, remaining);
            if (policy.Item.RequiresSerial && planned != 1m)
            {
                continue;
            }

            lines.Add(new ReplenishmentWorkPlanLineDto(
                lines.Count + 1,
                balance.LocationId,
                destination.Id,
                planned,
                balance.LotId,
                balance.SerialNumberId,
                balance.SerialNumber,
                balance.LicensePlateId,
                balance.InventoryStatusId,
                balance.BaseUnitOfMeasure,
                policy.Item.UseFefo
                    ? "FEFO, then source-location priority and receipt age."
                    : "FIFO, then source-location priority and receipt age.",
                balance.OwnerKind,
                balance.InventoryOwnerId,
                balance.OwnerCodeSnapshot));
            remaining -= planned;
        }

        if (lines.Count == 0)
        {
            return BlockedPlan(signal, destination.Id, openQuantity, "insufficient-eligible-source");
        }

        var plannedQuantity = lines.Sum(line => line.PlannedQuantity);
        var fingerprint = string.Join(
            ";",
            lines.Select(line => string.Join(
                ":",
                line.SourceLocationId,
                line.PlannedQuantity.ToString("0.############", CultureInfo.InvariantCulture),
                line.LotId,
                line.SerialNumberId,
                line.SerialNumber,
                line.LicensePlateId,
                line.InventoryStatusId,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot)));
        var fingerprintHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant()[..16];
        var creationKey =
            $"replenishment:{signal.PolicyId}:{destination.Id}:{fingerprintHash}";

        if (dryRun)
        {
            return new PlanResult(
                Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                    signal.PolicyId,
                    signal.ItemId,
                    signal.ItemSku,
                    signal.WarehouseId,
                    destination.Id,
                    signal.ShortfallQuantity,
                    openQuantity,
                    plannedQuantity,
                    "dry-run-eligible",
                    null,
                    null,
                    lines)
                {
                    CapacityAvailableQuantity = capacity
                }),
                0,
                0,
                0);
        }

        var existingByKey = await context.WarehouseWorks
            .AsNoTracking()
            .Include(work => work.Lines)
            .SingleOrDefaultAsync(
                work => work.WarehouseId == signal.WarehouseId &&
                        work.CreationKey == creationKey,
                cancellationToken);
        if (existingByKey is not null)
        {
            return new PlanResult(
                Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                    signal.PolicyId,
                    signal.ItemId,
                    signal.ItemSku,
                    signal.WarehouseId,
                    destination.Id,
                    signal.ShortfallQuantity,
                    openQuantity,
                    existingByKey.Lines.Sum(line => line.PlannedQuantity),
                    "idempotent-work-replay",
                    existingByKey.Id,
                    existingByKey.WorkNumber,
                    lines)),
                0,
                1,
                0);
        }

        var workResult = await warehouseWorkService.CreateAsync(
            new WarehouseWorkInput(
                creationKey,
                WarehouseWorkType.Replenishment,
                signal.WarehouseId,
                "InventoryReplenishmentPolicy",
                signal.PolicyId.ToString(CultureInfo.InvariantCulture),
                Priority: signal.SignalKind == InventoryReplenishmentSignalKind.OutOfStock ? 10 : 30,
                SourceLineReference: $"policy:{signal.PolicyId}:destination:{destination.Id}",
                QueueCode: "REPLENISHMENT",
                DueAtUtc: clock.UtcNow.UtcDateTime.AddDays(policy.LeadTimeDays ?? 0),
                Notes: $"{signal.SignalKind} signal; target shortfall {signal.ShortfallQuantity:0.####}; eligible source planned {plannedQuantity:0.####}.",
                MakeAvailable: true,
                Lines: lines.Select(line => new WarehouseWorkLineInput(
                    line.Sequence,
                    signal.WarehouseId,
                    signal.ItemId,
                    line.PlannedQuantity,
                    line.BaseUnitOfMeasure,
                    line.SourceLocationId,
                    line.DestinationLocationId,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.InventoryStatusId,
                    SourceReference: $"balance:{line.SourceLocationId}",
                    DimensionsSnapshot: "planned-from-inventory-balance",
                    OwnerKind: line.OwnerKind,
                    InventoryOwnerId: line.InventoryOwnerId,
                    OwnerCodeSnapshot: line.OwnerCodeSnapshot))
                    .ToArray()),
            actorUserId,
            cancellationToken);
        if (workResult.IsFailure)
        {
            return new PlanResult(
                Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                    signal.PolicyId,
                    signal.ItemId,
                    signal.ItemSku,
                    signal.WarehouseId,
                    destination.Id,
                    signal.ShortfallQuantity,
                    openQuantity,
                    plannedQuantity,
                    "work-generation-failed",
                    null,
                    null,
                    lines)),
                0,
                0,
                1);
        }

        return new PlanResult(
            Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                signal.PolicyId,
                signal.ItemId,
                signal.ItemSku,
                signal.WarehouseId,
                destination.Id,
                signal.ShortfallQuantity,
                openQuantity,
                plannedQuantity,
                remaining == 0m ? "work-created" : "partial-source-work-created",
                workResult.Value.Id,
                workResult.Value.WorkNumber,
                lines)),
            1,
            0,
            0);
    }

    private async Task<Location?> ResolveDestinationAsync(
        InventoryReplenishmentPolicy policy,
        CancellationToken cancellationToken)
    {
        if (policy.LocationId.HasValue)
        {
            return await context.Locations.SingleOrDefaultAsync(
                location => location.Id == policy.LocationId.Value &&
                            location.WarehouseId == policy.WarehouseId &&
                            location.IsActive &&
                            location.IsPickable,
                cancellationToken);
        }

        return await context.Locations
            .Where(location =>
                location.WarehouseId == policy.WarehouseId &&
                location.IsActive &&
                location.IsPickable &&
                (location.Type == LocationType.PickFace ||
                 location.Type == LocationType.Bin ||
                 location.Type == LocationType.Rack ||
                 location.Type == LocationType.Storage))
            .OrderBy(location => location.Priority)
            .ThenBy(location => location.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<decimal> GetAvailableCapacityAsync(
        Location destination,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (!destination.MaxUnits.HasValue || destination.MaxUnits.Value <= 0m)
        {
            return decimal.MaxValue;
        }

        var occupied = await context.InventoryBalances
            .Where(balance =>
                balance.WarehouseId == warehouseId &&
                balance.LocationId == destination.Id)
            .SumAsync(balance => balance.OnHandQuantity, cancellationToken);
        return Math.Max(0m, destination.MaxUnits.Value - occupied);
    }

    private static PlanResult BlockedPlan(
        InventoryReplenishmentSignalDto signal,
        int? destinationLocationId,
        decimal openQuantity,
        string decision) =>
        new(
            Fingerprinted(signal, new ReplenishmentWorkPlanDto(
                signal.PolicyId,
                signal.ItemId,
                signal.ItemSku,
                signal.WarehouseId,
                destinationLocationId,
                signal.ShortfallQuantity,
                openQuantity,
                0m,
                decision,
                null,
                null,
                [])),
            0,
            0,
            1);

    private static ReplenishmentWorkPlanDto Fingerprinted(
        InventoryReplenishmentSignalDto signal,
        ReplenishmentWorkPlanDto plan) =>
        plan with
        {
            SourceStateFingerprint = ReplenishmentWorkPlanFingerprint.Create(signal, plan)
        };

    private static int NormalizeLimit(int value) => value switch
    {
        < 1 => 1,
        > 1_000 => 1_000,
        _ => value
    };

    private sealed record PlanResult(
        ReplenishmentWorkPlanDto Plan,
        int WorkCreated,
        int WorkReused,
        int Blocked);
}
