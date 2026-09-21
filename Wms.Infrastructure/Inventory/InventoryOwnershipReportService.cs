using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

public sealed class InventoryOwnershipReportService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    ILogger<InventoryOwnershipReportService> logger) : IInventoryOwnershipReportService
{
    private const int MaximumPageSize = 500;
    private const int MaximumStatementRows = 2_000;

    public async Task<Result<InventoryOwnershipReportDto>> QueryAsync(
        InventoryOwnershipReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = Validate(query);
        if (validation is not null)
        {
            return Result.Failure<InventoryOwnershipReportDto>(validation);
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.InventoryOwnershipRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InventoryOwnershipReportDto>();
        }

        try
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
            var balances = BuildBalanceQuery(query, scope);
            var balanceRows = await balances
                .OrderBy(balance => balance.OwnerKind)
                .ThenBy(balance => balance.OwnerCodeSnapshot)
                .ThenBy(balance => balance.ItemId)
                .ThenBy(balance => balance.LocationId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(balance => new BalanceProjection(
                    balance.WarehouseId,
                    balance.Warehouse.Code,
                    balance.LocationId,
                    balance.Location.Code,
                    balance.ItemId,
                    balance.Item.Sku,
                    balance.Item.Name,
                    balance.OwnerKind,
                    balance.InventoryOwnerId,
                    balance.OwnerCodeSnapshot,
                    balance.InventoryOwner == null
                        ? "Company owned"
                        : balance.InventoryOwner.DisplayName,
                    balance.OnHandQuantity,
                    balance.ReservedQuantity,
                    balance.OnHandQuantity - balance.ReservedQuantity,
                    balance.CreatedAt))
                .ToListAsync(cancellationToken);

            var generatedAtUtc = clock.UtcNow;
            var balanceResult = balanceRows
                .Select(row => new InventoryOwnershipBalanceReportRow(
                    row.WarehouseId,
                    row.WarehouseCode,
                    row.LocationId,
                    row.LocationCode,
                    row.ItemId,
                    row.ItemSku,
                    row.ItemName,
                    row.OwnerKind,
                    row.InventoryOwnerId,
                    row.OwnerCodeSnapshot,
                    row.OwnerDisplayName,
                    row.OnHandQuantity,
                    row.ReservedQuantity,
                    row.AvailableQuantity,
                    row.BalanceCreatedAtUtc,
                    Math.Max(0, (generatedAtUtc.UtcDateTime - row.BalanceCreatedAtUtc).Days)))
                .ToArray();

            var transactions = BuildTransactionQuery(query, scope);
            var usageGroups = await transactions
                .GroupBy(transaction => new
                {
                    transaction.OwnerKind,
                    transaction.InventoryOwnerId,
                    transaction.OwnerCodeSnapshot,
                    transaction.Type
                })
                .Select(group => new
                {
                    group.Key.OwnerKind,
                    group.Key.InventoryOwnerId,
                    group.Key.OwnerCodeSnapshot,
                    TransactionType = group.Key.Type,
                    TransactionCount = group.Count(),
                    QuantityDelta = group.Sum(transaction => transaction.QuantityDelta),
                    ReservedQuantityDelta = group.Sum(transaction => transaction.ReservedQuantityDelta)
                })
                .OrderBy(row => row.OwnerKind)
                .ThenBy(row => row.OwnerCodeSnapshot)
                .ThenBy(row => row.TransactionType)
                .ToListAsync(cancellationToken);
            var usage = usageGroups
                .Select(row => new InventoryOwnershipUsageReportRow(
                    row.OwnerKind,
                    row.InventoryOwnerId,
                    row.OwnerCodeSnapshot,
                    row.TransactionType,
                    row.TransactionCount,
                    row.QuantityDelta,
                    row.ReservedQuantityDelta))
                .ToArray();

            var statement = await transactions
                .OrderByDescending(transaction => transaction.OccurredAtUtc)
                .ThenByDescending(transaction => transaction.Id)
                .Take(MaximumStatementRows)
                .Select(transaction => new InventoryOwnershipStatementRow(
                    transaction.Id,
                    transaction.OccurredAtUtc,
                    transaction.OwnerKind,
                    transaction.InventoryOwnerId,
                    transaction.OwnerCodeSnapshot,
                    transaction.Type,
                    transaction.WarehouseId,
                    transaction.LocationId,
                    transaction.ItemId,
                    transaction.QuantityDelta,
                    transaction.ReservedQuantityDelta,
                    transaction.QuantityBefore,
                    transaction.QuantityAfter,
                    transaction.ReferenceType,
                    transaction.ReferenceId,
                    transaction.Reason,
                    transaction.ActorUserId))
                .ToListAsync(cancellationToken);

            return Result.Success(new InventoryOwnershipReportDto(
                generatedAtUtc,
                balanceResult,
                usage,
                statement,
                page,
                pageSize));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Inventory ownership report query failed");
            return Result.Failure<InventoryOwnershipReportDto>(WmsErrors.FromException(
                exception,
                "inventory.ownership_report_failed",
                "The inventory ownership report could not be loaded."));
        }
    }

    private IQueryable<InventoryBalance> BuildBalanceQuery(
        InventoryOwnershipReportQuery query,
        WarehouseAccessScope scope)
    {
        var balances = context.InventoryBalances
            .AsNoTracking()
            .Include(balance => balance.Warehouse)
            .Include(balance => balance.Location)
            .Include(balance => balance.Item)
            .Include(balance => balance.InventoryOwner)
            .AsQueryable();
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

        ApplyOwnerFilter(ref balances, query);
        return balances;
    }

    private IQueryable<InventoryTransaction> BuildTransactionQuery(
        InventoryOwnershipReportQuery query,
        WarehouseAccessScope scope)
    {
        var transactions = context.InventoryTransactions
            .AsNoTracking()
            .AsQueryable();
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
            transactions = transactions.Where(transaction => transaction.ItemId == query.ItemId.Value);
        }

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

        ApplyOwnerFilter(ref transactions, query);
        return transactions;
    }

    private static void ApplyOwnerFilter(
        ref IQueryable<InventoryBalance> query,
        InventoryOwnershipReportQuery report)
    {
        if (!report.OwnerKind.HasValue &&
            !report.InventoryOwnerId.HasValue &&
            string.IsNullOrWhiteSpace(report.OwnerCodeSnapshot))
        {
            return;
        }

        var ownerKind = report.OwnerKind ??
            (report.InventoryOwnerId.HasValue
                ? InventoryOwnerKind.ExternalOwner
                : InventoryOwnerKind.CompanyOwned);
        var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            report.InventoryOwnerId,
            report.OwnerCodeSnapshot);
        query = query.Where(balance =>
            balance.OwnerKind == ownerKind &&
            balance.InventoryOwnerId == report.InventoryOwnerId &&
            balance.OwnerCodeSnapshot == ownerCode);
    }

    private static void ApplyOwnerFilter(
        ref IQueryable<InventoryTransaction> query,
        InventoryOwnershipReportQuery report)
    {
        if (!report.OwnerKind.HasValue &&
            !report.InventoryOwnerId.HasValue &&
            string.IsNullOrWhiteSpace(report.OwnerCodeSnapshot))
        {
            return;
        }

        var ownerKind = report.OwnerKind ??
            (report.InventoryOwnerId.HasValue
                ? InventoryOwnerKind.ExternalOwner
                : InventoryOwnerKind.CompanyOwned);
        var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            report.InventoryOwnerId,
            report.OwnerCodeSnapshot);
        query = query.Where(transaction =>
            transaction.OwnerKind == ownerKind &&
            transaction.InventoryOwnerId == report.InventoryOwnerId &&
            transaction.OwnerCodeSnapshot == ownerCode);
    }

    private static ResultError? Validate(InventoryOwnershipReportQuery query)
    {
        if (query.Page < 1 || query.PageSize < 1)
        {
            return WmsErrors.Validation(
                "inventory.ownership_report_paging_invalid",
                "Page and page size must be positive.");
        }

        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.ToUtc <= query.FromUtc)
        {
            return WmsErrors.Validation(
                "inventory.ownership_report_range_invalid",
                "The report end timestamp must be after the start timestamp.");
        }

        if (query.InventoryOwnerId is <= 0)
        {
            return WmsErrors.Validation(
                "inventory.ownership_report_owner_invalid",
                "Inventory owner id must be positive when supplied.");
        }

        return null;
    }

    private sealed record BalanceProjection(
        int WarehouseId,
        string WarehouseCode,
        int LocationId,
        string LocationCode,
        int ItemId,
        string ItemSku,
        string ItemName,
        InventoryOwnerKind OwnerKind,
        int? InventoryOwnerId,
        string OwnerCodeSnapshot,
        string OwnerDisplayName,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal AvailableQuantity,
        DateTime BalanceCreatedAtUtc);
}
