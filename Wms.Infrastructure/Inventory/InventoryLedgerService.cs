using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// The only writer for materialized inventory balances. It appends immutable
/// ledger rows and applies the matching delta to the balance in the same unit
/// of work, allowing the caller to commit both with its movement/document work.
/// </summary>
public sealed class InventoryLedgerService : IInventoryLedgerService
{
    private readonly IClock _clock;
    private readonly WmsDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public InventoryLedgerService(
        IUnitOfWork unitOfWork,
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService,
        IClock clock)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _warehouseAccessService = warehouseAccessService;
        _clock = clock;
    }

    public async Task<IReadOnlyList<InventoryTransaction>> RecordAsync(
        IReadOnlyCollection<InventoryLedgerEntryRequest> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return Array.Empty<InventoryTransaction>();
        }

        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var operationGroupId = $"ledger:{Guid.NewGuid():N}";
        var operationCorrelationId = WmsExecutionIdentifiers.NewCorrelationId();
        var recorded = new List<InventoryTransaction>(entries.Count);
        var seenKeys = new HashSet<(string Key, int Sequence)>(StringTupleComparer.Instance);
        foreach (var request in entries)
        {
            ArgumentNullException.ThrowIfNull(request.Key);
        }

        // Every multi-dimension mutation takes balance locks in the same
        // canonical order. This prevents inverse moves from deadlocking each
        // other when they are committed concurrently.
        var orderedEntries = entries
            .Select((request, index) => (request, index))
            .OrderBy(value => value.request.Key.WarehouseId)
            .ThenBy(value => value.request.Key.LocationId)
            .ThenBy(value => value.request.Key.ItemId)
            .ThenBy(value => value.request.Key.LotId ?? 0)
            .ThenBy(value => value.request.Key.SerialNumberId ?? 0)
            .ThenBy(value => value.request.Key.SerialNumber ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(value => value.request.Key.LicensePlateId ?? 0)
            .ThenBy(value => value.request.Key.InventoryStatusId)
            .ThenBy(value => value.request.Key.BaseUnitOfMeasure, StringComparer.Ordinal)
            .ThenBy(value => value.index)
            .Select(value => value.request);

        foreach (var request in orderedEntries)
        {
            EnsureWarehouseScope(scope, request.Key.WarehouseId);
            await EnsureDimensionExistsAsync(request.Key, cancellationToken);

            var transactionGroupId = NormalizeOrDefault(
                request.TransactionGroupId,
                operationGroupId,
                100);
            var idempotencyKey = NormalizeOrDefault(
                request.IdempotencyKey,
                $"{transactionGroupId}:{request.EntrySequence}",
                250);
            if (!seenKeys.Add((idempotencyKey, request.EntrySequence)))
            {
                throw new InvalidOperationException(
                    $"Duplicate inventory ledger entry '{idempotencyKey}'/{request.EntrySequence} was supplied.");
            }

            var existing = await _unitOfWork.InventoryTransactions.GetByIdempotencyAsync(
                idempotencyKey,
                request.EntrySequence,
                cancellationToken);
            if (existing is not null)
            {
                recorded.Add(existing);
                continue;
            }

            var balance = await _unitOfWork.InventoryBalances.GetByDimensionAsync(
                request.Key,
                cancellationToken);
            if (balance is null)
            {
                balance = await CreateBalanceFromLegacyStockAsync(
                    request.Key,
                    cancellationToken);
            }

            var warehouseAllowsNegative = await _context.Warehouses
                .Where(warehouse => warehouse.Id == request.Key.WarehouseId)
                .Select(warehouse => warehouse.AllowNegativeStock)
                .SingleAsync(cancellationToken);

            var quantityBefore = balance.OnHandQuantity;
            var reservedQuantityBefore = balance.ReservedQuantity;
            balance.Apply(
                request.QuantityDelta,
                request.ReservedQuantityDelta,
                warehouseAllowsNegative);

            var transaction = new InventoryTransaction(
                request.Type,
                request.Key,
                request.QuantityDelta,
                quantityBefore,
                balance.OnHandQuantity,
                request.ReservedQuantityDelta,
                reservedQuantityBefore,
                balance.ReservedQuantity,
                request.ActorUserId,
                request.OccurredAtUtc ?? _clock.UtcNow.UtcDateTime,
                NormalizeOrDefault(
                    request.CorrelationId,
                    operationCorrelationId,
                    100),
                idempotencyKey,
                transactionGroupId,
                request.EntrySequence,
                request.ReferenceType,
                request.ReferenceId,
                request.ReferenceLine,
                request.Reason,
                request.MovementId is > 0 ? request.MovementId : null,
                request.ReversalOfTransactionId is > 0 ? request.ReversalOfTransactionId : null);

            await _unitOfWork.InventoryTransactions.AddAsync(transaction, cancellationToken);
            recorded.Add(transaction);
        }

        return recorded;
    }

    public Task<InventoryBalance?> GetBalanceAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.InventoryBalances.GetByDimensionAsync(key, cancellationToken);

    public async Task<InventoryReconciliationReport> ReconcileAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var balances = (await _unitOfWork.InventoryBalances.GetAllAsync(
            warehouseId,
            cancellationToken)).ToArray();
        var transactions = (await _unitOfWork.InventoryTransactions.GetAllAsync(
            warehouseId,
            cancellationToken)).ToArray();
        var issues = new List<InventoryReconciliationIssue>();

        var balanceByKey = balances.ToDictionary(balance => balance.GetKey());
        var transactionGroups = transactions
            .GroupBy(transaction => new InventoryBalanceKey(
                transaction.WarehouseId,
                transaction.LocationId,
                transaction.ItemId,
                transaction.LotId,
                transaction.SerialNumberId,
                transaction.SerialNumber,
                transaction.LicensePlateId,
                transaction.InventoryStatusId,
                transaction.BaseUnitOfMeasure));

        foreach (var group in transactionGroups)
        {
            var ledgerOnHand = group.Sum(transaction => transaction.QuantityDelta);
            var ledgerReserved = group.Sum(transaction => transaction.ReservedQuantityDelta);
            if (!balanceByKey.TryGetValue(group.Key, out var balance))
            {
                issues.Add(new InventoryReconciliationIssue(
                    group.Key,
                    0m,
                    ledgerOnHand,
                    0m,
                    ledgerReserved,
                    "inventory_balance_missing"));
                continue;
            }

            if (balance.OnHandQuantity != ledgerOnHand ||
                balance.ReservedQuantity != ledgerReserved)
            {
                issues.Add(new InventoryReconciliationIssue(
                    group.Key,
                    balance.OnHandQuantity,
                    ledgerOnHand,
                    balance.ReservedQuantity,
                    ledgerReserved,
                    "inventory_balance_mismatch"));
            }
        }

        foreach (var balance in balances)
        {
            if (transactions.Any(transaction =>
                    transaction.WarehouseId == balance.WarehouseId &&
                    transaction.LocationId == balance.LocationId &&
                    transaction.ItemId == balance.ItemId &&
                    transaction.LotId == balance.LotId &&
                    transaction.SerialNumberId == balance.SerialNumberId &&
                    transaction.SerialNumber == balance.SerialNumber &&
                    transaction.LicensePlateId == balance.LicensePlateId &&
                    transaction.InventoryStatusId == balance.InventoryStatusId &&
                    transaction.BaseUnitOfMeasure == balance.BaseUnitOfMeasure))
            {
                continue;
            }

            issues.Add(new InventoryReconciliationIssue(
                balance.GetKey(),
                balance.OnHandQuantity,
                0m,
                balance.ReservedQuantity,
                0m,
                "inventory_transactions_missing"));
        }

        return new InventoryReconciliationReport(
            balances.Length,
            transactions.Length,
            issues);
    }

    private async Task<InventoryBalance> CreateBalanceFromLegacyStockAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken)
    {
        var existingLegacyStock = await _unitOfWork.InventoryBalances.GetLegacyStockByDimensionAsync(
            key,
            cancellationToken);
        var balance = new InventoryBalance(key);
        await _unitOfWork.InventoryBalances.AddAsync(balance, cancellationToken);

        if (existingLegacyStock is null)
        {
            return balance;
        }

        var legacyOpeningQuantity = existingLegacyStock.QuantityAvailable.Value;
        var legacyOpeningReserved = existingLegacyStock.QuantityReserved.Value;
        if (_context.Entry(existingLegacyStock).State == EntityState.Modified)
        {
            // Mutation services update the compatibility Stock projection before
            // calling the ledger. Seed a cutover balance from the persisted
            // pre-mutation values, not the tracked post-mutation snapshot.
            var databaseValues = await _context.Entry(existingLegacyStock)
                .GetDatabaseValuesAsync(cancellationToken);
            if (databaseValues is not null)
            {
                legacyOpeningQuantity = ReadQuantity(
                    databaseValues[nameof(Stock.QuantityAvailable)]);
                legacyOpeningReserved = ReadQuantity(
                    databaseValues[nameof(Stock.QuantityReserved)]);
            }
        }

        if (_context.Entry(existingLegacyStock).State == EntityState.Added)
        {
            // The new Stock row is part of the same pending operation and has
            // no pre-ledger balance to carry forward.
            return balance;
        }

        var warehouseAllowsNegative = await _context.Warehouses
            .Where(warehouse => warehouse.Id == key.WarehouseId)
            .Select(warehouse => warehouse.AllowNegativeStock)
            .SingleAsync(cancellationToken);
        balance.Apply(
            legacyOpeningQuantity,
            legacyOpeningReserved,
            warehouseAllowsNegative);

        var legacyKey = $"legacy:stock:{existingLegacyStock.Id}";
        var opening = new InventoryTransaction(
            InventoryTransactionType.OpeningBalance,
            key,
            legacyOpeningQuantity,
            0m,
            legacyOpeningQuantity,
            legacyOpeningReserved,
            0m,
            legacyOpeningReserved,
            "system.migration",
            existingLegacyStock.CreatedAt,
            legacyKey,
            legacyKey,
            legacyKey,
            1,
            "Stock",
            existingLegacyStock.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason: "Legacy stock balance carried into the inventory ledger");
        await _unitOfWork.InventoryTransactions.AddAsync(opening, cancellationToken);
        return balance;
    }

    private static decimal ReadQuantity(object? value) => value switch
    {
        Quantity quantity => quantity.Value,
        decimal decimalValue => decimalValue,
        _ => throw new InvalidOperationException(
            "The persisted stock quantity could not be read for ledger cutover.")
    };

    private async Task EnsureDimensionExistsAsync(
        InventoryBalanceKey key,
        CancellationToken cancellationToken)
    {
        var location = await _context.Locations
            .AsNoTracking()
            .Where(location => location.Id == key.LocationId)
            .Select(location => new { location.Id, location.WarehouseId })
            .SingleOrDefaultAsync(cancellationToken);
        if (location is null || location.WarehouseId != key.WarehouseId)
        {
            throw new InvalidOperationException(
                "The inventory ledger dimension location does not belong to the requested warehouse.");
        }

        if (!await _context.Items.AnyAsync(item => item.Id == key.ItemId, cancellationToken))
        {
            throw new InvalidOperationException($"Item {key.ItemId} was not found.");
        }

        if (!await _context.InventoryStatuses.AnyAsync(
                status => status.Id == key.InventoryStatusId,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Inventory status {key.InventoryStatusId} was not found.");
        }
    }

    private static void EnsureWarehouseScope(
        WarehouseAccessScope scope,
        int warehouseId)
    {
        if (!scope.HasGlobalAccess && !scope.WarehouseIds.Contains(warehouseId))
        {
            throw new UnauthorizedAccessException(
                $"The current user is not authorized for warehouse {warehouseId}.");
        }
    }

    private static string NormalizeOrDefault(
        string? value,
        string fallback,
        int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Key, int Sequence)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Key, int Sequence) x, (string Key, int Sequence) y) =>
            StringComparer.Ordinal.Equals(x.Key, y.Key) && x.Sequence == y.Sequence;

        public int GetHashCode((string Key, int Sequence) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Key), obj.Sequence);
    }
}
