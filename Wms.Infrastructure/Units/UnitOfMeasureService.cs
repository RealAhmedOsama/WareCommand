using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Units;

public sealed class UnitOfMeasureService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<UnitOfMeasureService> logger)
    : IUnitOfMeasureManagementService, IItemQuantityConversionService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;

    public async Task<Result<UnitOfMeasurePageDto>> ListAsync(
        UnitOfMeasureListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<UnitOfMeasurePageDto>();
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var units = context.UnitOfMeasures.AsNoTracking().AsQueryable();
        if (!request.IncludeInactive)
        {
            units = units.Where(unit => unit.IsActive);
        }

        if (request.Category.HasValue)
        {
            units = units.Where(unit => unit.Category == request.Category.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            units = string.Equals(
                context.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal)
                ? units.Where(unit =>
                    EF.Functions.ILike(unit.Code, pattern) ||
                    EF.Functions.ILike(unit.Name, pattern) ||
                    EF.Functions.ILike(unit.LocalizedName, pattern) ||
                    EF.Functions.ILike(unit.Symbol, pattern))
                : units.Where(unit =>
                    EF.Functions.Like(unit.Code, pattern) ||
                    EF.Functions.Like(unit.Name, pattern) ||
                    EF.Functions.Like(unit.LocalizedName, pattern) ||
                    EF.Functions.Like(unit.Symbol, pattern));
        }

        var totalCount = await units.CountAsync(cancellationToken);
        var rows = await units
            .OrderBy(unit => unit.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new UnitOfMeasurePageDto(
            rows.Select(MapToDto).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<UnitOfMeasureDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<UnitOfMeasureDto>();
        }

        var unit = await context.UnitOfMeasures
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return unit is null
            ? Result.Failure<UnitOfMeasureDto>(WmsErrors.NotFound(
                "uom.not_found",
                "The unit of measure was not found."))
            : Result.Success(MapToDto(unit));
    }

    public async Task<Result<UnitOfMeasureDto>> CreateAsync(
        UnitOfMeasureRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<UnitOfMeasureDto>();
        }

        try
        {
            var normalizedCode = NormalizeUnit(request.Code);
            if (await context.UnitOfMeasures.AnyAsync(
                    unit => unit.Code == normalizedCode,
                    cancellationToken))
            {
                return Result.Failure<UnitOfMeasureDto>(WmsErrors.Conflict(
                    "uom.code_conflict",
                    $"Unit of measure '{normalizedCode}' already exists."));
            }

            var unit = new UnitOfMeasure(
                normalizedCode,
                request.Category,
                request.Precision,
                request.Symbol,
                request.Name,
                request.LocalizedName);
            context.UnitOfMeasures.Add(unit);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.UnitOfMeasureCreated,
                    WmsAuditEntityTypes.UnitOfMeasure,
                    normalizedCode,
                    After: UnitSnapshot(unit),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(unit));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<UnitOfMeasureDto>(WmsErrors.Validation(
                "uom.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unit of measure creation failed for {Code}", request.Code);
            return Result.Failure<UnitOfMeasureDto>(WmsErrors.FromException(
                exception,
                "uom.create_failed",
                "The unit of measure could not be created."));
        }
    }

    public async Task<Result<UnitOfMeasureDto>> UpdateAsync(
        int id,
        UnitOfMeasureRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<UnitOfMeasureDto>();
        }

        try
        {
            var unit = await context.UnitOfMeasures
                .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (unit is null)
            {
                return Result.Failure<UnitOfMeasureDto>(WmsErrors.NotFound(
                    "uom.not_found",
                    "The unit of measure was not found."));
            }

            var historical = await HasUnitHistoryAsync(unit.Code, cancellationToken);
            if (historical &&
                (unit.Category != request.Category || unit.Precision != request.Precision))
            {
                return Result.Failure<UnitOfMeasureDto>(WmsErrors.Conflict(
                    "uom.historical_change_blocked",
                    $"Category or precision for '{unit.Code}' cannot change after it was used by a transaction."));
            }

            var activeConversions = await context.ItemUnitConversions
                .AsNoTracking()
                .Where(conversion => conversion.IsActive &&
                                     (conversion.FromUnitOfMeasure == unit.Code ||
                                      conversion.ToUnitOfMeasure == unit.Code))
                .ToListAsync(cancellationToken);
            if (activeConversions.Count > 0 && unit.Category != request.Category)
            {
                return Result.Failure<UnitOfMeasureDto>(WmsErrors.Conflict(
                    "uom.conversion_category_locked",
                    $"Category for '{unit.Code}' cannot change while active item conversions reference it."));
            }

            if (activeConversions.Any(conversion =>
                    conversion.ToUnitOfMeasure == unit.Code &&
                    conversion.ResultPrecision > request.Precision))
            {
                return Result.Failure<UnitOfMeasureDto>(WmsErrors.Conflict(
                    "uom.conversion_precision_locked",
                    $"Precision for '{unit.Code}' cannot be lower than an active conversion result precision."));
            }

            var before = UnitSnapshot(unit);
            unit.Update(
                request.Category,
                request.Precision,
                request.Symbol,
                request.Name,
                request.LocalizedName);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.UnitOfMeasureUpdated,
                    WmsAuditEntityTypes.UnitOfMeasure,
                    unit.Code,
                    Before: before,
                    After: UnitSnapshot(unit),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(unit));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<UnitOfMeasureDto>(WmsErrors.Validation(
                "uom.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unit of measure update failed for {UnitId}", id);
            return Result.Failure<UnitOfMeasureDto>(WmsErrors.FromException(
                exception,
                "uom.update_failed",
                "The unit of measure could not be updated."));
        }
    }

    public async Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var unit = await context.UnitOfMeasures
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (unit is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "uom.not_found",
                "The unit of measure was not found."));
        }

        if (!active &&
            (await context.Items.AnyAsync(
                 item => item.UnitOfMeasure == unit.Code ||
                         item.PurchaseUnit == unit.Code ||
                         item.SalesUnit == unit.Code,
                 cancellationToken) ||
             await context.ItemUnitConversions.AnyAsync(
                 conversion => conversion.IsActive &&
                               (conversion.FromUnitOfMeasure == unit.Code ||
                                conversion.ToUnitOfMeasure == unit.Code),
                 cancellationToken)))
        {
            return Result.Failure(WmsErrors.Conflict(
                "uom.item_assignment_active",
                $"Unit '{unit.Code}' is assigned to an item and cannot be deactivated."));
        }

        if (active)
        {
            unit.Activate();
        }
        else
        {
            unit.Deactivate();
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                active ? WmsAuditActions.UnitOfMeasureActivated : WmsAuditActions.UnitOfMeasureDeactivated,
                WmsAuditEntityTypes.UnitOfMeasure,
                unit.Code,
                After: UnitSnapshot(unit),
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ItemUnitAssignmentDto>> GetItemAssignmentAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemUnitAssignmentDto>();
        }

        var item = await context.Items
            .AsNoTracking()
            .Include(candidate => candidate.UnitConversions)
            .SingleOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken);
        return item is null
            ? Result.Failure<ItemUnitAssignmentDto>(WmsErrors.NotFound(
                "item.not_found",
                "The item was not found."))
            : Result.Success(await MapAssignmentAsync(item, cancellationToken));
    }

    public async Task<Result<ItemUnitAssignmentDto>> SaveItemAssignmentAsync(
        ItemUnitAssignmentRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemUnitAssignmentDto>();
        }

        try
        {
            var item = await context.Items
                .Include(candidate => candidate.UnitConversions)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.ItemId, cancellationToken);
            if (item is null)
            {
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The item was not found."));
            }

            var requestedConversions = request.Conversions ?? [];
            var baseCode = NormalizeUnit(request.BaseUnitOfMeasure);
            var purchaseCode = NormalizeUnit(request.PurchaseUnitOfMeasure ?? baseCode);
            var salesCode = NormalizeUnit(request.SalesUnitOfMeasure ?? baseCode);
            var codes = requestedConversions
                .SelectMany(conversion => new[]
                {
                    NormalizeUnit(conversion.FromUnitOfMeasure),
                    NormalizeUnit(conversion.ToUnitOfMeasure)
                })
                .Concat([baseCode, purchaseCode, salesCode])
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var units = await context.UnitOfMeasures
                .Where(unit => codes.Contains(unit.Code))
                .ToDictionaryAsync(unit => unit.Code, StringComparer.Ordinal, cancellationToken);
            var missing = codes.FirstOrDefault(code => !units.ContainsKey(code));
            if (missing is not null)
            {
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                    "uom.assignment_unit_missing",
                    $"Unit of measure '{missing}' must be created before it can be assigned."));
            }

            if (units.Values.Any(unit => !unit.IsActive))
            {
                var inactive = units.Values.First(unit => !unit.IsActive);
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                    "uom.assignment_unit_inactive",
                    $"Unit of measure '{inactive.Code}' is inactive."));
            }

            var baseUnit = units[baseCode];
            if (units[purchaseCode].Category != baseUnit.Category ||
                units[salesCode].Category != baseUnit.Category)
            {
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                    "uom.assignment_category_mismatch",
                    "Base, purchase, and sales units must use the same category."));
            }

            var requested = new List<RequestedConversion>();
            foreach (var conversion in requestedConversions)
            {
                var fromCode = NormalizeUnit(conversion.FromUnitOfMeasure);
                var toCode = NormalizeUnit(conversion.ToUnitOfMeasure);
                if (fromCode == toCode)
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                        "uom.conversion_same_unit",
                        "A conversion must use two different units."));
                }

                if (conversion.ConversionFactor <= 0)
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                        "uom.conversion_factor_invalid",
                        "Conversion factors must be positive."));
                }

                if (units[fromCode].Category != units[toCode].Category)
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                        "uom.conversion_category_mismatch",
                        $"'{fromCode}' and '{toCode}' cannot be converted because their categories differ."));
                }

                if (conversion.ResultPrecision is < 0 or > 12 ||
                    conversion.ResultPrecision > units[toCode].Precision)
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                        "uom.conversion_precision_invalid",
                        $"The conversion result precision cannot exceed the '{toCode}' unit precision."));
                }

                if (requested.Any(existing =>
                        existing.FromCode == fromCode && existing.ToCode == toCode))
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Conflict(
                        "uom.conversion_duplicate",
                        $"The conversion from '{fromCode}' to '{toCode}' is duplicated."));
                }

                requested.Add(new RequestedConversion(
                    fromCode,
                    toCode,
                    conversion.ConversionFactor,
                    conversion.ResultPrecision,
                    conversion.RoundingMode));
            }

            var hasHistory = await HasTransactionalHistoryAsync(item.Id, cancellationToken);
            if (hasHistory && !string.Equals(item.UnitOfMeasure, baseCode, StringComparison.Ordinal))
            {
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Conflict(
                    "uom.base_change_blocked",
                    "The canonical base unit cannot change after stock or transaction history exists."));
            }

            if (hasHistory && item.AllowFractionalQuantity != request.AllowFractionalQuantity)
            {
                return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Conflict(
                    "uom.fraction_policy_change_blocked",
                    "The fractional-quantity policy cannot change after stock or transaction history exists."));
            }

            var proposedEdges = requested
                .Select(conversion => new ConversionEdge(
                    null,
                    conversion.FromCode,
                    conversion.ToCode,
                    conversion.Factor,
                    conversion.ResultPrecision,
                    conversion.RoundingMode))
                .ToArray();
            foreach (var requiredUnit in new[] { purchaseCode, salesCode })
            {
                if (requiredUnit == baseCode)
                {
                    continue;
                }

                if (!TryResolvePath(
                        proposedEdges,
                        requiredUnit,
                        baseCode,
                        out _,
                        units.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value.Precision,
                            StringComparer.Ordinal)))
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                        "uom.assignment_path_missing",
                        $"No deterministic conversion path exists from '{requiredUnit}' to base unit '{baseCode}'."));
                }
            }

            var before = AssignmentSnapshot(item);
            item.UpdateUnits(baseCode, purchaseCode, salesCode, request.AllowFractionalQuantity);

            var activeConversions = item.UnitConversions
                .Where(conversion => conversion.IsActive)
                .ToDictionary(
                    conversion => (conversion.FromUnitOfMeasure, conversion.ToUnitOfMeasure),
                    StringTupleComparer.Instance);
            var requestedPairs = requested
                .Select(conversion => (conversion.FromCode, conversion.ToCode))
                .ToHashSet(StringTupleComparer.Instance);
            foreach (var existing in item.UnitConversions.Where(conversion =>
                         conversion.IsActive &&
                         !requestedPairs.Contains((conversion.FromUnitOfMeasure, conversion.ToUnitOfMeasure))))
            {
                if (await IsConversionUsedAsync(existing.Id, item.Id, cancellationToken))
                {
                    return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Conflict(
                        "uom.conversion_removal_blocked",
                        $"Conversion '{existing.FromUnitOfMeasure} -> {existing.ToUnitOfMeasure}' is historical and cannot be removed."));
                }
            }

            foreach (var conversion in requested)
            {
                if (activeConversions.TryGetValue((conversion.FromCode, conversion.ToCode), out var existing))
                {
                    if (existing.ConversionFactor == conversion.Factor &&
                        existing.ResultPrecision == conversion.ResultPrecision &&
                        existing.RoundingMode == conversion.RoundingMode)
                    {
                        continue;
                    }

                    if (await IsConversionUsedAsync(existing.Id, item.Id, cancellationToken))
                    {
                        existing.Deactivate();
                        var next = existing.CreateNextVersion(
                            conversion.Factor,
                            conversion.ResultPrecision,
                            conversion.RoundingMode);
                        item.AddUnitConversion(next);
                        context.ItemUnitConversions.Add(next);
                    }
                    else
                    {
                        existing.Update(
                            conversion.Factor,
                            conversion.ResultPrecision,
                            conversion.RoundingMode);
                    }

                    continue;
                }

                var nextVersion = item.UnitConversions
                    .Where(existing =>
                        existing.FromUnitOfMeasure == conversion.FromCode &&
                        existing.ToUnitOfMeasure == conversion.ToCode)
                    .Select(existing => existing.Version)
                    .DefaultIfEmpty(0)
                    .Max() + 1;
                var created = new ItemUnitConversion(
                    item.Id,
                    conversion.FromCode,
                    conversion.ToCode,
                    conversion.Factor,
                    conversion.ResultPrecision,
                    conversion.RoundingMode,
                    nextVersion);
                item.AddUnitConversion(created);
                context.ItemUnitConversions.Add(created);
            }

            foreach (var existing in item.UnitConversions
                         .Where(conversion => conversion.IsActive)
                         .ToArray())
            {
                if (requested.Any(conversion =>
                        conversion.FromCode == existing.FromUnitOfMeasure &&
                        conversion.ToCode == existing.ToUnitOfMeasure))
                {
                    continue;
                }

                existing.Deactivate();
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ItemUnitsAssigned,
                    WmsAuditEntityTypes.Item,
                    item.Sku,
                    Before: before,
                    After: AssignmentSnapshot(item),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(await MapAssignmentAsync(item, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.Validation(
                "uom.assignment_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Item unit assignment failed for {ItemId}", request.ItemId);
            return Result.Failure<ItemUnitAssignmentDto>(WmsErrors.FromException(
                exception,
                "uom.assignment_failed",
                "The item unit assignment could not be saved."));
        }
    }

    public async Task<Result<QuantityConversionResult>> ConvertToBaseAsync(
        int itemId,
        decimal enteredQuantity,
        string? enteredUnitOfMeasure = null,
        QuantityRoundingMode? roundingMode = null,
        CancellationToken cancellationToken = default)
    {
        if (enteredQuantity < 0)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.negative",
                "Quantity cannot be negative."));
        }

        var item = await context.Items
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.NotFound(
                "item.not_found",
                "The item was not found."));
        }

        string fromCode;
        string baseCode;
        try
        {
            fromCode = NormalizeUnit(enteredUnitOfMeasure ?? item.UnitOfMeasure);
            baseCode = NormalizeUnit(item.UnitOfMeasure);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.unit_invalid",
                exception.Message));
        }

        var pathResult = await ResolveItemPathAsync(item, fromCode, baseCode, cancellationToken);
        if (pathResult.IsFailure)
        {
            return pathResult.ToFailure<QuantityConversionResult>();
        }

        if (pathResult.Value.FromUnit.Precision < GetDecimalScale(enteredQuantity))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.precision_exceeded",
                $"Quantity '{enteredQuantity}' exceeds the precision allowed for unit '{fromCode}'."));
        }

        if (!item.AllowFractionalQuantity && !IsWhole(enteredQuantity))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.fractional_not_allowed",
                $"Item '{item.Sku}' does not allow fractional quantities."));
        }

        if (item.RequiresSerial && !IsWhole(enteredQuantity))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.serial_fractional",
                "Serial-controlled stock must use whole quantities."));
        }

        var path = pathResult.Value.Path;
        if (!TryApplyPath(
                enteredQuantity,
                pathResult.Value,
                roundingMode,
                out var roundedBaseQuantity,
                out var roundingDelta,
                out var resultPrecision,
                out var effectiveMode,
                out _))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.rounding_required",
                $"Converting '{enteredQuantity} {fromCode}' to '{baseCode}' requires explicit rounding."));
        }

        if (!item.AllowFractionalQuantity && !IsWhole(roundedBaseQuantity) ||
            item.RequiresSerial && !IsWhole(roundedBaseQuantity))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.base_fractional",
                $"The converted quantity for item '{item.Sku}' must be a whole number."));
        }

        var snapshot = new QuantityConversionResult(
            enteredQuantity,
            fromCode,
            roundedBaseQuantity,
            baseCode,
            path.Factor,
            resultPrecision,
            effectiveMode,
            roundingDelta,
            path.PathDescription,
            path.RuleIds);
        return Result.Success(snapshot);
    }

    public async Task<Result<QuantityConversionResult>> ConvertPackagingToBaseAsync(
        int itemId,
        decimal enteredPackageQuantity,
        string packagingCode,
        QuantityRoundingMode? roundingMode = null,
        CancellationToken cancellationToken = default)
    {
        if (enteredPackageQuantity < 0)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "quantity.negative",
                "Package quantity cannot be negative."));
        }

        if (string.IsNullOrWhiteSpace(packagingCode))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.code_required",
                "A packaging code is required when a package quantity is entered."));
        }

        var normalizedCode = packagingCode.Trim().ToUpperInvariant();
        var packaging = await context.ItemPackagings
            .AsNoTracking()
            .Include(value => value.Item)
            .SingleOrDefaultAsync(
                value => value.ItemId == itemId && value.Code == normalizedCode,
                cancellationToken);
        if (packaging is null)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.NotFound(
                "packaging.not_found",
                $"Packaging '{normalizedCode}' was not found for the item."));
        }

        if (!packaging.IsActive)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.inactive",
                $"Packaging '{packaging.Code}' is inactive."));
        }

        if (packaging.PartialPackagePolicy == PackagingPartialPolicy.Reject &&
            !IsWhole(enteredPackageQuantity))
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.partial_not_allowed",
                $"Packaging '{packaging.Code}' does not allow partial packages."));
        }

        decimal containedQuantity;
        try
        {
            containedQuantity = checked(enteredPackageQuantity * packaging.UnitsPerPackage);
        }
        catch (OverflowException)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.quantity_overflow",
                "The package quantity is too large to convert safely."));
        }

        var conversion = await ConvertToBaseAsync(
            itemId,
            containedQuantity,
            packaging.UnitOfMeasure,
            roundingMode,
            cancellationToken);
        if (conversion.IsFailure)
        {
            return conversion;
        }

        var value = conversion.Value;
        PackagingConversionSnapshot packageSnapshot;
        try
        {
            packageSnapshot = new PackagingConversionSnapshot(
                packaging.Id,
                packaging.Version,
                packaging.Code,
                packaging.Name,
                packaging.LocalizedName,
                packaging.Type,
                packaging.UnitOfMeasure,
                packaging.UnitsPerPackage,
                packaging.PartialPackagePolicy,
                packaging.GrossWeightKg,
                packaging.LengthCm,
                packaging.WidthCm,
                packaging.HeightCm,
                packaging.VolumeCubicMeters);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.definition_invalid",
                exception.Message));
        }

        decimal factor;
        try
        {
            factor = checked(packaging.UnitsPerPackage * value.ConversionFactorToBase);
        }
        catch (OverflowException)
        {
            return Result.Failure<QuantityConversionResult>(WmsErrors.Validation(
                "packaging.conversion_overflow",
                "The packaging conversion factor is too large to represent safely."));
        }

        return Result.Success(new QuantityConversionResult(
            enteredPackageQuantity,
            packaging.UnitOfMeasure,
            value.BaseQuantity,
            value.BaseUnitOfMeasure,
            factor,
            value.ResultPrecision,
            value.RoundingMode,
            value.RoundingDelta,
            $"{packaging.Code} ({packaging.UnitOfMeasure}) -> {value.ConversionPath}",
            value.ConversionRuleIds)
        {
            PackagingSnapshot = packageSnapshot
        });
    }

    public async Task<Result<decimal>> ConvertFromBaseAsync(
        int itemId,
        decimal baseQuantity,
        string displayUnitOfMeasure,
        QuantityRoundingMode roundingMode = QuantityRoundingMode.Reject,
        CancellationToken cancellationToken = default)
    {
        if (baseQuantity < 0)
        {
            return Result.Failure<decimal>(WmsErrors.Validation(
                "quantity.negative",
                "Quantity cannot be negative."));
        }

        var item = await context.Items
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Result.Failure<decimal>(WmsErrors.NotFound(
                "item.not_found",
                "The item was not found."));
        }

        string baseCode;
        string displayCode;
        try
        {
            baseCode = NormalizeUnit(item.UnitOfMeasure);
            displayCode = NormalizeUnit(displayUnitOfMeasure);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<decimal>(WmsErrors.Validation(
                "quantity.unit_invalid",
                exception.Message));
        }

        var pathResult = await ResolveItemPathAsync(item, baseCode, displayCode, cancellationToken);
        if (pathResult.IsFailure)
        {
            return pathResult.ToFailure<decimal>();
        }

        if (!TryApplyPath(
                baseQuantity,
                pathResult.Value,
                roundingMode,
                out var converted,
                out _,
                out _,
                out _,
                out var roundingRequired) && roundingRequired)
        {
            return Result.Failure<decimal>(WmsErrors.Validation(
                "quantity.rounding_required",
                $"Displaying {baseCode} quantity in '{displayCode}' requires explicit rounding."));
        }

        return Result.Success(converted);
    }

    private async Task<Result<ResolvedItemPath>> ResolveItemPathAsync(
        Item item,
        string fromCode,
        string toCode,
        CancellationToken cancellationToken)
    {
        var units = await context.UnitOfMeasures
            .AsNoTracking()
            .ToDictionaryAsync(unit => unit.Code, StringComparer.Ordinal, cancellationToken);
        if (!units.TryGetValue(fromCode, out var fromUnit) ||
            !units.TryGetValue(toCode, out var toUnit))
        {
            var missing = !units.ContainsKey(fromCode) ? fromCode : toCode;
            return Result.Failure<ResolvedItemPath>(WmsErrors.NotFound(
                "uom.not_found",
                $"Unit of measure '{missing}' was not found."));
        }

        if (!fromUnit.IsActive || !toUnit.IsActive)
        {
            return Result.Failure<ResolvedItemPath>(WmsErrors.BusinessRule(
                "uom.inactive",
                "An inactive unit of measure cannot be used for a new quantity."));
        }

        if (fromUnit.Category != toUnit.Category)
        {
            return Result.Failure<ResolvedItemPath>(WmsErrors.Validation(
                "uom.category_mismatch",
                $"'{fromCode}' and '{toCode}' belong to incompatible categories."));
        }

        var conversions = await context.ItemUnitConversions
            .AsNoTracking()
            .Where(conversion => conversion.ItemId == item.Id && conversion.IsActive)
            .ToListAsync(cancellationToken);
        var edges = conversions
            .Where(conversion =>
                units.TryGetValue(conversion.FromUnitOfMeasure, out var edgeFrom) &&
                units.TryGetValue(conversion.ToUnitOfMeasure, out var edgeTo) &&
                edgeFrom.IsActive &&
                edgeTo.IsActive &&
                edgeFrom.Category == edgeTo.Category &&
                edgeFrom.Category == fromUnit.Category)
            .Select(conversion => new ConversionEdge(
                conversion.Id,
                conversion.FromUnitOfMeasure,
                conversion.ToUnitOfMeasure,
                conversion.ConversionFactor,
                conversion.ResultPrecision,
                conversion.RoundingMode))
            .ToArray();
        var unitPrecisions = units.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Precision,
            StringComparer.Ordinal);
        if (!TryResolvePath(edges, fromCode, toCode, out var path, unitPrecisions))
        {
            return Result.Failure<ResolvedItemPath>(WmsErrors.Validation(
                "uom.conversion_missing",
                $"No deterministic conversion path exists from '{fromCode}' to '{toCode}' for item '{item.Sku}'."));
        }

        return Result.Success(new ResolvedItemPath(path, fromUnit, toUnit, unitPrecisions));
    }

    private async Task<bool> HasTransactionalHistoryAsync(
        int itemId,
        CancellationToken cancellationToken) =>
        await context.Stock.AnyAsync(stock => stock.ItemId == itemId, cancellationToken) ||
        await context.Lots.AnyAsync(lot => lot.ItemId == itemId, cancellationToken) ||
        await context.Movements.AnyAsync(movement => movement.ItemId == itemId, cancellationToken);

    private async Task<bool> HasUnitHistoryAsync(
        string code,
        CancellationToken cancellationToken) =>
        await context.Movements.AnyAsync(
            movement => movement.EnteredUnitOfMeasure == code ||
                        movement.BaseUnitOfMeasure == code ||
                        movement.ConversionPath == code ||
                        movement.ConversionPath.StartsWith(code + " -> ") ||
                        movement.ConversionPath.EndsWith(" -> " + code) ||
                        movement.ConversionPath.Contains(" -> " + code + " -> "),
            cancellationToken);

    private async Task<bool> IsConversionUsedAsync(
        int conversionId,
        int itemId,
        CancellationToken cancellationToken)
    {
        var token = $"|{conversionId}|";
        return await context.Movements.AnyAsync(
            movement => movement.ItemId == itemId &&
                        movement.ConversionRuleIds.Contains(token),
            cancellationToken);
    }

    private async Task<Result> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(
            permission,
            cancellationToken: cancellationToken);

    private async Task<ItemUnitAssignmentDto> MapAssignmentAsync(
        Item item,
        CancellationToken cancellationToken)
    {
        var conversions = new List<ItemUnitConversionDto>();
        foreach (var conversion in item.UnitConversions.OrderBy(
                     candidate => candidate.FromUnitOfMeasure))
        {
            conversions.Add(new ItemUnitConversionDto(
                conversion.Id,
                conversion.FromUnitOfMeasure,
                conversion.ToUnitOfMeasure,
                conversion.ConversionFactor,
                conversion.ResultPrecision,
                conversion.RoundingMode,
                conversion.Version,
                conversion.IsActive,
                await IsConversionUsedAsync(conversion.Id, item.Id, cancellationToken)));
        }

        return new ItemUnitAssignmentDto(
            item.Id,
            item.Sku,
            item.UnitOfMeasure,
            item.PurchaseUnit,
            item.SalesUnit,
            item.AllowFractionalQuantity,
            conversions);
    }

    private static UnitOfMeasureDto MapToDto(UnitOfMeasure unit) =>
        new(
            unit.Id,
            unit.Code,
            unit.Category,
            unit.Precision,
            unit.Symbol,
            unit.Name,
            unit.LocalizedName,
            unit.IsActive,
            unit.CreatedAt,
            unit.UpdatedAt);

    private static string NormalizeUnit(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A unit of measure is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 20)
        {
            throw new ArgumentException(
                "A unit of measure cannot exceed 20 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static int GetDecimalScale(decimal value)
    {
        var scale = (decimal.GetBits(value)[3] >> 16) & 0x7F;
        while (scale > 0 && value == decimal.Round(value, scale - 1, MidpointRounding.ToEven))
        {
            scale--;
        }

        return scale;
    }

    private static bool IsWhole(decimal value) => decimal.Truncate(value) == value;

    private static bool TryApplyPath(
        decimal quantity,
        ResolvedItemPath resolvedPath,
        QuantityRoundingMode? requestedMode,
        out decimal converted,
        out decimal roundingDelta,
        out int resultPrecision,
        out QuantityRoundingMode effectiveMode,
        out bool roundingRequired)
    {
        var current = quantity;
        roundingDelta = 0m;
        roundingRequired = false;
        resultPrecision = resolvedPath.Path.ResultPrecision;
        effectiveMode = requestedMode ?? resolvedPath.Path.EffectiveRoundingMode;

        foreach (var step in resolvedPath.Path.Steps)
        {
            resultPrecision = step.IsReverse
                ? resolvedPath.UnitPrecisions.TryGetValue(step.ToCode, out var reversePrecision)
                    ? reversePrecision
                    : step.Rule.ResultPrecision
                : step.Rule.ResultPrecision;
            effectiveMode = requestedMode ?? step.Rule.RoundingMode;

            var exact = current * step.Factor;
            var rounded = ApplyRounding(
                exact,
                resultPrecision,
                effectiveMode,
                out var stepDelta);
            if (effectiveMode == QuantityRoundingMode.Reject && stepDelta != 0m)
            {
                roundingRequired = true;
                converted = current;
                return false;
            }

            current = rounded;
            roundingDelta += stepDelta;
        }

        converted = current;
        return true;
    }

    private static decimal ApplyRounding(
        decimal value,
        int precision,
        QuantityRoundingMode mode,
        out decimal delta)
    {
        var roundingMode = mode switch
        {
            QuantityRoundingMode.ToEven => MidpointRounding.ToEven,
            QuantityRoundingMode.AwayFromZero => MidpointRounding.AwayFromZero,
            QuantityRoundingMode.ToZero => MidpointRounding.ToZero,
            _ => MidpointRounding.ToEven
        };
        var rounded = decimal.Round(value, precision, roundingMode);
        delta = Math.Abs(value - rounded);
        return rounded;
    }

    private static bool TryResolvePath(
        IReadOnlyList<ConversionEdge> edges,
        string fromCode,
        string toCode,
        out ResolvedPath path,
        Dictionary<string, int>? unitPrecisions = null)
    {
        if (fromCode == toCode)
        {
            path = new ResolvedPath(
                [fromCode],
                [],
                1m,
                unitPrecisions is not null && unitPrecisions.TryGetValue(toCode, out var sameUnitPrecision)
                    ? sameUnitPrecision
                    : 0,
                QuantityRoundingMode.Reject,
                string.Empty);
            return true;
        }

        var adjacency = edges
            .SelectMany(edge => new[]
            {
                new PathStep(edge, false, edge.FromCode, edge.ToCode, edge.Factor),
                new PathStep(edge, true, edge.ToCode, edge.FromCode, 1m / edge.Factor)
            })
            .GroupBy(step => step.FromCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(step => step.ToCode, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var queue = new Queue<PathState>();
        queue.Enqueue(new PathState(fromCode, [fromCode], [], 1m));
        var shortestLength = int.MaxValue;
        var matches = new List<ResolvedPath>();
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            if (state.Steps.Count >= shortestLength)
            {
                continue;
            }

            if (!adjacency.TryGetValue(state.UnitCode, out var nextSteps))
            {
                continue;
            }

            foreach (var step in nextSteps)
            {
                if (state.Units.Contains(step.ToCode, StringComparer.Ordinal))
                {
                    continue;
                }

                var units = state.Units.Append(step.ToCode).ToArray();
                var steps = state.Steps.Append(step).ToArray();
                var factor = state.Factor * step.Factor;
                if (step.ToCode == toCode)
                {
                    shortestLength = steps.Length;
                    var resultPrecision = step.IsReverse &&
                                          unitPrecisions is not null &&
                                          unitPrecisions.TryGetValue(step.ToCode, out var reversePrecision)
                        ? reversePrecision
                        : step.Rule.ResultPrecision;
                    matches.Add(new ResolvedPath(
                        units,
                        steps,
                        factor,
                        resultPrecision,
                        steps[^1].Rule.RoundingMode,
                        string.Join(" -> ", units)));
                }
                else if (steps.Length < shortestLength)
                {
                    queue.Enqueue(new PathState(step.ToCode, units, steps, factor));
                }
            }
        }

        if (matches.Count == 0)
        {
            path = new ResolvedPath(
                [],
                [],
                0m,
                0,
                QuantityRoundingMode.Reject,
                string.Empty);
            return false;
        }

        var first = matches[0];
        if (matches.Any(candidate =>
                candidate.Factor != first.Factor ||
                candidate.ResultPrecision != first.ResultPrecision ||
                candidate.EffectiveRoundingMode != first.EffectiveRoundingMode))
        {
            path = new ResolvedPath(
                [],
                [],
                0m,
                0,
                QuantityRoundingMode.Reject,
                string.Empty);
            return false;
        }

        var ruleIds = string.Concat(
            first.Steps
                .Where(step => step.Rule.Id.HasValue)
                .Select(step => $"|{step.Rule.Id.GetValueOrDefault()}|"));
        path = first with
        {
            RuleIds = ruleIds
        };
        return true;
    }

    private static Dictionary<string, object?> UnitSnapshot(UnitOfMeasure unit) => new()
    {
        ["code"] = unit.Code,
        ["category"] = unit.Category.ToString(),
        ["precision"] = unit.Precision,
        ["symbol"] = unit.Symbol,
        ["name"] = unit.Name,
        ["localizedName"] = unit.LocalizedName,
        ["isActive"] = unit.IsActive
    };

    private static Dictionary<string, object?> AssignmentSnapshot(Item item) => new()
    {
        ["sku"] = item.Sku,
        ["baseUnit"] = item.UnitOfMeasure,
        ["purchaseUnit"] = item.PurchaseUnit,
        ["salesUnit"] = item.SalesUnit,
        ["allowFractionalQuantity"] = item.AllowFractionalQuantity,
        ["conversionCount"] = item.UnitConversions.Count(candidate => candidate.IsActive)
    };

    private sealed record RequestedConversion(
        string FromCode,
        string ToCode,
        decimal Factor,
        int ResultPrecision,
        QuantityRoundingMode RoundingMode);

    private sealed record ConversionEdge(
        int? Id,
        string FromCode,
        string ToCode,
        decimal Factor,
        int ResultPrecision,
        QuantityRoundingMode RoundingMode);

    private sealed record PathStep(
        ConversionEdge Rule,
        bool IsReverse,
        string FromCode,
        string ToCode,
        decimal Factor);

    private sealed record PathState(
        string UnitCode,
        IReadOnlyList<string> Units,
        IReadOnlyList<PathStep> Steps,
        decimal Factor);

    private sealed record ResolvedPath(
        IReadOnlyList<string> Units,
        IReadOnlyList<PathStep> Steps,
        decimal Factor,
        int ResultPrecision,
        QuantityRoundingMode EffectiveRoundingMode,
        string PathDescription,
        string RuleIds = "");

    private sealed record ResolvedItemPath(
        ResolvedPath Path,
        UnitOfMeasure FromUnit,
        UnitOfMeasure ToUnit,
        IReadOnlyDictionary<string, int> UnitPrecisions);

    private sealed class StringTupleComparer : IEqualityComparer<(string From, string To)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string From, string To) x, (string From, string To) y) =>
            string.Equals(x.From, y.From, StringComparison.Ordinal) &&
            string.Equals(x.To, y.To, StringComparison.Ordinal);

        public int GetHashCode((string From, string To) obj) =>
            HashCode.Combine(obj.From, obj.To);
    }
}
