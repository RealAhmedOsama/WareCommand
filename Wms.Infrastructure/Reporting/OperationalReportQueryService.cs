using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Reporting;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Application.Units;
using Wms.Application.UseCases.Reports;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Reporting;

/// <summary>
/// Provider-neutral report read models. Filters, authorization, ordering,
/// paging, and aggregation stay in the database query; only the requested
/// page or bounded group page is materialized.
/// </summary>
public sealed class OperationalReportQueryService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IWmsSettingsService settingsService,
    IClock clock,
    ICurrentUser currentUser,
    ILogger<OperationalReportQueryService> logger,
    IItemQuantityConversionService? quantityConversionService = null) : IReportQueryService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;

    public async Task<Result<ReportPage<MovementReportDto>>> QueryMovementLedgerAsync(
        MovementLedgerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReportsRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReportPage<MovementReportDto>>();
            }

            var settings = await GetSettingsAsync(cancellationToken);
            var effective = Normalize(query, settings);
            if (effective.IsFailure)
            {
                return effective.ToFailure<ReportPage<MovementReportDto>>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var movements = BuildMovementQuery(effective.Value, scope);
            var totalCount = await movements.CountAsync(cancellationToken);
            var totalPages = CalculateTotalPages(totalCount, effective.Value.PageSize);
            var page = totalPages == 0
                ? 1
                : Math.Min(effective.Value.Page, totalPages);

            var rows = await ApplyOrdering(movements, effective.Value)
                .Skip((page - 1) * effective.Value.PageSize)
                .Take(effective.Value.PageSize)
                .Select(ProjectMovement())
                .ToListAsync(cancellationToken);

            var items = await ConvertRowsAsync(
                rows,
                effective.Value.DisplayUnitOfMeasure,
                cancellationToken);
            var metadata = CreateMetadata(effective.Value, settings);

            return Result.Success(new ReportPage<MovementReportDto>(
                items,
                page,
                effective.Value.PageSize,
                totalCount,
                totalPages,
                metadata));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReportConversionException exception)
        {
            return Result.Failure<ReportPage<MovementReportDto>>(exception.Error);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Movement ledger report query failed");
            return Result.Failure<ReportPage<MovementReportDto>>(WmsErrors.FromException(
                exception,
                "reports.movement_query_failed",
                "The movement report could not be loaded. Please try again."));
        }
    }

    public async Task<Result<ReportGroupPageDto>> GroupMovementLedgerAsync(
        MovementLedgerQuery query,
        MovementReportGroupBy groupBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (groupBy == MovementReportGroupBy.None)
        {
            return Result.Failure<ReportGroupPageDto>(WmsErrors.Validation(
                "reports.group_required",
                "Choose a grouping field before requesting a grouped report."));
        }

        try
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReportsRead,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ReportGroupPageDto>();
            }

            var settings = await GetSettingsAsync(cancellationToken);
            var effective = Normalize(query, settings);
            if (effective.IsFailure)
            {
                return effective.ToFailure<ReportGroupPageDto>();
            }

            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            var movements = BuildMovementQuery(effective.Value, scope);
            var grouped = await QueryGroupsAsync(
                movements,
                groupBy,
                effective.Value.Page,
                effective.Value.PageSize,
                cancellationToken);
            var totalPages = CalculateTotalPages(grouped.TotalGroups, effective.Value.PageSize);

            return Result.Success(new ReportGroupPageDto(
                grouped.Rows,
                grouped.Page,
                effective.Value.PageSize,
                grouped.TotalGroups,
                totalPages,
                CreateMetadata(effective.Value with { GroupBy = groupBy }, settings)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Grouped movement report query failed");
            return Result.Failure<ReportGroupPageDto>(WmsErrors.FromException(
                exception,
                "reports.movement_group_query_failed",
                "The grouped movement report could not be loaded. Please try again."));
        }
    }

    private async Task<WmsSettingsValues> GetSettingsAsync(
        CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
        if (settings.IsSuccess)
        {
            return settings.Value.Values;
        }

        logger.LogWarning(
            "Report settings could not be loaded; defaults will be used: {ErrorCode}",
            settings.ErrorCode);
        return WmsSettingsDefaults.Create();
    }

    private Result<EffectiveMovementLedgerQuery> Normalize(
        MovementLedgerQuery query,
        WmsSettingsValues settings)
    {
        var timeZoneId = string.IsNullOrWhiteSpace(query.BusinessTimeZoneId)
            ? settings.Localization.TimeZone
            : query.BusinessTimeZoneId.Trim();
        if (!WmsTimeZoneCatalog.TryNormalize(timeZoneId, out timeZoneId))
        {
            return Result.Failure<EffectiveMovementLedgerQuery>(WmsErrors.Validation(
                "reports.time_zone_invalid",
                "The requested business time zone is invalid."));
        }

        var today = WmsBusinessTime.GetBusinessDate(
            clock.UtcNow,
            timeZoneId);
        var fromDate = query.FromDate ?? today.AddDays(-settings.Reports.DefaultPeriodDays);
        var toDate = query.ToDate ?? today;
        if (toDate < fromDate)
        {
            return Result.Failure<EffectiveMovementLedgerQuery>(WmsErrors.Validation(
                "reports.invalid_date_range",
                "The report end date cannot be before the start date.",
                new Dictionary<string, string[]>
                {
                    ["FromDate"] = ["The start date must be on or before the end date."],
                    ["ToDate"] = ["The end date must be on or after the start date."]
                }));
        }

        var maximumRows = Math.Max(1, Math.Min(settings.Reports.MaximumRows, MaximumPageSize));
        var pageSize = query.PageSize switch
        {
            < 1 => Math.Min(DefaultPageSize, maximumRows),
            _ => Math.Min(query.PageSize, maximumRows)
        };

        return Result.Success(new EffectiveMovementLedgerQuery(
            fromDate,
            toDate,
            query.WarehouseId,
            NormalizeOptional(query.SearchTerm),
            NormalizeOptional(query.ItemSku),
            NormalizeOptional(query.LocationCode),
            query.MovementType,
            NormalizeOptional(query.UserId),
            NormalizeOptional(query.ReferenceNumber),
            NormalizeOptional(query.LotNumber),
            NormalizeOptional(query.SerialNumber),
            NormalizeOptional(query.LicensePlateNumber),
            query.Sort,
            query.Descending,
            Math.Max(1, query.Page),
            pageSize,
            NormalizeOptional(query.DisplayUnitOfMeasure),
            timeZoneId,
            MovementReportGroupBy.None));
    }

#pragma warning disable CA1304, CA1311
    private IQueryable<Movement> BuildMovementQuery(
        EffectiveMovementLedgerQuery query,
        WarehouseAccessScope scope)
    {
        var range = WmsBusinessTime.GetInclusiveDateRange(
            query.FromDate,
            query.ToDate,
            query.TimeZoneId);
        var movements = context.Movements
            .AsNoTracking()
            .Where(movement =>
                movement.Timestamp >= range.FromUtc &&
                movement.Timestamp < range.ToUtcExclusive);

        if (!scope.HasGlobalAccess)
        {
            movements = movements.Where(movement =>
                (movement.FromLocation != null &&
                 movement.ToLocation == null &&
                 scope.WarehouseIds.Contains(movement.FromLocation.WarehouseId)) ||
                (movement.FromLocation == null &&
                 movement.ToLocation != null &&
                 scope.WarehouseIds.Contains(movement.ToLocation.WarehouseId)) ||
                (movement.FromLocation != null &&
                 movement.ToLocation != null &&
                 scope.WarehouseIds.Contains(movement.FromLocation.WarehouseId) &&
                 scope.WarehouseIds.Contains(movement.ToLocation.WarehouseId)));
        }

        if (query.WarehouseId.HasValue)
        {
            movements = movements.Where(movement =>
                (movement.FromLocation != null &&
                 movement.FromLocation.WarehouseId == query.WarehouseId.Value) ||
                (movement.ToLocation != null &&
                 movement.ToLocation.WarehouseId == query.WarehouseId.Value));
        }

        if (query.MovementType.HasValue)
        {
            movements = movements.Where(movement => movement.Type == query.MovementType.Value);
        }

        if (query.UserId is not null)
        {
            movements = movements.Where(movement => movement.UserId == query.UserId);
        }

        if (query.ItemSku is not null)
        {
            movements = movements.Where(movement => movement.Item.Sku == query.ItemSku);
        }

        if (query.LocationCode is not null)
        {
            movements = movements.Where(movement =>
                (movement.FromLocation != null && movement.FromLocation.Code == query.LocationCode) ||
                (movement.ToLocation != null && movement.ToLocation.Code == query.LocationCode));
        }

        if (query.ReferenceNumber is not null)
        {
            movements = movements.Where(movement =>
                movement.ReferenceNumber == query.ReferenceNumber);
        }

        if (query.LotNumber is not null)
        {
            movements = movements.Where(movement =>
                movement.Lot != null && movement.Lot.Number == query.LotNumber);
        }

        if (query.SerialNumber is not null)
        {
            movements = movements.Where(movement =>
                movement.SerialNumber == query.SerialNumber ||
                (movement.Serial != null && movement.Serial.Number == query.SerialNumber));
        }

        if (query.LicensePlateNumber is not null)
        {
            movements = movements.Where(movement =>
                (movement.LicensePlate != null &&
                 movement.LicensePlate.Number == query.LicensePlateNumber) ||
                (movement.FromLicensePlate != null &&
                 movement.FromLicensePlate.Number == query.LicensePlateNumber) ||
                (movement.ToLicensePlate != null &&
                 movement.ToLicensePlate.Number == query.LicensePlateNumber));
        }

        if (query.SearchTerm is not null)
        {
            var pattern = $"%{query.SearchTerm.ToUpperInvariant()}%";
            movements = movements.Where(movement =>
                EF.Functions.Like(movement.Item.Sku.ToUpper(), pattern) ||
                EF.Functions.Like(movement.Item.Name.ToUpper(), pattern) ||
                (movement.FromLocation != null &&
                 EF.Functions.Like(movement.FromLocation.Code.ToUpper(), pattern)) ||
                (movement.ToLocation != null &&
                 EF.Functions.Like(movement.ToLocation.Code.ToUpper(), pattern)) ||
                (movement.ReferenceNumber != null &&
                 EF.Functions.Like(movement.ReferenceNumber.ToUpper(), pattern)) ||
                (movement.Lot != null &&
                 EF.Functions.Like(movement.Lot.Number.ToUpper(), pattern)) ||
                (movement.SerialNumber != null &&
                 EF.Functions.Like(movement.SerialNumber.ToUpper(), pattern)));
        }

        return movements;
    }
#pragma warning restore CA1304, CA1311

    private static IQueryable<Movement> ApplyOrdering(
        IQueryable<Movement> movements,
        EffectiveMovementLedgerQuery query) => query.Sort switch
        {
            MovementReportSort.ItemSku => query.Descending
                ? movements.OrderByDescending(movement => movement.Item.Sku)
                    .ThenByDescending(movement => movement.Id)
                : movements.OrderBy(movement => movement.Item.Sku)
                    .ThenBy(movement => movement.Id),
            MovementReportSort.Quantity => query.Descending
                ? movements.OrderByDescending(movement => movement.Quantity.Value)
                    .ThenByDescending(movement => movement.Id)
                : movements.OrderBy(movement => movement.Quantity.Value)
                    .ThenBy(movement => movement.Id),
            MovementReportSort.MovementType => query.Descending
                ? movements.OrderByDescending(movement => movement.Type)
                    .ThenByDescending(movement => movement.Id)
                : movements.OrderBy(movement => movement.Type)
                    .ThenBy(movement => movement.Id),
            MovementReportSort.User => query.Descending
                ? movements.OrderByDescending(movement => movement.UserId)
                    .ThenByDescending(movement => movement.Id)
                : movements.OrderBy(movement => movement.UserId)
                    .ThenBy(movement => movement.Id),
            _ => query.Descending
                ? movements.OrderByDescending(movement => movement.Timestamp)
                    .ThenByDescending(movement => movement.Id)
                : movements.OrderBy(movement => movement.Timestamp)
                    .ThenBy(movement => movement.Id)
        };

    private async Task<IReadOnlyList<MovementReportDto>> ConvertRowsAsync(
        IReadOnlyList<MovementReportProjection> rows,
        string? displayUnitOfMeasure,
        CancellationToken cancellationToken)
    {
        var items = rows.Select(MapToDto).ToList();
        if (displayUnitOfMeasure is null)
        {
            return items;
        }

        if (quantityConversionService is null)
        {
            throw new InvalidOperationException(
                "A quantity conversion service is required for display-unit report queries.");
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var conversion = await quantityConversionService.ConvertFromBaseAsync(
                rows[index].ItemId,
                rows[index].Quantity,
                displayUnitOfMeasure,
                QuantityRoundingMode.ToEven,
                cancellationToken);
            if (conversion.IsFailure)
            {
                throw new ReportConversionException(conversion.FirstError!);
            }

            items[index] = items[index] with
            {
                DisplayQuantity = conversion.Value,
                DisplayUnitOfMeasure = displayUnitOfMeasure
            };
        }

        return items;
    }

    private static async Task<(
        IReadOnlyList<ReportGroupRowDto> Rows,
        int TotalGroups,
        int Page)> QueryGroupsAsync(
        IQueryable<Movement> movements,
        MovementReportGroupBy groupBy,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return groupBy switch
        {
            MovementReportGroupBy.Item => await QueryGroupsAsync(
                movements,
                movement => movement.Item.Sku,
                key => key,
                page,
                pageSize,
                cancellationToken),
            MovementReportGroupBy.Location => await QueryGroupsAsync(
                movements,
                movement => movement.ToLocation != null
                    ? movement.ToLocation.Code
                    : movement.FromLocation != null
                        ? movement.FromLocation.Code
                        : "(none)",
                key => key,
                page,
                pageSize,
                cancellationToken),
            MovementReportGroupBy.MovementType => await QueryGroupsAsync(
                movements,
                movement => movement.Type,
                key => key.ToString(),
                page,
                pageSize,
                cancellationToken),
            MovementReportGroupBy.User => await QueryGroupsAsync(
                movements,
                movement => movement.UserId,
                key => key,
                page,
                pageSize,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, null)
        };
    }

    private static async Task<(
        IReadOnlyList<ReportGroupRowDto> Rows,
        int TotalGroups,
        int Page)> QueryGroupsAsync<TKey>(
        IQueryable<Movement> movements,
        System.Linq.Expressions.Expression<Func<Movement, TKey>> keySelector,
        Func<TKey, string> displayKey,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var grouped = movements.GroupBy(keySelector);
        var totalGroups = await grouped.CountAsync(cancellationToken);
        var effectivePage = totalGroups == 0
            ? 1
            : Math.Min(Math.Max(1, page), CalculateTotalPages(totalGroups, pageSize));
        var keyRows = await grouped
            .OrderBy(group => group.Key)
            .Skip((effectivePage - 1) * pageSize)
            .Take(pageSize)
            .Select(group => new GroupKeyCount<TKey>(
                group.Key,
                group.Count()))
            .ToListAsync(cancellationToken);
        var aggregateRows = new List<GroupAggregate<TKey>>(keyRows.Count);
        foreach (var keyRow in keyRows)
        {
            var totalBaseQuantity = await movements
                .Where(BuildKeyEquals(keySelector, keyRow.Key))
                .SumAsync(movement => movement.Quantity, cancellationToken);
            aggregateRows.Add(new GroupAggregate<TKey>(
                keyRow.Key,
                keyRow.RowCount,
                totalBaseQuantity));
        }

        return (
            aggregateRows
                .Select(row => new ReportGroupRowDto(
                    displayKey(row.Key),
                    row.RowCount,
                    row.TotalBaseQuantity))
                .ToArray(),
            totalGroups,
            effectivePage);
    }

    private static System.Linq.Expressions.Expression<Func<Movement, bool>> BuildKeyEquals<TKey>(
        System.Linq.Expressions.Expression<Func<Movement, TKey>> keySelector,
        TKey key)
    {
        var body = System.Linq.Expressions.Expression.Equal(
            keySelector.Body,
            System.Linq.Expressions.Expression.Constant(key, typeof(TKey)));
        return System.Linq.Expressions.Expression.Lambda<Func<Movement, bool>>(
            body,
            keySelector.Parameters);
    }

    private sealed record GroupKeyCount<TKey>(TKey Key, int RowCount);

    private ReportMetadata CreateMetadata(
        EffectiveMovementLedgerQuery query,
        WmsSettingsValues settings) =>
        new(
            "movement-ledger",
            clock.UtcNow,
            clock.UtcNow,
            query.TimeZoneId,
            currentUser.UserId,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fromDate"] = query.FromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["toDate"] = query.ToDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["warehouseId"] = query.WarehouseId?.ToString(CultureInfo.InvariantCulture),
                ["searchTerm"] = query.SearchTerm,
                ["itemSku"] = query.ItemSku,
                ["locationCode"] = query.LocationCode,
                ["movementType"] = query.MovementType?.ToString(),
                ["userId"] = query.UserId,
                ["referenceNumber"] = query.ReferenceNumber,
                ["lotNumber"] = query.LotNumber,
                ["serialNumber"] = query.SerialNumber,
                ["licensePlateNumber"] = query.LicensePlateNumber,
                ["groupBy"] = query.GroupBy.ToString(),
                ["sort"] = query.Sort.ToString(),
                ["descending"] = query.Descending.ToString(CultureInfo.InvariantCulture),
                ["page"] = query.Page.ToString(CultureInfo.InvariantCulture),
                ["pageSize"] = query.PageSize.ToString(CultureInfo.InvariantCulture),
                ["displayUnitOfMeasure"] = query.DisplayUnitOfMeasure
            });

    private static System.Linq.Expressions.Expression<Func<Movement, MovementReportProjection>> ProjectMovement() =>
        movement => new MovementReportProjection(
            movement.Id,
            movement.ItemId,
            movement.Type,
            movement.Item.Sku,
            movement.Item.Name,
            movement.FromLocation == null ? null : movement.FromLocation.Code,
            movement.ToLocation == null ? null : movement.ToLocation.Code,
            movement.Quantity.Value,
            movement.Lot == null ? null : movement.Lot.Number,
            movement.SerialNumber ?? (movement.Serial == null ? null : movement.Serial.Number),
            movement.UserId,
            movement.ReferenceNumber,
            movement.Notes,
            movement.Timestamp,
            movement.BaseUnitOfMeasure,
            movement.EnteredQuantity,
            movement.EnteredUnitOfMeasure,
            movement.ConversionRoundingDelta,
            movement.PackagingCode,
            movement.PackagingName,
            movement.PackagingType,
            movement.PackagingVersion,
            movement.PackagingUnitsPerPackage,
            movement.OwnerKind,
            movement.InventoryOwnerId,
            movement.OwnerCodeSnapshot);

    private static MovementReportDto MapToDto(MovementReportProjection row) =>
        new(
            row.Id,
            row.Type.ToString(),
            row.ItemSku,
            row.ItemName,
            row.FromLocationCode,
            row.ToLocationCode,
            row.Quantity,
            row.LotNumber,
            row.SerialNumber,
            row.UserId,
            row.ReferenceNumber,
            row.Notes,
            row.Timestamp)
        {
            BaseUnitOfMeasure = row.BaseUnitOfMeasure,
            EnteredQuantity = row.EnteredQuantity,
            EnteredUnitOfMeasure = row.EnteredUnitOfMeasure,
            DisplayQuantity = row.Quantity,
            DisplayUnitOfMeasure = row.BaseUnitOfMeasure,
            ConversionRoundingDelta = row.ConversionRoundingDelta,
            PackagingCode = row.PackagingCode,
            PackagingName = row.PackagingName,
            PackagingType = row.PackagingType,
            PackagingVersion = row.PackagingVersion,
            PackagingUnitsPerPackage = row.PackagingUnitsPerPackage,
            OwnerKind = row.OwnerKind,
            InventoryOwnerId = row.InventoryOwnerId,
            OwnerCodeSnapshot = row.OwnerCodeSnapshot
        };

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private static int CalculateTotalPages(int totalCount, int pageSize) =>
        totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)pageSize);

    private sealed record EffectiveMovementLedgerQuery(
        DateOnly FromDate,
        DateOnly ToDate,
        int? WarehouseId,
        string? SearchTerm,
        string? ItemSku,
        string? LocationCode,
        MovementType? MovementType,
        string? UserId,
        string? ReferenceNumber,
        string? LotNumber,
        string? SerialNumber,
        string? LicensePlateNumber,
        MovementReportSort Sort,
        bool Descending,
        int Page,
        int PageSize,
        string? DisplayUnitOfMeasure,
        string TimeZoneId,
        MovementReportGroupBy GroupBy);

    private sealed record MovementReportProjection(
        int Id,
        int ItemId,
        MovementType Type,
        string ItemSku,
        string ItemName,
        string? FromLocationCode,
        string? ToLocationCode,
        decimal Quantity,
        string? LotNumber,
        string? SerialNumber,
        string UserId,
        string? ReferenceNumber,
        string? Notes,
        DateTime Timestamp,
        string BaseUnitOfMeasure,
        decimal EnteredQuantity,
        string EnteredUnitOfMeasure,
        decimal ConversionRoundingDelta,
        string? PackagingCode,
        string? PackagingName,
        PackagingType? PackagingType,
        int? PackagingVersion,
        decimal? PackagingUnitsPerPackage,
        InventoryOwnerKind OwnerKind,
        int? InventoryOwnerId,
        string OwnerCodeSnapshot);

    private sealed record GroupAggregate<TKey>(
        TKey Key,
        int RowCount,
        decimal TotalBaseQuantity);

    private sealed class ReportConversionException(ResultError error) : Exception(error.Message)
    {
        public ResultError Error { get; } = error;
    }
}
