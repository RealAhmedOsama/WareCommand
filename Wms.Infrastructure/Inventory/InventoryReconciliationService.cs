using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Read-only inventory reconciliation boundary. It uses server-side aggregate
/// joins for ledger/balance and reservation comparisons, then scans each source
/// table in bounded primary-key batches for row-level invariants. No entity is
/// mutated and no repair is attempted here.
/// </summary>
public sealed class InventoryReconciliationService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    ILogger<InventoryReconciliationService> logger)
    : IInventoryReconciliationService
{
    private const int MaximumBatchSize = 5_000;
    private const int MaximumIssueLimit = 10_000;

    public async Task<Result<InventoryReconciliationReportDto>> ReconcileAsync(
        InventoryReconciliationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = Validate(query);
        if (validation is not null)
        {
            return Result.Failure<InventoryReconciliationReportDto>(validation);
        }

        var startedAtUtc = clock.UtcNow;
        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryReconciliationReportDto>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var counters = new ReconciliationCounters();
            var issues = new IssueCollector(query.MaxIssues);

            await CompareLedgerToBalancesAsync(query, scope, counters, issues, cancellationToken);
            await ScanBalancesAsync(query, scope, counters, issues, cancellationToken);
            await ScanTransactionsAsync(query, scope, counters, issues, cancellationToken);

            if (query.Deep)
            {
                await CompareReservationsToBalancesAsync(
                    query,
                    scope,
                    counters,
                    issues,
                    cancellationToken);
                await ScanReservationsAsync(query, scope, counters, issues, cancellationToken);
                await ScanAllocationsAsync(query, scope, counters, issues, cancellationToken);
                await ScanSerialsAsync(query, scope, counters, issues, cancellationToken);
                await ScanLicensePlatesAsync(query, scope, counters, issues, cancellationToken);
                await ScanLicensePlateContentsAsync(
                    query,
                    scope,
                    counters,
                    issues,
                    cancellationToken);
            }

            var completedAtUtc = clock.UtcNow;
            var issueList = issues.Items.ToArray();
            return Result.Success(new InventoryReconciliationReportDto(
                startedAtUtc,
                completedAtUtc,
                query.Deep,
                issues.Count == 0,
                issues.IsTruncated,
                issues.Count,
                issues.CriticalCount,
                issues.WarningCount,
                counters.BatchesRead,
                counters.BalancesScanned,
                counters.TransactionsScanned,
                counters.ReservationsScanned,
                counters.AllocationsScanned,
                counters.SerialNumbersScanned,
                counters.LicensePlatesScanned,
                counters.LicensePlateContentsScanned,
                issueList));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory reconciliation failed");
            return Result.Failure<InventoryReconciliationReportDto>(WmsErrors.FromException(
                exception,
                "inventory.reconciliation_failed",
                "Inventory reconciliation could not be completed."));
        }
    }

    private async Task CompareLedgerToBalancesAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var balances = BuildBalanceQuery(query, scope);
        var aggregates = BuildLedgerAggregateQuery(query, scope);
        var aggregateOffset = 0;
        while (true)
        {
            var aggregateBatch = await aggregates
                .OrderBy(row => row.WarehouseId)
                .ThenBy(row => row.LocationId)
                .ThenBy(row => row.ItemId)
                .ThenBy(row => row.LotId)
                .ThenBy(row => row.SerialNumberId)
                .ThenBy(row => row.LicensePlateId)
                .ThenBy(row => row.InventoryStatusId)
                .Skip(aggregateOffset)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (aggregateBatch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            var keys = aggregateBatch.Select(ToKey).ToArray();
            var balanceByKey = await balances
                .Where(BuildBalanceKeyPredicate(keys))
                .ToDictionaryAsync(balance => balance.GetKey(), cancellationToken);
            foreach (var ledger in aggregateBatch)
            {
                var key = ToKey(ledger);
                var formattedKey = FormatDimension(key);
                if (!balanceByKey.TryGetValue(key, out var balance))
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.LedgerBalance,
                        "inventory_balance_missing",
                        $"Ledger dimension {formattedKey} has no materialized inventory balance.",
                        "Investigate the ledger/balance write transaction before any repair.",
                        "InventoryBalance",
                        formattedKey,
                        key.WarehouseId,
                        key.ItemId,
                        key.LocationId,
                        ledger.OnHandQuantity,
                        0m);
                    continue;
                }

                if (balance.OnHandQuantity != ledger.OnHandQuantity ||
                    balance.ReservedQuantity != ledger.ReservedQuantity)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.LedgerBalance,
                        "inventory_balance_mismatch",
                        $"Ledger dimension {formattedKey} differs from its materialized balance.",
                        "Compare immutable transaction history and the originating operation; do not edit ledger rows.",
                        "InventoryBalance",
                        formattedKey,
                        key.WarehouseId,
                        key.ItemId,
                        key.LocationId,
                        ledger.OnHandQuantity,
                        balance.OnHandQuantity);
                }
            }

            aggregateOffset += aggregateBatch.Count;
            if (aggregateBatch.Count < query.BatchSize)
            {
                break;
            }
        }

        var lastBalanceId = 0;
        while (true)
        {
            var balanceBatch = await balances
                .Where(balance => balance.Id > lastBalanceId)
                .OrderBy(balance => balance.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (balanceBatch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            var keys = balanceBatch.Select(balance => balance.GetKey()).ToArray();
            var existingKeys = (await BuildAllTransactionQuery(query, scope)
                    .Where(BuildTransactionKeyPredicate(keys))
                    .Select(transaction => new
                    {
                        transaction.WarehouseId,
                        transaction.LocationId,
                        transaction.ItemId,
                        transaction.LotId,
                        transaction.SerialNumberId,
                        transaction.SerialNumber,
                        transaction.LicensePlateId,
                        transaction.InventoryStatusId,
                        transaction.BaseUnitOfMeasure,
                        transaction.OwnerKind,
                        transaction.InventoryOwnerId,
                        transaction.OwnerCodeSnapshot
                    })
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .Select(row => new InventoryBalanceKey(
                    row.WarehouseId,
                    row.LocationId,
                    row.ItemId,
                    row.LotId,
                    row.SerialNumberId,
                    row.SerialNumber,
                    row.LicensePlateId,
                    row.InventoryStatusId,
                    row.BaseUnitOfMeasure,
                    row.OwnerKind,
                    row.InventoryOwnerId,
                    row.OwnerCodeSnapshot))
                .ToHashSet();

            foreach (var balance in balanceBatch)
            {
                if (existingKeys.Contains(balance.GetKey()))
                {
                    continue;
                }

                var key = balance.GetKey();
                var formattedKey = FormatDimension(key);
                issues.Add(
                    InventoryReconciliationSeverity.Critical,
                    InventoryReconciliationCheck.LedgerBalance,
                    "inventory_transactions_missing",
                    $"Materialized balance {formattedKey} has no immutable ledger history.",
                    "Determine whether this is an uncut legacy row or an incomplete ledger cutover.",
                    "InventoryBalance",
                    balance.Id.ToString(CultureInfo.InvariantCulture),
                    key.WarehouseId,
                    key.ItemId,
                    key.LocationId,
                    0m,
                    balance.OnHandQuantity);
            }

            lastBalanceId = balanceBatch[^1].Id;
        }
    }

    private async Task ScanBalancesAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var balances = BuildBalanceQuery(query, scope)
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Location)
            .Include(balance => balance.Item)
            .Include(balance => balance.InventoryStatus)
            .Include(balance => balance.Serial)
            .Include(balance => balance.LicensePlate);

        var lastId = 0;
        while (true)
        {
            var batch = await balances
                .Where(balance => balance.Id > lastId)
                .OrderBy(balance => balance.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.BalancesScanned += batch.Count;
            foreach (var balance in batch)
            {
                if (balance.OnHandQuantity < 0m && balance.Warehouse is { AllowNegativeStock: false })
                {
                    AddBalanceIssue(
                        issues,
                        "inventory_negative_on_hand",
                        "Inventory balance is negative in a warehouse that disallows negative stock.",
                        balance,
                        balance.OnHandQuantity,
                        0m);
                }

                if (balance.ReservedQuantity < 0m ||
                    balance.OnHandQuantity >= 0m && balance.ReservedQuantity > balance.OnHandQuantity)
                {
                    AddBalanceIssue(
                        issues,
                        "inventory_reservation_exceeds_balance",
                        "Inventory balance reservations are negative or exceed physical on-hand quantity.",
                        balance,
                        balance.OnHandQuantity,
                        balance.ReservedQuantity);
                }

                if (balance.Warehouse is null || balance.Location is null || balance.Item is null ||
                    balance.InventoryStatus is null)
                {
                    AddBalanceIssue(
                        issues,
                        "inventory_dimension_reference_missing",
                        "Inventory balance references a missing warehouse, location, item, or status dimension.",
                        balance,
                        null,
                        null);
                }

                if (balance.SerialNumberId.HasValue && balance.Serial is null)
                {
                    AddBalanceIssue(
                        issues,
                        "inventory_serial_reference_missing",
                        "Inventory balance references a serial number that cannot be loaded.",
                        balance,
                        null,
                        null);
                }

                if (balance.LicensePlateId.HasValue && balance.LicensePlate is null)
                {
                    AddBalanceIssue(
                        issues,
                        "inventory_license_plate_reference_missing",
                        "Inventory balance references a license plate that cannot be loaded.",
                        balance,
                        null,
                        null);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task ScanTransactionsAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var transactions = BuildTransactionQuery(query, scope)
            .Include(transaction => transaction.Warehouse)
            .Include(transaction => transaction.Location)
            .Include(transaction => transaction.Item)
            .Include(transaction => transaction.InventoryStatus)
            .Include(transaction => transaction.Movement)
            .Include(transaction => transaction.ReversalOfTransaction);

        var lastId = 0;
        while (true)
        {
            var batch = await transactions
                .Where(transaction => transaction.Id > lastId)
                .OrderBy(transaction => transaction.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.TransactionsScanned += batch.Count;
            foreach (var transaction in batch)
            {
                if (transaction.QuantityBefore + transaction.QuantityDelta != transaction.QuantityAfter ||
                    transaction.ReservedQuantityBefore + transaction.ReservedQuantityDelta !=
                    transaction.ReservedQuantityAfter)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.InventoryInvariant,
                        "inventory_ledger_equation_mismatch",
                        $"Ledger transaction {transaction.Id} violates its before-plus-delta equation.",
                        "Preserve the immutable row and investigate the write boundary or database corruption.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId,
                        transaction.QuantityBefore + transaction.QuantityDelta,
                        transaction.QuantityAfter);
                }

                if (transaction.QuantityAfter < 0m ||
                    transaction.ReservedQuantityAfter < 0m ||
                    transaction.ReservedQuantityAfter > transaction.QuantityAfter)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.InventoryInvariant,
                        "inventory_ledger_quantity_invalid",
                        $"Ledger transaction {transaction.Id} produces an invalid quantity state.",
                        "Compare the originating operation and warehouse negative-stock policy before any repair.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId,
                        transaction.QuantityAfter,
                        transaction.ReservedQuantityAfter);
                }

                if (transaction.Warehouse is null || transaction.Location is null ||
                    transaction.Item is null || transaction.InventoryStatus is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_ledger_dimension_reference_missing",
                        $"Ledger transaction {transaction.Id} has a missing inventory dimension reference.",
                        "Do not rewrite the transaction; investigate the database constraint or migration state.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId);
                }

                if (transaction.MovementId.HasValue && transaction.Movement is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_ledger_movement_reference_missing",
                        $"Ledger transaction {transaction.Id} references a missing movement.",
                        "Restore or investigate the originating operation reference; never delete ledger history.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId);
                }

                if (transaction.ReversalOfTransactionId == transaction.Id)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_ledger_self_reversal",
                        $"Ledger transaction {transaction.Id} reverses itself.",
                        "Review the reversal command and preserve both immutable rows.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId);
                }
                else if (transaction.ReversalOfTransactionId.HasValue &&
                         transaction.ReversalOfTransaction is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_ledger_reversal_reference_missing",
                        $"Ledger transaction {transaction.Id} references a missing reversal source.",
                        "Investigate the reversal operation and immutable ledger retention.",
                        "InventoryTransaction",
                        transaction.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        transaction.WarehouseId,
                        transaction.ItemId,
                        transaction.LocationId);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task CompareReservationsToBalancesAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var balances = BuildBalanceQuery(query, scope);
        var allocationTotals = BuildActiveAllocationAggregateQuery(query, scope);
        var allocationOffset = 0;
        while (true)
        {
            var allocationBatch = await allocationTotals
                .OrderBy(row => row.WarehouseId)
                .ThenBy(row => row.LocationId)
                .ThenBy(row => row.ItemId)
                .ThenBy(row => row.LotId)
                .ThenBy(row => row.SerialNumberId)
                .ThenBy(row => row.LicensePlateId)
                .ThenBy(row => row.InventoryStatusId)
                .Skip(allocationOffset)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (allocationBatch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            var keys = allocationBatch.Select(ToKey).ToArray();
            var balanceByKey = await balances
                .Where(BuildBalanceKeyPredicate(keys))
                .ToDictionaryAsync(balance => balance.GetKey(), cancellationToken);
            foreach (var allocation in allocationBatch)
            {
                var key = ToKey(allocation);
                var formattedKey = FormatDimension(key);
                var balanceReserved = balanceByKey.TryGetValue(key, out var balance)
                    ? balance.ReservedQuantity
                    : 0m;
                if (!balanceByKey.ContainsKey(key) || balanceReserved != allocation.RemainingQuantity)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReservationAllocation,
                        "inventory_reservation_balance_mismatch",
                        $"Active reservation remainder for {formattedKey} differs from the materialized reserved quantity.",
                        "Compare reservation events, allocation rows, and the ledger reservation delta before any repair.",
                        "InventoryReservationAllocation",
                        formattedKey,
                        key.WarehouseId,
                        key.ItemId,
                        key.LocationId,
                        allocation.RemainingQuantity,
                        balanceReserved);
                }
            }

            allocationOffset += allocationBatch.Count;
            if (allocationBatch.Count < query.BatchSize)
            {
                break;
            }
        }

        var lastBalanceId = 0;
        var reservedBalances = balances.Where(balance => balance.ReservedQuantity != 0m);
        while (true)
        {
            var balanceBatch = await reservedBalances
                .Where(balance => balance.Id > lastBalanceId)
                .OrderBy(balance => balance.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (balanceBatch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            var keys = balanceBatch.Select(balance => balance.GetKey()).ToArray();
            var allocationKeys = (await BuildAllocationQuery(query, scope)
                    .Where(allocation =>
                        allocation.AllocatedQuantity -
                            allocation.ConsumedQuantity -
                            allocation.ReleasedQuantity > 0m)
                    .Where(BuildAllocationKeyPredicate(keys))
                    .Select(allocation => new
                    {
                        allocation.WarehouseId,
                        allocation.LocationId,
                        allocation.ItemId,
                        allocation.LotId,
                        allocation.SerialNumberId,
                        allocation.SerialNumber,
                        allocation.LicensePlateId,
                        allocation.InventoryStatusId,
                        allocation.BaseUnitOfMeasure,
                        allocation.OwnerKind,
                        allocation.InventoryOwnerId,
                        allocation.OwnerCodeSnapshot
                    })
                    .Distinct()
                    .ToListAsync(cancellationToken))
                .Select(row => new InventoryBalanceKey(
                    row.WarehouseId,
                    row.LocationId,
                    row.ItemId,
                    row.LotId,
                    row.SerialNumberId,
                    row.SerialNumber,
                    row.LicensePlateId,
                    row.InventoryStatusId,
                    row.BaseUnitOfMeasure,
                    row.OwnerKind,
                    row.InventoryOwnerId,
                    row.OwnerCodeSnapshot))
                .ToHashSet();

            foreach (var balance in balanceBatch)
            {
                var key = balance.GetKey();
                if (allocationKeys.Contains(key))
                {
                    continue;
                }

                issues.Add(
                    InventoryReconciliationSeverity.Critical,
                    InventoryReconciliationCheck.ReservationAllocation,
                    "inventory_reservation_allocation_missing",
                    $"Balance {balance.Id} has reserved quantity but no active reservation allocation.",
                    "Trace the reservation ledger entries and demand lifecycle before any repair.",
                    "InventoryBalance",
                    balance.Id.ToString(CultureInfo.InvariantCulture),
                    key.WarehouseId,
                    key.ItemId,
                    key.LocationId,
                    0m,
                    balance.ReservedQuantity);
            }

            lastBalanceId = balanceBatch[^1].Id;
        }
    }

    private async Task ScanReservationsAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var reservations = BuildReservationQuery(query, scope)
            .Include(reservation => reservation.Warehouse)
            .Include(reservation => reservation.Item);
        var allocationTotals = context.InventoryReservationAllocations
            .AsNoTracking()
            .GroupBy(allocation => allocation.ReservationId)
            .Select(group => new
            {
                ReservationId = group.Key,
                ConsumedQuantity = group.Sum(allocation => allocation.ConsumedQuantity),
                RemainingQuantity = group.Sum(allocation =>
                    allocation.AllocatedQuantity - allocation.ConsumedQuantity - allocation.ReleasedQuantity)
            });

        var lastId = 0;
        while (true)
        {
            var batch = await reservations
                .Where(reservation => reservation.Id > lastId)
                .OrderBy(reservation => reservation.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.ReservationsScanned += batch.Count;
            var ids = batch.Select(reservation => reservation.Id).ToArray();
            var totals = await allocationTotals
                .Where(total => ids.Contains(total.ReservationId))
                .ToDictionaryAsync(total => total.ReservationId, cancellationToken);
            foreach (var reservation in batch)
            {
                totals.TryGetValue(reservation.Id, out var total);
                var remaining = total?.RemainingQuantity ?? 0m;
                var consumedAndReserved = (total?.ConsumedQuantity ?? 0m) + remaining;
                if (consumedAndReserved > reservation.RequestedQuantity)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReservationAllocation,
                        "inventory_reservation_overallocated",
                        $"Reservation {reservation.Id} has consumed or active allocation quantities beyond its requested quantity.",
                        "Review allocation selection, consumption, and immutable reservation events before any repair.",
                        "InventoryReservation",
                        reservation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        reservation.WarehouseId,
                        reservation.ItemId,
                        expectedQuantity: reservation.RequestedQuantity,
                        actualQuantity: consumedAndReserved);
                }

                if (remaining > 0m && !reservation.IsOpen)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReservationAllocation,
                        "inventory_reservation_status_mismatch",
                        $"Reservation {reservation.Id} has an open allocation remainder but is {reservation.Status}.",
                        "Reconcile reservation status transitions with the allocation event history.",
                        "InventoryReservation",
                        reservation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        reservation.WarehouseId,
                        reservation.ItemId,
                        expectedQuantity: 0m,
                        actualQuantity: remaining);
                }

                if (reservation.Warehouse is null || reservation.Item is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_reservation_reference_missing",
                        $"Reservation {reservation.Id} has a missing warehouse or item reference.",
                        "Investigate referential integrity and migration state; do not silently delete the reservation.",
                        "InventoryReservation",
                        reservation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        reservation.WarehouseId,
                        reservation.ItemId);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task ScanAllocationsAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var allocations = BuildAllocationQuery(query, scope)
            .Include(allocation => allocation.Reservation)
            .Include(allocation => allocation.Warehouse)
            .Include(allocation => allocation.Location)
            .Include(allocation => allocation.Item)
            .Include(allocation => allocation.InventoryStatus);
        var lastId = 0;
        while (true)
        {
            var batch = await allocations
                .Where(allocation => allocation.Id > lastId)
                .OrderBy(allocation => allocation.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.AllocationsScanned += batch.Count;
            foreach (var allocation in batch)
            {
                var remaining = allocation.AllocatedQuantity -
                    allocation.ConsumedQuantity - allocation.ReleasedQuantity;
                if (allocation.AllocatedQuantity <= 0m ||
                    allocation.ConsumedQuantity < 0m ||
                    allocation.ReleasedQuantity < 0m ||
                    remaining < 0m)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReservationAllocation,
                        "inventory_reservation_allocation_quantity_invalid",
                        $"Reservation allocation {allocation.Id} has invalid quantity accounting.",
                        "Preserve the allocation history and compare it with reservation events and ledger rows.",
                        "InventoryReservationAllocation",
                        allocation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        allocation.WarehouseId,
                        allocation.ItemId,
                        allocation.LocationId,
                        allocation.AllocatedQuantity,
                        remaining);
                }

                if (allocation.Reservation is null ||
                    allocation.Reservation.WarehouseId != allocation.WarehouseId ||
                    allocation.Reservation.ItemId != allocation.ItemId)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_reservation_allocation_parent_mismatch",
                        $"Reservation allocation {allocation.Id} does not match its reservation identity.",
                        "Review the allocation command and the reservation aggregate before any repair.",
                        "InventoryReservationAllocation",
                        allocation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        allocation.WarehouseId,
                        allocation.ItemId,
                        allocation.LocationId);
                }

                if (allocation.Warehouse is null || allocation.Location is null ||
                    allocation.Item is null || allocation.InventoryStatus is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_reservation_allocation_reference_missing",
                        $"Reservation allocation {allocation.Id} has a missing dimension reference.",
                        "Investigate referential integrity and migration state before any repair.",
                        "InventoryReservationAllocation",
                        allocation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        allocation.WarehouseId,
                        allocation.ItemId,
                        allocation.LocationId);
                }

                if (remaining > 0m && allocation.Status is not
                    (InventoryReservationAllocationStatus.Active or
                    InventoryReservationAllocationStatus.PartiallyConsumed))
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReservationAllocation,
                        "inventory_reservation_allocation_status_mismatch",
                        $"Allocation {allocation.Id} has a remainder but is {allocation.Status}.",
                        "Reconcile allocation lifecycle events with the status transition.",
                        "InventoryReservationAllocation",
                        allocation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        allocation.WarehouseId,
                        allocation.ItemId,
                        allocation.LocationId,
                        expectedQuantity: 0m,
                        actualQuantity: remaining);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task ScanSerialsAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var serials = context.SerialNumbers
            .AsNoTracking()
            .Include(serial => serial.Item)
            .Include(serial => serial.CurrentWarehouse)
            .Include(serial => serial.CurrentLocation)
            .Include(serial => serial.CurrentLicensePlateEntity)
            .Where(serial =>
                scope.HasGlobalAccess ||
                serial.CurrentWarehouseId.HasValue &&
                    scope.WarehouseIds.Contains(serial.CurrentWarehouseId.Value) ||
                !serial.CurrentWarehouseId.HasValue &&
                    context.InventoryBalances.Any(balance =>
                        balance.SerialNumberId == serial.Id &&
                        scope.WarehouseIds.Contains(balance.WarehouseId)));
        if (query.WarehouseId.HasValue)
        {
            serials = serials.Where(serial =>
                serial.CurrentWarehouseId == query.WarehouseId.Value ||
                context.InventoryBalances.Any(balance =>
                    balance.SerialNumberId == serial.Id &&
                    balance.WarehouseId == query.WarehouseId.Value));
        }

        if (query.ItemId.HasValue)
        {
            serials = serials.Where(serial => serial.ItemId == query.ItemId.Value);
        }

        var lastId = 0;
        while (true)
        {
            var batch = await serials
                .Where(serial => serial.Id > lastId)
                .OrderBy(serial => serial.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.SerialNumbersScanned += batch.Count;
            var ids = batch.Select(serial => serial.Id).ToArray();
            var balances = await BuildBalanceQuery(query, scope)
                .Where(balance => balance.SerialNumberId.HasValue && ids.Contains(balance.SerialNumberId.Value))
                .ToListAsync(cancellationToken);
            foreach (var serial in batch)
            {
                var serialBalances = balances
                    .Where(balance => balance.SerialNumberId == serial.Id && balance.OnHandQuantity > 0m)
                    .ToArray();
                if (serialBalances.Length > 1 || serialBalances.Any(balance => balance.OnHandQuantity != 1m))
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.SerialPlacement,
                        "inventory_serial_quantity_not_unique",
                        $"Serial {serial.Number} has {serialBalances.Length} positive balance rows or a quantity other than one.",
                        "Compare serial lifecycle, ledger dimensions, and any LPN content before repair.",
                        "SerialNumber",
                        serial.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        serial.CurrentWarehouseId,
                        serial.ItemId,
                        serial.CurrentLocationId,
                        1m,
                        serialBalances.Sum(balance => balance.OnHandQuantity));
                }

                if (serial.IsAllocationEligible && serialBalances.Length == 0)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Warning,
                        InventoryReconciliationCheck.SerialPlacement,
                        "inventory_serial_positive_balance_missing",
                        $"Eligible serial {serial.Number} has no positive canonical inventory balance.",
                        "Trace the latest receipt, move, pick, or migration event before repair.",
                        "SerialNumber",
                        serial.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        serial.CurrentWarehouseId,
                        serial.ItemId,
                        serial.CurrentLocationId,
                        1m,
                        0m);
                }

                if (serial.CurrentWarehouseId.HasValue && serial.CurrentLocationId is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.SerialPlacement,
                        "inventory_serial_location_missing",
                        $"Serial {serial.Number} has a warehouse placement without a location.",
                        "Resolve the serial placement through a controlled inventory operation.",
                        "SerialNumber",
                        serial.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        serial.CurrentWarehouseId,
                        serial.ItemId);
                }

                var balance = serialBalances.SingleOrDefault();
                if (balance is not null &&
                    (balance.WarehouseId != serial.CurrentWarehouseId ||
                     balance.LocationId != serial.CurrentLocationId ||
                     balance.LicensePlateId != serial.CurrentLicensePlateId))
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.SerialPlacement,
                        "inventory_serial_placement_mismatch",
                        $"Serial {serial.Number} placement differs from its canonical inventory dimension.",
                        "Compare serial lifecycle and balance ledger dimensions; do not edit either source independently.",
                        "SerialNumber",
                        serial.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        serial.CurrentWarehouseId,
                        serial.ItemId,
                        serial.CurrentLocationId);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task ScanLicensePlatesAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var plates = context.LicensePlates
            .AsNoTracking()
            .Include(plate => plate.Warehouse)
            .Include(plate => plate.CurrentLocation)
            .Where(plate => scope.HasGlobalAccess || scope.WarehouseIds.Contains(plate.WarehouseId));
        if (query.WarehouseId.HasValue)
        {
            plates = plates.Where(plate => plate.WarehouseId == query.WarehouseId.Value);
        }

        var lastId = 0;
        while (true)
        {
            var batch = await plates
                .Where(plate => plate.Id > lastId)
                .OrderBy(plate => plate.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.LicensePlatesScanned += batch.Count;
            foreach (var plate in batch)
            {
                if (plate.Warehouse is null ||
                    plate.CurrentLocationId.HasValue && plate.CurrentLocation is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_license_plate_reference_missing",
                        $"License plate {plate.Number} has a missing warehouse or location reference.",
                        "Investigate the plate lifecycle and warehouse/location master data.",
                        "LicensePlate",
                        plate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        plate.WarehouseId,
                        null,
                        plate.CurrentLocationId);
                }

                if (plate.Status is LicensePlateStatus.Shipped or LicensePlateStatus.Voided &&
                    plate.CurrentLocationId.HasValue)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.LicensePlateContent,
                        "inventory_license_plate_location_after_close",
                        $"License plate {plate.Number} is {plate.Status} but still has a current location.",
                        "Compare the shipment/void lifecycle with the plate state transition.",
                        "LicensePlate",
                        plate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        plate.WarehouseId,
                        null,
                        plate.CurrentLocationId);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private async Task ScanLicensePlateContentsAsync(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope,
        ReconciliationCounters counters,
        IssueCollector issues,
        CancellationToken cancellationToken)
    {
        var contents = context.LicensePlateContents
            .AsNoTracking()
            .Include(content => content.LicensePlate)
            .Include(content => content.Item)
            .Include(content => content.SerialNumber)
            .Include(content => content.InventoryStatus)
            .Where(content =>
                scope.HasGlobalAccess ||
                scope.WarehouseIds.Contains(content.LicensePlate.WarehouseId));
        if (query.WarehouseId.HasValue)
        {
            contents = contents.Where(content =>
                content.LicensePlate.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ItemId.HasValue)
        {
            contents = contents.Where(content => content.ItemId == query.ItemId.Value);
        }

        var lastId = 0;
        while (true)
        {
            var batch = await contents
                .Where(content => content.Id > lastId)
                .OrderBy(content => content.Id)
                .Take(query.BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            counters.BatchesRead++;
            counters.LicensePlateContentsScanned += batch.Count;
            foreach (var content in batch)
            {
                if (content.LicensePlate is null || content.Item is null ||
                    content.InventoryStatus is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.ReferenceIntegrity,
                        "inventory_license_plate_content_reference_missing",
                        $"License plate content {content.Id} has a missing parent, item, or status reference.",
                        "Investigate the LPN content mutation and master-data constraints.",
                        "LicensePlateContent",
                        content.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        content.LicensePlate?.WarehouseId,
                        content.ItemId);
                }

                if (content.Quantity.Value <= 0m ||
                    content.SerialNumberId.HasValue && content.Quantity.Value != 1m)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.LicensePlateContent,
                        "inventory_license_plate_content_quantity_invalid",
                        $"License plate content {content.Id} has invalid quantity accounting.",
                        "Compare LPN content history, serial identity, and the corresponding balance dimension.",
                        "LicensePlateContent",
                        content.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        content.LicensePlate?.WarehouseId,
                        content.ItemId,
                        expectedQuantity: content.SerialNumberId.HasValue ? 1m : null,
                        actualQuantity: content.Quantity.Value);
                }

                if (content.SerialNumberId.HasValue && content.SerialNumber is null)
                {
                    issues.Add(
                        InventoryReconciliationSeverity.Critical,
                        InventoryReconciliationCheck.SerialPlacement,
                        "inventory_license_plate_serial_reference_missing",
                        $"License plate content {content.Id} references a missing serial entity.",
                        "Trace the serial/LPN mutation and preserve the immutable inventory history.",
                        "LicensePlateContent",
                        content.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        content.LicensePlate?.WarehouseId,
                        content.ItemId);
                }
            }

            lastId = batch[^1].Id;
        }
    }

    private IQueryable<InventoryBalance> BuildBalanceQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope)
    {
        var balances = context.InventoryBalances.AsNoTracking();
        if (!scope.HasGlobalAccess)
        {
            balances = balances.Where(balance => scope.WarehouseIds.Contains(balance.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            balances = balances.Where(balance => balance.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ItemId.HasValue)
        {
            balances = balances.Where(balance => balance.ItemId == query.ItemId.Value);
        }

        return balances;
    }

    private IQueryable<InventoryTransaction> BuildTransactionQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope)
    {
        var transactions = BuildAllTransactionQuery(query, scope);
        if (query.FromUtc.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.OccurredAtUtc >= query.FromUtc.Value.UtcDateTime);
        }

        if (query.ToUtc.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.OccurredAtUtc < query.ToUtc.Value.UtcDateTime);
        }

        return transactions;
    }

    private IQueryable<InventoryTransaction> BuildAllTransactionQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope)
    {
        var transactions = context.InventoryTransactions.AsNoTracking();
        if (!scope.HasGlobalAccess)
        {
            transactions = transactions.Where(transaction =>
                scope.WarehouseIds.Contains(transaction.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ItemId.HasValue)
        {
            transactions = transactions.Where(transaction =>
                transaction.ItemId == query.ItemId.Value);
        }

        return transactions;
    }

    private IQueryable<InventoryReservation> BuildReservationQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope)
    {
        var reservations = context.InventoryReservations.AsNoTracking();
        if (!scope.HasGlobalAccess)
        {
            reservations = reservations.Where(reservation =>
                scope.WarehouseIds.Contains(reservation.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            reservations = reservations.Where(reservation =>
                reservation.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ItemId.HasValue)
        {
            reservations = reservations.Where(reservation =>
                reservation.ItemId == query.ItemId.Value);
        }

        return reservations;
    }

    private IQueryable<InventoryReservationAllocation> BuildAllocationQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope)
    {
        var allocations = context.InventoryReservationAllocations.AsNoTracking();
        if (!scope.HasGlobalAccess)
        {
            allocations = allocations.Where(allocation =>
                scope.WarehouseIds.Contains(allocation.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            allocations = allocations.Where(allocation =>
                allocation.WarehouseId == query.WarehouseId.Value);
        }

        if (query.ItemId.HasValue)
        {
            allocations = allocations.Where(allocation =>
                allocation.ItemId == query.ItemId.Value);
        }

        return allocations;
    }

    private IQueryable<LedgerAggregateRow> BuildLedgerAggregateQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope) =>
        BuildAllTransactionQuery(query, scope)
            .GroupBy(transaction => new
            {
                transaction.WarehouseId,
                transaction.LocationId,
                transaction.ItemId,
                transaction.LotId,
                transaction.SerialNumberId,
                transaction.SerialNumber,
                transaction.LicensePlateId,
                transaction.InventoryStatusId,
                transaction.BaseUnitOfMeasure,
                transaction.OwnerKind,
                transaction.InventoryOwnerId,
                transaction.OwnerCodeSnapshot
            })
            .Select(group => new LedgerAggregateRow
            {
                WarehouseId = group.Key.WarehouseId,
                LocationId = group.Key.LocationId,
                ItemId = group.Key.ItemId,
                LotId = group.Key.LotId,
                SerialNumberId = group.Key.SerialNumberId,
                SerialNumber = group.Key.SerialNumber,
                LicensePlateId = group.Key.LicensePlateId,
                InventoryStatusId = group.Key.InventoryStatusId,
                BaseUnitOfMeasure = group.Key.BaseUnitOfMeasure,
                OwnerKind = group.Key.OwnerKind,
                InventoryOwnerId = group.Key.InventoryOwnerId,
                OwnerCodeSnapshot = group.Key.OwnerCodeSnapshot,
                OnHandQuantity = group.Sum(transaction => transaction.QuantityDelta),
                ReservedQuantity = group.Sum(transaction => transaction.ReservedQuantityDelta)
            });

    private IQueryable<AllocationAggregateRow> BuildActiveAllocationAggregateQuery(
        InventoryReconciliationQuery query,
        WarehouseAccessScope scope) =>
        BuildAllocationQuery(query, scope)
            .Where(allocation =>
                allocation.AllocatedQuantity -
                    allocation.ConsumedQuantity -
                    allocation.ReleasedQuantity > 0m)
            .GroupBy(allocation => new
            {
                allocation.WarehouseId,
                allocation.LocationId,
                allocation.ItemId,
                allocation.LotId,
                allocation.SerialNumberId,
                allocation.SerialNumber,
                allocation.LicensePlateId,
                allocation.InventoryStatusId,
                allocation.BaseUnitOfMeasure,
                allocation.OwnerKind,
                allocation.InventoryOwnerId,
                allocation.OwnerCodeSnapshot
            })
            .Select(group => new AllocationAggregateRow
            {
                WarehouseId = group.Key.WarehouseId,
                LocationId = group.Key.LocationId,
                ItemId = group.Key.ItemId,
                LotId = group.Key.LotId,
                SerialNumberId = group.Key.SerialNumberId,
                SerialNumber = group.Key.SerialNumber,
                LicensePlateId = group.Key.LicensePlateId,
                InventoryStatusId = group.Key.InventoryStatusId,
                BaseUnitOfMeasure = group.Key.BaseUnitOfMeasure,
                OwnerKind = group.Key.OwnerKind,
                InventoryOwnerId = group.Key.InventoryOwnerId,
                OwnerCodeSnapshot = group.Key.OwnerCodeSnapshot,
                RemainingQuantity = group.Sum(allocation =>
                    allocation.AllocatedQuantity -
                    allocation.ConsumedQuantity -
                    allocation.ReleasedQuantity)
            });

    private static void AddBalanceIssue(
        IssueCollector issues,
        string code,
        string message,
        InventoryBalance balance,
        decimal? expected,
        decimal? actual) =>
        issues.Add(
            InventoryReconciliationSeverity.Critical,
            InventoryReconciliationCheck.InventoryInvariant,
            code,
            message,
            "Investigate the inventory mutation and ledger state before any repair.",
            "InventoryBalance",
            balance.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            balance.WarehouseId,
            balance.ItemId,
            balance.LocationId,
            expected,
            actual);

    private static ResultError? Validate(InventoryReconciliationQuery query)
    {
        if (query.WarehouseId is <= 0 || query.ItemId is <= 0)
        {
            return WmsErrors.Validation(
                "inventory.reconciliation_scope_invalid",
                "Warehouse and item identifiers must be positive when supplied.");
        }

        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc >= query.ToUtc)
        {
            return WmsErrors.Validation(
                "inventory.reconciliation_date_range_invalid",
                "The reconciliation start must be before its end.");
        }

        if (query.BatchSize is < 1 or > MaximumBatchSize)
        {
            return WmsErrors.Validation(
                "inventory.reconciliation_batch_invalid",
                $"Batch size must be between 1 and {MaximumBatchSize}.");
        }

        if (query.MaxIssues is < 1 or > MaximumIssueLimit)
        {
            return WmsErrors.Validation(
                "inventory.reconciliation_issue_limit_invalid",
                $"Issue limit must be between 1 and {MaximumIssueLimit}.");
        }

        return null;
    }

    private static string FormatDimension(
        InventoryBalanceKey key) =>
        FormatDimension(
            key.WarehouseId,
            key.LocationId,
            key.ItemId,
            key.LotId,
            key.SerialNumberId,
            key.LicensePlateId,
            key.InventoryStatusId,
            key.BaseUnitOfMeasure,
            key.OwnerKind,
            key.InventoryOwnerId,
            key.OwnerCodeSnapshot);

    private static string FormatDimension(
        int warehouseId,
        int locationId,
        int itemId,
        int? lotId,
        int? serialNumberId,
        int? licensePlateId,
        int inventoryStatusId,
        string baseUnitOfMeasure,
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string ownerCodeSnapshot) =>
        $"warehouse={warehouseId.ToString(CultureInfo.InvariantCulture)};" +
        $"location={locationId.ToString(CultureInfo.InvariantCulture)};" +
        $"item={itemId.ToString(CultureInfo.InvariantCulture)};" +
        $"lot={lotId?.ToString(CultureInfo.InvariantCulture) ?? "-"};" +
        $"serial={serialNumberId?.ToString(CultureInfo.InvariantCulture) ?? "-"};" +
        $"lpn={licensePlateId?.ToString(CultureInfo.InvariantCulture) ?? "-"};" +
        $"status={inventoryStatusId.ToString(CultureInfo.InvariantCulture)};uom={baseUnitOfMeasure};" +
        $"owner={ownerKind}:{inventoryOwnerId?.ToString(CultureInfo.InvariantCulture) ?? "-"}:{ownerCodeSnapshot}";

    private static InventoryBalanceKey ToKey(LedgerAggregateRow row) => new(
        row.WarehouseId,
        row.LocationId,
        row.ItemId,
        row.LotId,
        row.SerialNumberId,
        row.SerialNumber,
        row.LicensePlateId,
        row.InventoryStatusId,
        row.BaseUnitOfMeasure,
        row.OwnerKind,
        row.InventoryOwnerId,
        row.OwnerCodeSnapshot);

    private static InventoryBalanceKey ToKey(AllocationAggregateRow row) => new(
        row.WarehouseId,
        row.LocationId,
        row.ItemId,
        row.LotId,
        row.SerialNumberId,
        row.SerialNumber,
        row.LicensePlateId,
        row.InventoryStatusId,
        row.BaseUnitOfMeasure,
        row.OwnerKind,
        row.InventoryOwnerId,
        row.OwnerCodeSnapshot);

    private static Expression<Func<InventoryBalance, bool>> BuildBalanceKeyPredicate(
        IReadOnlyCollection<InventoryBalanceKey> keys) =>
        BuildDimensionKeyPredicate<InventoryBalance>(keys);

    private static Expression<Func<InventoryTransaction, bool>> BuildTransactionKeyPredicate(
        IReadOnlyCollection<InventoryBalanceKey> keys) =>
        BuildDimensionKeyPredicate<InventoryTransaction>(keys);

    private static Expression<Func<InventoryReservationAllocation, bool>> BuildAllocationKeyPredicate(
        IReadOnlyCollection<InventoryBalanceKey> keys) =>
        BuildDimensionKeyPredicate<InventoryReservationAllocation>(keys);

    private static Expression<Func<T, bool>> BuildDimensionKeyPredicate<T>(
        IReadOnlyCollection<InventoryBalanceKey> keys)
    {
        var parameter = Expression.Parameter(typeof(T), "row");
        Expression? predicate = null;
        foreach (var key in keys)
        {
            var keyPredicate = Expression.AndAlso(
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.WarehouseId)),
                    Expression.Constant(key.WarehouseId)),
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.LocationId)),
                    Expression.Constant(key.LocationId)));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.ItemId)),
                    Expression.Constant(key.ItemId)));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(parameter, nameof(InventoryBalance.LotId), key.LotId));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(parameter, nameof(InventoryBalance.SerialNumberId), key.SerialNumberId));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(parameter, nameof(InventoryBalance.SerialNumber), key.SerialNumber));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(parameter, nameof(InventoryBalance.LicensePlateId), key.LicensePlateId));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.InventoryStatusId)),
                    Expression.Constant(key.InventoryStatusId)));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.BaseUnitOfMeasure)),
                    Expression.Constant(key.BaseUnitOfMeasure)));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                Expression.Equal(
                    Expression.Property(parameter, nameof(InventoryBalance.OwnerKind)),
                    Expression.Constant(key.OwnerKind)));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(
                    parameter,
                    nameof(InventoryBalance.InventoryOwnerId),
                    key.InventoryOwnerId));
            keyPredicate = Expression.AndAlso(
                keyPredicate,
                EqualNullable(
                    parameter,
                    nameof(InventoryBalance.OwnerCodeSnapshot),
                    key.OwnerCodeSnapshot));
            predicate = predicate is null
                ? keyPredicate
                : Expression.OrElse(predicate, keyPredicate);
        }

        return Expression.Lambda<Func<T, bool>>(
            predicate ?? Expression.Constant(false),
            parameter);
    }

    private static BinaryExpression EqualNullable<TValue>(
        ParameterExpression parameter,
        string propertyName,
        TValue? value)
        where TValue : struct =>
        Expression.Equal(
            Expression.Property(parameter, propertyName),
            Expression.Constant(value, typeof(TValue?)));

    private static BinaryExpression EqualNullable(
        ParameterExpression parameter,
        string propertyName,
        string? value) =>
        Expression.Equal(
            Expression.Property(parameter, propertyName),
            Expression.Constant(value, typeof(string)));

    private sealed class LedgerAggregateRow
    {
        public int WarehouseId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public int? LotId { get; init; }
        public int? SerialNumberId { get; init; }
        public string? SerialNumber { get; init; }
        public int? LicensePlateId { get; init; }
        public int InventoryStatusId { get; init; }
        public string BaseUnitOfMeasure { get; init; } = string.Empty;
        public InventoryOwnerKind OwnerKind { get; init; }
        public int? InventoryOwnerId { get; init; }
        public string OwnerCodeSnapshot { get; init; } = InventoryOwnershipDimension.CompanyOwnerCode;
        public decimal OnHandQuantity { get; init; }
        public decimal ReservedQuantity { get; init; }
    }

    private sealed class AllocationAggregateRow
    {
        public int WarehouseId { get; init; }
        public int LocationId { get; init; }
        public int ItemId { get; init; }
        public int? LotId { get; init; }
        public int? SerialNumberId { get; init; }
        public string? SerialNumber { get; init; }
        public int? LicensePlateId { get; init; }
        public int InventoryStatusId { get; init; }
        public string BaseUnitOfMeasure { get; init; } = string.Empty;
        public InventoryOwnerKind OwnerKind { get; init; }
        public int? InventoryOwnerId { get; init; }
        public string OwnerCodeSnapshot { get; init; } = InventoryOwnershipDimension.CompanyOwnerCode;
        public decimal RemainingQuantity { get; init; }
    }

    private sealed class ReconciliationCounters
    {
        public long BatchesRead { get; set; }
        public long BalancesScanned { get; set; }
        public long TransactionsScanned { get; set; }
        public long ReservationsScanned { get; set; }
        public long AllocationsScanned { get; set; }
        public long SerialNumbersScanned { get; set; }
        public long LicensePlatesScanned { get; set; }
        public long LicensePlateContentsScanned { get; set; }
    }

    private sealed class IssueCollector(int maximum)
    {
        private readonly List<InventoryReconciliationIssueDto> _items = new();

        public IReadOnlyList<InventoryReconciliationIssueDto> Items => _items;
        public int Count { get; private set; }
        public int CriticalCount { get; private set; }
        public int WarningCount { get; private set; }
        public bool IsTruncated { get; private set; }

        public void Add(
            InventoryReconciliationSeverity severity,
            InventoryReconciliationCheck check,
            string code,
            string message,
            string suggestedAction,
            string? referenceType = null,
            string? referenceId = null,
            int? warehouseId = null,
            int? itemId = null,
            int? locationId = null,
            decimal? expectedQuantity = null,
            decimal? actualQuantity = null)
        {
            Count++;
            if (severity == InventoryReconciliationSeverity.Critical)
            {
                CriticalCount++;
            }
            else
            {
                WarningCount++;
            }

            if (_items.Count < maximum)
            {
                _items.Add(new InventoryReconciliationIssueDto(
                    severity,
                    check,
                    code,
                    message,
                    suggestedAction,
                    referenceType,
                    referenceId,
                    warehouseId,
                    itemId,
                    locationId,
                    expectedQuantity,
                    actualQuantity));
            }
            else
            {
                IsTruncated = true;
            }
        }
    }
}
