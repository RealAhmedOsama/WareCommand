using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

/// <summary>
/// Server-side inventory read model over the canonical balance ledger.
/// No inventory entity collection is materialized before paging or aggregation.
/// </summary>
public sealed class InventoryInquiryService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    ILogger<InventoryInquiryService> logger) : IInventoryInquiryService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;

    public async Task<Result<InventoryInquiryPageDto>> QueryAsync(
        InventoryInquiryQuery query,
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
                return authorization.ToFailure<InventoryInquiryPageDto>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var balances = BuildQuery(query, scope);
            var totalCount = await balances.CountAsync(cancellationToken);
            var pageSize = NormalizePageSize(query.PageSize);
            if (totalCount == 0)
            {
                return await QueryLegacyStockAsync(query, scope, pageSize, cancellationToken);
            }

            var totalPages = CalculateTotalPages(totalCount, pageSize);
            var page = totalPages == 0
                ? 1
                : Math.Min(NormalizePage(query.Page), totalPages);

            var orderedBalances = ApplyOrdering(balances, query);
            var items = await orderedBalances
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ProjectToStockDto())
                .ToListAsync(cancellationToken);

            return Result.Success<InventoryInquiryPageDto>(new InventoryInquiryPageDto(
                items,
                page,
                pageSize,
                totalCount,
                totalPages));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying inventory balances");
            return Result.Failure<InventoryInquiryPageDto>(WmsErrors.FromException(
                ex,
                "inventory.inquiry_failed",
                "Inventory could not be queried. Please try again."));
        }
    }

    public async Task<Result<IReadOnlyList<StockSummaryDto>>> SummarizeAsync(
        InventoryInquiryQuery query,
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
                return authorization.ToFailure<IReadOnlyList<StockSummaryDto>>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var balances = BuildQuery(query, scope);
            if (!await balances.AnyAsync(cancellationToken))
            {
                return await SummarizeLegacyStockAsync(query, scope, cancellationToken);
            }

            var summaryRows = await balances
                .GroupBy(balance => new
                {
                    balance.ItemId,
                    balance.Item.Sku,
                    balance.Item.Name,
                    balance.InventoryStatusId,
                    StatusCode = balance.InventoryStatus.Code,
                    StatusName = balance.InventoryStatus.Name,
                    balance.InventoryStatus.IsAllocatable,
                    balance.OwnerKind,
                    balance.InventoryOwnerId,
                    balance.OwnerCodeSnapshot
                })
                .Select(group => new
                {
                    group.Key.Sku,
                    group.Key.Name,
                    TotalQuantity = group.Sum(balance => balance.OnHandQuantity),
                    TotalReserved = group.Sum(balance => balance.ReservedQuantity),
                    TotalAvailable = group.Sum(balance =>
                        balance.OnHandQuantity - balance.ReservedQuantity),
                    LocationCount = group.Select(balance => balance.LocationId).Distinct().Count(),
                    group.Key.StatusCode,
                    group.Key.StatusName,
                    group.Key.IsAllocatable,
                    group.Key.OwnerKind,
                    group.Key.InventoryOwnerId,
                    group.Key.OwnerCodeSnapshot
                })
                .OrderBy(summary => summary.Sku)
                .ThenBy(summary => summary.StatusCode)
                .ToListAsync(cancellationToken);

            var summaries = summaryRows
                .Select(summary => new StockSummaryDto(
                    summary.Sku,
                    summary.Name,
                    summary.TotalQuantity,
                    summary.TotalReserved,
                    summary.TotalAvailable,
                    summary.LocationCount,
                    summary.StatusCode,
                    summary.StatusName,
                    summary.IsAllocatable,
                    summary.OwnerKind,
                    summary.InventoryOwnerId,
                    summary.OwnerCodeSnapshot))
                .ToList();

            return Result.Success<IReadOnlyList<StockSummaryDto>>(summaries);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error summarizing inventory balances");
            return Result.Failure<IReadOnlyList<StockSummaryDto>>(WmsErrors.FromException(
                ex,
                "inventory.summary_failed",
                "Inventory summary could not be loaded. Please try again."));
        }
    }

    public async Task<Result<InventoryDashboardMetricsDto>> GetDashboardMetricsAsync(
        InventoryInquiryQuery query,
        DateOnly asOfBusinessDate,
        int expiryWarningDays,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(expiryWarningDays);

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<InventoryDashboardMetricsDto>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var balances = BuildQuery(query, scope);
            var expiryFrom = asOfBusinessDate.ToDateTime(TimeOnly.MinValue);
            var expiryToExclusive = asOfBusinessDate
                .AddDays(expiryWarningDays + 1)
                .ToDateTime(TimeOnly.MinValue);
            var aggregate = await AggregateBalancesAsync(
                balances,
                expiryFrom,
                expiryToExclusive,
                cancellationToken);

            if (aggregate is not null)
            {
                return Result.Success(ToDashboardMetrics(aggregate));
            }

            var legacyAggregate = await AggregateLegacyStockAsync(
                BuildLegacyQuery(query, scope),
                expiryFrom,
                expiryToExclusive,
                cancellationToken);

            return Result.Success(ToDashboardMetrics(legacyAggregate ?? DashboardAggregate.Empty));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading operational dashboard inventory metrics");
            return Result.Failure<InventoryDashboardMetricsDto>(WmsErrors.FromException(
                ex,
                "inventory.dashboard_metrics_failed",
                "Operational inventory metrics could not be loaded. Please try again."));
        }
    }

    private IQueryable<InventoryBalance> BuildQuery(
        InventoryInquiryQuery query,
        WarehouseAccessScope scope)
    {
        var balances = context.InventoryBalances.AsNoTracking();
        var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            query.OwnerKind,
            query.InventoryOwnerId,
            query.OwnerCodeSnapshot);

        if (!scope.HasGlobalAccess)
        {
            balances = balances.Where(balance => scope.WarehouseIds.Contains(balance.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            balances = balances.Where(balance => balance.WarehouseId == query.WarehouseId.Value);
        }

        if (query.LocationId.HasValue)
        {
            balances = balances.Where(balance => balance.LocationId == query.LocationId.Value);
        }

        if (query.ItemId.HasValue)
        {
            balances = balances.Where(balance => balance.ItemId == query.ItemId.Value);
        }

        if (query.LotId.HasValue)
        {
            balances = balances.Where(balance => balance.LotId == query.LotId.Value);
        }

        if (query.SerialNumberId.HasValue)
        {
            balances = balances.Where(balance => balance.SerialNumberId == query.SerialNumberId.Value);
        }

        if (query.LicensePlateId.HasValue)
        {
            balances = balances.Where(balance => balance.LicensePlateId == query.LicensePlateId.Value);
        }

        if (query.InventoryStatusId.HasValue)
        {
            balances = balances.Where(balance => balance.InventoryStatusId == query.InventoryStatusId.Value);
        }

        balances = balances.Where(balance =>
            balance.OwnerKind == query.OwnerKind &&
            balance.InventoryOwnerId == query.InventoryOwnerId &&
            balance.OwnerCodeSnapshot == ownerCode);

        if (!query.IncludeZero)
        {
            balances = balances.Where(balance =>
                balance.OnHandQuantity != 0m || balance.ReservedQuantity != 0m);
        }

        if (query.AvailableOnly)
        {
            balances = balances.Where(balance =>
                balance.OnHandQuantity - balance.ReservedQuantity > 0m);
        }

        if (query.ExpiryFrom.HasValue)
        {
            var expiryFrom = query.ExpiryFrom.Value.Date;
            balances = balances.Where(balance =>
                balance.Lot != null &&
                balance.Lot.ExpiryDate.HasValue &&
                balance.Lot.ExpiryDate.Value >= expiryFrom);
        }

        if (query.ExpiryTo.HasValue)
        {
            var expiryTo = query.ExpiryTo.Value.Date;
            balances = balances.Where(balance =>
                balance.Lot != null &&
                balance.Lot.ExpiryDate.HasValue &&
                balance.Lot.ExpiryDate.Value <= expiryTo);
        }

        var scanValue = Normalize(query.ScanValue);
        if (scanValue is not null)
        {
            balances = balances.Where(balance =>
                balance.Item.Sku == scanValue ||
                balance.Location.Code == scanValue ||
                balance.Location.Barcode == scanValue ||
                (balance.Lot != null && balance.Lot.Number == scanValue) ||
                balance.SerialNumber == scanValue ||
                (balance.Serial != null && balance.Serial.Number == scanValue) ||
                (balance.LicensePlate != null && balance.LicensePlate.Number == scanValue) ||
                balance.InventoryStatus.Code == scanValue ||
                balance.Item.Barcodes.Any(barcode => barcode.Value == scanValue));
        }

        var searchTerm = Normalize(query.SearchTerm);
        if (searchTerm is not null)
        {
            balances = ApplySearch(balances, searchTerm);
        }

        return balances;
    }

    private IQueryable<Stock> BuildLegacyQuery(
        InventoryInquiryQuery query,
        WarehouseAccessScope scope)
    {
        var stock = context.Stock.AsNoTracking();
        var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            query.OwnerKind,
            query.InventoryOwnerId,
            query.OwnerCodeSnapshot);

        if (!scope.HasGlobalAccess)
        {
            stock = stock.Where(row => scope.WarehouseIds.Contains(row.Location.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            stock = stock.Where(row => row.Location.WarehouseId == query.WarehouseId.Value);
        }

        if (query.LocationId.HasValue)
        {
            stock = stock.Where(row => row.LocationId == query.LocationId.Value);
        }

        if (query.ItemId.HasValue)
        {
            stock = stock.Where(row => row.ItemId == query.ItemId.Value);
        }

        if (query.LotId.HasValue)
        {
            stock = stock.Where(row => row.LotId == query.LotId.Value);
        }

        if (query.SerialNumberId.HasValue)
        {
            stock = stock.Where(row => row.SerialNumberId == query.SerialNumberId.Value);
        }

        if (query.LicensePlateId.HasValue)
        {
            stock = stock.Where(row => row.LicensePlateId == query.LicensePlateId.Value);
        }

        if (query.InventoryStatusId.HasValue)
        {
            stock = stock.Where(row => row.InventoryStatusId == query.InventoryStatusId.Value);
        }

        stock = stock.Where(row =>
            row.OwnerKind == query.OwnerKind &&
            row.InventoryOwnerId == query.InventoryOwnerId &&
            row.OwnerCodeSnapshot == ownerCode);

        if (!query.IncludeZero)
        {
            stock = stock.Where(row =>
                (decimal)row.QuantityAvailable != 0m ||
                (decimal)row.QuantityReserved != 0m);
        }

        if (query.AvailableOnly)
        {
            stock = stock.Where(row =>
                (decimal)row.QuantityAvailable - (decimal)row.QuantityReserved > 0m);
        }

        if (query.ExpiryFrom.HasValue)
        {
            var expiryFrom = query.ExpiryFrom.Value.Date;
            stock = stock.Where(row =>
                row.Lot != null &&
                row.Lot.ExpiryDate.HasValue &&
                row.Lot.ExpiryDate.Value >= expiryFrom);
        }

        if (query.ExpiryTo.HasValue)
        {
            var expiryTo = query.ExpiryTo.Value.Date;
            stock = stock.Where(row =>
                row.Lot != null &&
                row.Lot.ExpiryDate.HasValue &&
                row.Lot.ExpiryDate.Value <= expiryTo);
        }

        var scanValue = Normalize(query.ScanValue);
        if (scanValue is not null)
        {
            stock = stock.Where(row =>
                row.Item.Sku == scanValue ||
                row.Location.Code == scanValue ||
                row.Location.Barcode == scanValue ||
                (row.Lot != null && row.Lot.Number == scanValue) ||
                row.SerialNumber == scanValue ||
                (row.Serial != null && row.Serial.Number == scanValue) ||
                (row.LicensePlate != null && row.LicensePlate.Number == scanValue) ||
                (row.InventoryStatus != null && row.InventoryStatus.Code == scanValue) ||
                row.Item.Barcodes.Any(barcode => barcode.Value == scanValue));
        }

        var searchTerm = Normalize(query.SearchTerm);
        if (searchTerm is not null)
        {
            stock = ApplyLegacySearch(stock, searchTerm);
        }

        return stock;
    }

    private static Task<DashboardAggregate?> AggregateBalancesAsync(
        IQueryable<InventoryBalance> balances,
        DateTime expiryFrom,
        DateTime expiryToExclusive,
        CancellationToken cancellationToken) =>
        balances
            .GroupBy(_ => 1)
            .Select(group => new DashboardAggregate(
                group.Select(balance => balance.ItemId).Distinct().Count(),
                group.Sum(balance => balance.OnHandQuantity),
                group.Sum(balance => balance.ReservedQuantity),
                group.Sum(balance => balance.OnHandQuantity - balance.ReservedQuantity),
                group.Where(balance => balance.InventoryStatus.Code == InventoryStatusCodes.Hold)
                    .Sum(balance => (decimal?)balance.OnHandQuantity) ?? 0m,
                group.Where(balance => balance.InventoryStatus.Code == InventoryStatusCodes.Damaged)
                    .Sum(balance => (decimal?)balance.OnHandQuantity) ?? 0m,
                group.Where(balance => balance.InventoryStatus.Code == InventoryStatusCodes.Expired)
                    .Sum(balance => (decimal?)balance.OnHandQuantity) ?? 0m,
                group.Where(balance =>
                        balance.InventoryStatus.Code != InventoryStatusCodes.Expired &&
                        balance.Lot != null &&
                        balance.Lot.ExpiryDate.HasValue &&
                        balance.Lot.ExpiryDate.Value >= expiryFrom &&
                        balance.Lot.ExpiryDate.Value < expiryToExclusive)
                    .Sum(balance => (decimal?)balance.OnHandQuantity) ?? 0m,
                group.Select(balance => balance.LocationId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken);

    private static Task<DashboardAggregate?> AggregateLegacyStockAsync(
        IQueryable<Stock> stock,
        DateTime expiryFrom,
        DateTime expiryToExclusive,
        CancellationToken cancellationToken) =>
        stock
            .GroupBy(_ => 1)
            .Select(group => new DashboardAggregate(
                group.Select(row => row.ItemId).Distinct().Count(),
                group.Sum(row => (decimal)row.QuantityAvailable),
                group.Sum(row => (decimal)row.QuantityReserved),
                group.Sum(row =>
                    (decimal)row.QuantityAvailable - (decimal)row.QuantityReserved),
                group.Where(row =>
                        row.InventoryStatus != null &&
                        row.InventoryStatus.Code == InventoryStatusCodes.Hold)
                    .Sum(row => (decimal?)row.QuantityAvailable) ?? 0m,
                group.Where(row =>
                        row.InventoryStatus != null &&
                        row.InventoryStatus.Code == InventoryStatusCodes.Damaged)
                    .Sum(row => (decimal?)row.QuantityAvailable) ?? 0m,
                group.Where(row =>
                        row.InventoryStatus != null &&
                        row.InventoryStatus.Code == InventoryStatusCodes.Expired)
                    .Sum(row => (decimal?)row.QuantityAvailable) ?? 0m,
                group.Where(row =>
                        (row.InventoryStatus == null ||
                         row.InventoryStatus.Code != InventoryStatusCodes.Expired) &&
                        row.Lot != null &&
                        row.Lot.ExpiryDate.HasValue &&
                        row.Lot.ExpiryDate.Value >= expiryFrom &&
                        row.Lot.ExpiryDate.Value < expiryToExclusive)
                    .Sum(row => (decimal?)row.QuantityAvailable) ?? 0m,
                group.Select(row => row.LocationId).Distinct().Count()))
            .SingleOrDefaultAsync(cancellationToken);

    private static InventoryDashboardMetricsDto ToDashboardMetrics(
        DashboardAggregate aggregate) =>
        new(
            aggregate.StockKeepingUnits,
            aggregate.OnHandQuantity,
            aggregate.ReservedQuantity,
            aggregate.AvailableQuantity,
            aggregate.HeldQuantity,
            aggregate.DamagedQuantity,
            aggregate.ExpiredQuantity,
            aggregate.ExpiringQuantity,
            aggregate.StockLocations);

    private async Task<Result<InventoryInquiryPageDto>> QueryLegacyStockAsync(
        InventoryInquiryQuery query,
        WarehouseAccessScope scope,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var stock = BuildLegacyQuery(query, scope);
        var totalCount = await stock.CountAsync(cancellationToken);
        var totalPages = CalculateTotalPages(totalCount, pageSize);
        var page = totalPages == 0
            ? 1
            : Math.Min(NormalizePage(query.Page), totalPages);
        var orderedStock = ApplyLegacyOrdering(stock, query);
        var items = await orderedStock
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ProjectLegacyStockDto())
            .ToListAsync(cancellationToken);

        return Result.Success<InventoryInquiryPageDto>(new InventoryInquiryPageDto(
            items,
            page,
            pageSize,
            totalCount,
            totalPages));
    }

    private async Task<Result<IReadOnlyList<StockSummaryDto>>> SummarizeLegacyStockAsync(
        InventoryInquiryQuery query,
        WarehouseAccessScope scope,
        CancellationToken cancellationToken)
    {
        var stock = BuildLegacyQuery(query, scope);
        var summaryRows = await stock
            .GroupBy(row => new
            {
                row.ItemId,
                row.Item.Sku,
                row.Item.Name,
                row.InventoryStatusId,
                StatusCode = row.InventoryStatus == null
                    ? InventoryStatusCodes.Available
                    : row.InventoryStatus.Code,
                StatusName = row.InventoryStatus == null
                    ? "Available"
                    : row.InventoryStatus.Name,
                IsAllocatable = row.InventoryStatus == null || row.InventoryStatus.IsAllocatable,
                row.OwnerKind,
                row.InventoryOwnerId,
                row.OwnerCodeSnapshot
            })
            .Select(group => new
            {
                group.Key.Sku,
                group.Key.Name,
                TotalQuantity = group.Sum(row => (decimal)row.QuantityAvailable),
                TotalReserved = group.Sum(row => (decimal)row.QuantityReserved),
                TotalAvailable = group.Sum(row =>
                    (decimal)row.QuantityAvailable - (decimal)row.QuantityReserved),
                LocationCount = group.Select(row => row.LocationId).Distinct().Count(),
                group.Key.StatusCode,
                group.Key.StatusName,
                group.Key.IsAllocatable,
                group.Key.OwnerKind,
                group.Key.InventoryOwnerId,
                group.Key.OwnerCodeSnapshot
            })
            .OrderBy(summary => summary.Sku)
            .ThenBy(summary => summary.StatusCode)
            .ToListAsync(cancellationToken);

        var summaries = summaryRows
            .Select(summary => new StockSummaryDto(
                summary.Sku,
                summary.Name,
                summary.TotalQuantity,
                summary.TotalReserved,
                summary.TotalAvailable,
                summary.LocationCount,
                summary.StatusCode,
                summary.StatusName,
                summary.IsAllocatable,
                summary.OwnerKind,
                summary.InventoryOwnerId,
                summary.OwnerCodeSnapshot))
            .ToList();

        return Result.Success<IReadOnlyList<StockSummaryDto>>(summaries);
    }

    private IQueryable<Stock> ApplyLegacySearch(
        IQueryable<Stock> stock,
        string searchTerm)
    {
        var pattern = $"%{searchTerm}%";
        var usePostgreSql = context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) == true;

        if (usePostgreSql)
        {
            return stock.Where(row =>
                EF.Functions.ILike(row.Item.Sku, pattern) ||
                EF.Functions.ILike(row.Item.Name, pattern) ||
                EF.Functions.ILike(row.Location.Code, pattern) ||
                EF.Functions.ILike(row.Location.Name, pattern) ||
                (row.Lot != null && EF.Functions.ILike(row.Lot.Number, pattern)) ||
                (row.SerialNumber != null && EF.Functions.ILike(row.SerialNumber, pattern)) ||
                (row.Serial != null && EF.Functions.ILike(row.Serial.Number, pattern)) ||
                (row.LicensePlate != null && EF.Functions.ILike(row.LicensePlate.Number, pattern)) ||
                (row.InventoryStatus != null && EF.Functions.ILike(row.InventoryStatus.Code, pattern)) ||
                (row.InventoryStatus != null && EF.Functions.ILike(row.InventoryStatus.Name, pattern)) ||
                row.Item.Barcodes.Any(barcode => EF.Functions.ILike(barcode.Value, pattern)));
        }

#pragma warning disable CA1304, CA1311 // SQL providers translate ToUpper(), not the invariant overload.
        return stock.Where(row =>
            EF.Functions.Like(row.Item.Sku.ToUpper(), pattern) ||
            EF.Functions.Like(row.Item.Name.ToUpper(), pattern) ||
            EF.Functions.Like(row.Location.Code.ToUpper(), pattern) ||
            EF.Functions.Like(row.Location.Name.ToUpper(), pattern) ||
            (row.Lot != null && EF.Functions.Like(row.Lot.Number.ToUpper(), pattern)) ||
            (row.SerialNumber != null && EF.Functions.Like(row.SerialNumber.ToUpper(), pattern)) ||
            (row.Serial != null && EF.Functions.Like(row.Serial.Number.ToUpper(), pattern)) ||
            (row.LicensePlate != null && EF.Functions.Like(row.LicensePlate.Number.ToUpper(), pattern)) ||
            (row.InventoryStatus != null && EF.Functions.Like(row.InventoryStatus.Code.ToUpper(), pattern)) ||
            (row.InventoryStatus != null && EF.Functions.Like(row.InventoryStatus.Name.ToUpper(), pattern)) ||
            row.Item.Barcodes.Any(barcode =>
                EF.Functions.Like(barcode.Value.ToUpper(), pattern)));
#pragma warning restore CA1304, CA1311
    }

    private static IQueryable<Stock> ApplyLegacyOrdering(
        IQueryable<Stock> stock,
        InventoryInquiryQuery query)
    {
        return query.Sort switch
        {
            InventoryInquirySort.Location => query.Descending
                ? stock.OrderByDescending(row => row.Location.Code).ThenByDescending(row => row.Id)
                : stock.OrderBy(row => row.Location.Code).ThenBy(row => row.Id),
            InventoryInquirySort.Quantity => query.Descending
                ? stock.OrderByDescending(row => (decimal)row.QuantityAvailable)
                    .ThenByDescending(row => row.Id)
                : stock.OrderBy(row => (decimal)row.QuantityAvailable)
                    .ThenBy(row => row.Id),
            InventoryInquirySort.Expiry => query.Descending
                ? stock.OrderByDescending(row => row.Lot == null ? null : row.Lot.ExpiryDate)
                    .ThenByDescending(row => row.Id)
                : stock.OrderBy(row => row.Lot == null ? null : row.Lot.ExpiryDate)
                    .ThenBy(row => row.Id),
            InventoryInquirySort.Updated => query.Descending
                ? stock.OrderByDescending(row => row.UpdatedAt ?? row.CreatedAt)
                    .ThenByDescending(row => row.Id)
                : stock.OrderBy(row => row.UpdatedAt ?? row.CreatedAt)
                    .ThenBy(row => row.Id),
            _ => query.Descending
                ? stock.OrderByDescending(row => row.Item.Sku).ThenByDescending(row => row.Id)
                : stock.OrderBy(row => row.Item.Sku).ThenBy(row => row.Id)
        };
    }

    private IQueryable<InventoryBalance> ApplySearch(
        IQueryable<InventoryBalance> balances,
        string searchTerm)
    {
        var pattern = $"%{searchTerm}%";
        var usePostgreSql = context.Database.ProviderName?.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase) == true;

        if (usePostgreSql)
        {
            return balances.Where(balance =>
                EF.Functions.ILike(balance.Item.Sku, pattern) ||
                EF.Functions.ILike(balance.Item.Name, pattern) ||
                EF.Functions.ILike(balance.Location.Code, pattern) ||
                EF.Functions.ILike(balance.Location.Name, pattern) ||
                (balance.Lot != null && EF.Functions.ILike(balance.Lot.Number, pattern)) ||
                (balance.SerialNumber != null && EF.Functions.ILike(balance.SerialNumber, pattern)) ||
                (balance.Serial != null && EF.Functions.ILike(balance.Serial.Number, pattern)) ||
                (balance.LicensePlate != null && EF.Functions.ILike(balance.LicensePlate.Number, pattern)) ||
                EF.Functions.ILike(balance.InventoryStatus.Code, pattern) ||
                EF.Functions.ILike(balance.InventoryStatus.Name, pattern) ||
                balance.Item.Barcodes.Any(barcode => EF.Functions.ILike(barcode.Value, pattern)));
        }

#pragma warning disable CA1304, CA1311 // SQL providers translate ToUpper(), not the invariant overload.
        return balances.Where(balance =>
            EF.Functions.Like(balance.Item.Sku.ToUpper(), pattern) ||
            EF.Functions.Like(balance.Item.Name.ToUpper(), pattern) ||
            EF.Functions.Like(balance.Location.Code.ToUpper(), pattern) ||
            EF.Functions.Like(balance.Location.Name.ToUpper(), pattern) ||
            (balance.Lot != null && EF.Functions.Like(balance.Lot.Number.ToUpper(), pattern)) ||
            (balance.SerialNumber != null && EF.Functions.Like(balance.SerialNumber.ToUpper(), pattern)) ||
            (balance.Serial != null && EF.Functions.Like(balance.Serial.Number.ToUpper(), pattern)) ||
            (balance.LicensePlate != null && EF.Functions.Like(balance.LicensePlate.Number.ToUpper(), pattern)) ||
            EF.Functions.Like(balance.InventoryStatus.Code.ToUpper(), pattern) ||
            EF.Functions.Like(balance.InventoryStatus.Name.ToUpper(), pattern) ||
            balance.Item.Barcodes.Any(barcode =>
                EF.Functions.Like(barcode.Value.ToUpper(), pattern)));
#pragma warning restore CA1304, CA1311
    }

    private static IQueryable<InventoryBalance> ApplyOrdering(
        IQueryable<InventoryBalance> balances,
        InventoryInquiryQuery query)
    {
        return query.Sort switch
        {
            InventoryInquirySort.Location => query.Descending
                ? balances.OrderByDescending(balance => balance.Location.Code).ThenByDescending(balance => balance.Id)
                : balances.OrderBy(balance => balance.Location.Code).ThenBy(balance => balance.Id),
            InventoryInquirySort.Quantity => query.Descending
                ? balances.OrderByDescending(balance => balance.OnHandQuantity)
                    .ThenByDescending(balance => balance.Id)
                : balances.OrderBy(balance => balance.OnHandQuantity).ThenBy(balance => balance.Id),
            InventoryInquirySort.Expiry => query.Descending
                ? balances.OrderByDescending(balance => balance.Lot == null
                        ? null
                        : balance.Lot.ExpiryDate)
                    .ThenByDescending(balance => balance.Id)
                : balances.OrderBy(balance => balance.Lot == null
                        ? null
                        : balance.Lot.ExpiryDate)
                    .ThenBy(balance => balance.Id),
            InventoryInquirySort.Updated => query.Descending
                ? balances.OrderByDescending(balance => balance.UpdatedAt ?? balance.CreatedAt)
                    .ThenByDescending(balance => balance.Id)
                : balances.OrderBy(balance => balance.UpdatedAt ?? balance.CreatedAt)
                    .ThenBy(balance => balance.Id),
            _ => query.Descending
                ? balances.OrderByDescending(balance => balance.Item.Sku).ThenByDescending(balance => balance.Id)
                : balances.OrderBy(balance => balance.Item.Sku).ThenBy(balance => balance.Id)
        };
    }

    private static Expression<Func<InventoryBalance, StockDto>> ProjectToStockDto() => balance =>
        new StockDto(
            balance.Id,
            balance.ItemId,
            balance.Item.Sku,
            balance.Item.Name,
            balance.LocationId,
            balance.Location.Code,
            balance.Location.Name,
            balance.LotId,
            balance.Lot == null ? null : balance.Lot.Number,
            balance.SerialNumber ?? (balance.Serial == null ? null : balance.Serial.Number),
            balance.OnHandQuantity,
            balance.ReservedQuantity,
            balance.OnHandQuantity - balance.ReservedQuantity,
            balance.CreatedAt,
            balance.UpdatedAt,
            balance.SerialNumberId,
            balance.InventoryStatusId,
            balance.InventoryStatus.Code,
            balance.InventoryStatus.Name,
            balance.InventoryStatus.IsAllocatable,
            balance.InventoryStatus.IsPickable,
            balance.InventoryStatus.IsShippable,
            balance.LicensePlateId,
            balance.LicensePlate == null ? null : balance.LicensePlate.Number,
            balance.InventoryStatus.IsActive &&
            balance.InventoryStatus.IsAllocatable &&
            balance.Item.IsActive &&
            balance.Location.IsActive
                ? (balance.OnHandQuantity - balance.ReservedQuantity > 0m
                    ? balance.OnHandQuantity - balance.ReservedQuantity
                    : 0m)
                : 0m,
            balance.InventoryStatus.Code == InventoryStatusCodes.Hold
                ? balance.OnHandQuantity
                : 0m,
            0m,
            0m,
            balance.Lot == null ? null : balance.Lot.ExpiryDate,
            balance.OwnerKind,
            balance.InventoryOwnerId,
            balance.OwnerCodeSnapshot);

    private static Expression<Func<Stock, StockDto>> ProjectLegacyStockDto() => stock =>
        new StockDto(
            stock.Id,
            stock.ItemId,
            stock.Item.Sku,
            stock.Item.Name,
            stock.LocationId,
            stock.Location.Code,
            stock.Location.Name,
            stock.LotId,
            stock.Lot == null ? null : stock.Lot.Number,
            stock.SerialNumber ?? (stock.Serial == null ? null : stock.Serial.Number),
            (decimal)stock.QuantityAvailable,
            (decimal)stock.QuantityReserved,
            (decimal)stock.QuantityAvailable - (decimal)stock.QuantityReserved,
            stock.CreatedAt,
            stock.UpdatedAt,
            stock.SerialNumberId,
            stock.InventoryStatus == null
                ? stock.InventoryStatusId
                : stock.InventoryStatus.Id,
            stock.InventoryStatus == null
                ? InventoryStatusCodes.Available
                : stock.InventoryStatus.Code,
            stock.InventoryStatus == null
                ? "Available"
                : stock.InventoryStatus.Name,
            stock.InventoryStatus == null || stock.InventoryStatus.IsAllocatable,
            stock.InventoryStatus == null || stock.InventoryStatus.IsPickable,
            stock.InventoryStatus == null || stock.InventoryStatus.IsShippable,
            stock.LicensePlateId,
            stock.LicensePlate == null ? null : stock.LicensePlate.Number,
            stock.Item.IsActive &&
            stock.Location.IsActive &&
            (stock.InventoryStatus == null ||
             (stock.InventoryStatus.IsActive && stock.InventoryStatus.IsAllocatable))
                ? ((decimal)stock.QuantityAvailable - (decimal)stock.QuantityReserved > 0m
                    ? (decimal)stock.QuantityAvailable - (decimal)stock.QuantityReserved
                    : 0m)
                : 0m,
            stock.InventoryStatus != null && stock.InventoryStatus.Code == InventoryStatusCodes.Hold
                ? (decimal)stock.QuantityAvailable
                : 0m,
            0m,
            0m,
            stock.Lot == null ? null : stock.Lot.ExpiryDate,
            stock.OwnerKind,
            stock.InventoryOwnerId,
            stock.OwnerCodeSnapshot);

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.ToUpperInvariant();
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizePageSize(int pageSize) => pageSize switch
    {
        < 1 => DefaultPageSize,
        > MaximumPageSize => MaximumPageSize,
        _ => pageSize
    };

    private static int CalculateTotalPages(int totalCount, int pageSize) =>
        totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

    private sealed record DashboardAggregate(
        int StockKeepingUnits,
        decimal OnHandQuantity,
        decimal ReservedQuantity,
        decimal AvailableQuantity,
        decimal HeldQuantity,
        decimal DamagedQuantity,
        decimal ExpiredQuantity,
        decimal ExpiringQuantity,
        int StockLocations)
    {
        public static DashboardAggregate Empty { get; } = new(0, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0);
    }
}
