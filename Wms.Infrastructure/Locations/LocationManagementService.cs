using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Locations;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Locations;

public sealed class LocationManagementService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<LocationManagementService> logger) : ILocationManagementService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumBulkRows = 1_000;
    private const int MaximumHierarchyDepth = 32;

    public async Task<Result<LocationPageDto>> ListAsync(
        LocationListQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<LocationPageDto>();
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var locations = ApplyScope(context.Locations.AsNoTracking(), scope);

        if (query.WarehouseId.HasValue)
        {
            locations = locations.Where(location => location.WarehouseId == query.WarehouseId.Value);
        }

        if (!query.IncludeInactive)
        {
            locations = locations.Where(location => location.IsActive);
        }

        if (query.Type.HasValue)
        {
            locations = locations.Where(location => location.Type == query.Type.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = $"%{query.SearchTerm.Trim()}%";
            locations = string.Equals(
                context.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal)
                ? locations.Where(location =>
                    EF.Functions.ILike(location.Code, pattern) ||
                    EF.Functions.ILike(location.Name, pattern) ||
                    (location.Barcode != null && EF.Functions.ILike(location.Barcode, pattern)))
                : locations.Where(location =>
                    EF.Functions.Like(location.Code, pattern) ||
                    EF.Functions.Like(location.Name, pattern) ||
                    (location.Barcode != null && EF.Functions.Like(location.Barcode, pattern)));
        }

        var totalCount = await locations.CountAsync(cancellationToken);
        var rows = await locations
            .OrderBy(location => location.WarehouseId)
            .ThenBy(location => location.ParentLocationId)
            .ThenBy(location => location.Priority)
            .ThenBy(location => location.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(location => location.ParentLocation)
            .ToListAsync(cancellationToken);

        return Result.Success(new LocationPageDto(
            rows.Select(MapToDto).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<IReadOnlyList<LocationDto>>> GetChildrenAsync(
        int warehouseId,
        int? parentLocationId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<LocationDto>>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(context.Locations.AsNoTracking(), scope)
            .Where(location => location.WarehouseId == warehouseId &&
                               location.ParentLocationId == parentLocationId)
            .OrderBy(location => location.Priority)
            .ThenBy(location => location.Code)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, MaximumPageSize))
            .Take(Math.Clamp(pageSize, 1, MaximumPageSize))
            .Include(location => location.ParentLocation);

        var rows = await query.ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<LocationDto>>(rows.Select(MapToDto).ToArray());
    }

    public async Task<Result<LocationDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var location = await LoadVisibleLocationAsync(id, cancellationToken);
        if (location is null)
        {
            return Result.Failure<LocationDto>(WmsErrors.NotFound(
                "location.not_found",
                "The requested location was not found."));
        }

        return Result.Success(MapToDto(location));
    }

    public async Task<Result<LocationDto>> CreateAsync(
        LocationCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<LocationDto>();
        }

        try
        {
            var warehouseExists = await context.Warehouses
                .AsNoTracking()
                .AnyAsync(warehouse => warehouse.Id == request.WarehouseId && warehouse.IsActive, cancellationToken);
            if (!warehouseExists)
            {
                return Result.Failure<LocationDto>(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The selected warehouse was not found or is inactive."));
            }

            var normalizedCode = NormalizeCode(request.Code);
            if (await CodeExistsAsync(request.WarehouseId, normalizedCode, null, cancellationToken))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.code_conflict",
                    $"Location code '{normalizedCode}' already exists in this warehouse."));
            }

            if (await BarcodeExistsAsync(request.WarehouseId, request.Barcode, null, cancellationToken))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.barcode_conflict",
                    "The location barcode is already assigned in this warehouse."));
            }

            var parentResult = await ValidateParentAsync(
                request.WarehouseId,
                request.ParentLocationId,
                null,
                cancellationToken);
            if (parentResult.IsFailure)
            {
                return parentResult.ToFailure<LocationDto>();
            }

            var location = new Location(
                normalizedCode,
                request.Name,
                request.WarehouseId,
                request.ParentLocationId,
                request.Type,
                request.Barcode,
                request.Priority,
                request.IsPickable,
                request.IsReceivable,
                request.IsCountable,
                request.AllowMixedItems,
                request.AllowMixedLots,
                request.MaxUnits,
                request.MaxWeightKg,
                request.MaxVolumeCubicMeters,
                request.MaxPallets,
                request.MaxLpns,
                request.StorageProfile,
                request.MinimumTemperatureCelsius,
                request.MaximumTemperatureCelsius,
                request.HazardClass,
                request.AccessRestriction,
                request.ConstraintAttributesJson);
            context.Locations.Add(location);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationCreated,
                    WmsAuditEntityTypes.Location,
                    normalizedCode,
                    request.WarehouseId,
                    After: ToAuditValues(location),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<LocationDto>(WmsErrors.Validation(
                "location.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create location {LocationCode}", request.Code);
            return Result.Failure<LocationDto>(WmsErrors.FromException(
                exception,
                "location.create_failed",
                "The location could not be created."));
        }
    }

    public async Task<Result<LocationDto>> UpdateAsync(
        LocationUpdateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var location = await context.Locations
            .Include(item => item.ParentLocation)
            .SingleOrDefaultAsync(item => item.Id == request.Id, cancellationToken);
        if (location is null)
        {
            return Result.Failure<LocationDto>(WmsErrors.NotFound(
                "location.not_found",
                "The requested location was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            location.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<LocationDto>();
        }

        try
        {
            var parentChanged = location.ParentLocationId != request.ParentLocationId;
            var typeChanged = location.Type != request.Type;
            var stock = await GetStockSnapshotAsync(location.Id, cancellationToken);
            if ((parentChanged || typeChanged) && (stock.Units > 0 || stock.ReservedUnits > 0))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.restructure_blocked",
                    "A location with stock or reservations cannot be restructured or change type."));
            }

            if ((parentChanged || typeChanged) && await HasRunningWarehouseJobsAsync(
                    location.WarehouseId,
                    cancellationToken))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.restructure_blocked",
                    "A location cannot be restructured while warehouse work is running."));
            }

            var parentResult = await ValidateParentAsync(
                location.WarehouseId,
                request.ParentLocationId,
                location.Id,
                cancellationToken);
            if (parentResult.IsFailure)
            {
                return parentResult.ToFailure<LocationDto>();
            }

            if (request.MaxUnits.HasValue && stock.Units > request.MaxUnits.Value)
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.capacity_reduction_blocked",
                    "The configured unit capacity is below the stock currently stored at this location."));
            }

            if (await BarcodeExistsAsync(location.WarehouseId, request.Barcode, location.Id, cancellationToken))
            {
                return Result.Failure<LocationDto>(WmsErrors.Conflict(
                    "location.barcode_conflict",
                    "The location barcode is already assigned in this warehouse."));
            }

            var before = ToAuditValues(location);
            location.UpdateDetails(request.Name);
            location.UpdateDefinition(
                request.Type,
                request.Barcode,
                request.Priority,
                request.IsPickable,
                request.IsReceivable,
                request.IsCountable,
                request.AllowMixedItems,
                request.AllowMixedLots,
                request.MaxUnits,
                request.MaxWeightKg,
                request.MaxVolumeCubicMeters,
                request.MaxPallets,
                request.MaxLpns,
                request.StorageProfile,
                request.MinimumTemperatureCelsius,
                request.MaximumTemperatureCelsius,
                request.HazardClass,
                request.AccessRestriction,
                request.ConstraintAttributesJson);
            if (parentChanged)
            {
                location.SetParentLocation(request.ParentLocationId);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationUpdated,
                    WmsAuditEntityTypes.Location,
                    location.Id.ToString(CultureInfo.InvariantCulture),
                    location.WarehouseId,
                    Before: before,
                    After: ToAuditValues(location),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(location));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<LocationDto>(WmsErrors.Validation(
                "location.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not update location {LocationId}", request.Id);
            return Result.Failure<LocationDto>(WmsErrors.FromException(
                exception,
                "location.update_failed",
                "The location could not be updated."));
        }
    }

    public async Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var location = await context.Locations
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (location is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "location.not_found",
                "The requested location was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            location.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        if (location.IsActive == active)
        {
            return Result.Success();
        }

        if (active)
        {
            if (location.ParentLocationId.HasValue)
            {
                var parent = await context.Locations
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == location.ParentLocationId.Value, cancellationToken);
                if (parent is null || !parent.IsActive)
                {
                    return Result.Failure(WmsErrors.Conflict(
                        "location.parent_inactive",
                        "A location cannot be activated while its parent is inactive."));
                }
            }

            location.Activate();
        }
        else
        {
            var unsafeUse = await GetUnsafeDeactivationReasonAsync(location, cancellationToken);
            if (unsafeUse is not null)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "location.deactivation_blocked",
                    unsafeUse));
            }

            location.Deactivate();
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                active ? WmsAuditActions.LocationActivated : WmsAuditActions.LocationDeactivated,
                WmsAuditEntityTypes.Location,
                location.Id.ToString(CultureInfo.InvariantCulture),
                location.WarehouseId,
                After: ToAuditValues(location),
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var location = await context.Locations
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (location is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "location.not_found",
                "The requested location was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            location.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        if (await context.Locations.AnyAsync(
                child => child.ParentLocationId == location.Id,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.Conflict(
                "location.children_exist",
                "A location with child locations cannot be deleted. Move or delete the children first."));
        }

        var unsafeUse = await GetUnsafeDeactivationReasonAsync(location, cancellationToken);
        if (unsafeUse is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "location.delete_blocked",
                unsafeUse));
        }

        if (!location.IsActive)
        {
            return Result.Success();
        }

        location.Deactivate();
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.LocationDeactivated,
                WmsAuditEntityTypes.Location,
                location.Id.ToString(CultureInfo.InvariantCulture),
                location.WarehouseId,
                After: ToAuditValues(location),
                ActorUserId: userId,
                Details: "Location soft-deleted after safety checks."),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<LocationDto>>> GenerateAsync(
        BulkLocationGenerationRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<LocationDto>>();
        }

        if (request.Count is < 1 or > MaximumBulkRows ||
            request.StartNumber < 0 ||
            request.NumberWidth is < 1 or > 20 ||
            string.IsNullOrWhiteSpace(request.CodePrefix) ||
            string.IsNullOrWhiteSpace(request.NamePrefix))
        {
            return Result.Failure<IReadOnlyList<LocationDto>>(WmsErrors.Validation(
                "location.bulk_generation_invalid",
                "Bulk generation requires 1-1,000 rows, a positive number width, and code/name prefixes."));
        }

        var parentResult = await ValidateParentAsync(
            request.WarehouseId,
            request.ParentLocationId,
            null,
            cancellationToken);
        if (parentResult.IsFailure)
        {
            return parentResult.ToFailure<IReadOnlyList<LocationDto>>();
        }

        var prefix = NormalizeCode(request.CodePrefix);
        var codes = Enumerable.Range(request.StartNumber, request.Count)
            .Select(number => prefix + number.ToString($"D{request.NumberWidth}", CultureInfo.InvariantCulture))
            .ToArray();
        if (codes.Any(code => code.Length > 50) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length)
        {
            return Result.Failure<IReadOnlyList<LocationDto>>(WmsErrors.Validation(
                "location.bulk_generation_codes_invalid",
                "Generated location codes must be unique and no longer than 50 characters."));
        }

        var existingCodes = await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == request.WarehouseId && codes.Contains(location.Code))
            .Select(location => location.Code)
            .ToListAsync(cancellationToken);
        if (existingCodes.Count > 0)
        {
            return Result.Failure<IReadOnlyList<LocationDto>>(WmsErrors.Conflict(
                "location.bulk_generation_conflict",
                $"Generated location codes already exist: {string.Join(", ", existingCodes)}."));
        }

        var created = new List<Location>(request.Count);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (code, index) in codes.Select((code, index) => (code, index)))
            {
                var location = new Location(
                    code,
                    $"{request.NamePrefix.Trim()} {request.StartNumber + index}",
                    request.WarehouseId,
                    request.ParentLocationId,
                    request.Type,
                    barcode: null,
                    priority: index,
                    isPickable: request.IsPickable,
                    isReceivable: request.IsReceivable,
                    isCountable: request.IsCountable,
                    allowMixedItems: request.AllowMixedItems,
                    allowMixedLots: request.AllowMixedLots,
                    maxUnits: request.MaxUnits,
                    maxWeightKg: null,
                    maxVolumeCubicMeters: null,
                    maxPallets: null,
                    maxLpns: null,
                    storageProfile: request.StorageProfile);
                context.Locations.Add(location);
                created.Add(location);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationBulkGenerated,
                    WmsAuditEntityTypes.Location,
                    request.WarehouseId.ToString(CultureInfo.InvariantCulture),
                    request.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["count"] = created.Count,
                        ["firstCode"] = created[0].Code,
                        ["lastCode"] = created[^1].Code
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success<IReadOnlyList<LocationDto>>(created.Select(MapToDto).ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            context.ChangeTracker.Clear();
            if (exception is ArgumentException)
            {
                return Result.Failure<IReadOnlyList<LocationDto>>(WmsErrors.Validation(
                    "location.definition_invalid",
                    exception.Message));
            }

            logger.LogError(exception, "Could not generate locations for warehouse {WarehouseId}", request.WarehouseId);
            return Result.Failure<IReadOnlyList<LocationDto>>(WmsErrors.FromException(
                exception,
                "location.bulk_generation_failed",
                "Locations could not be generated."));
        }
    }

    public async Task<Result<LocationImportResult>> ImportAsync(
        int warehouseId,
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<LocationImportResult>();
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000)
        {
            return Result.Failure<LocationImportResult>(WmsErrors.Validation(
                "location.import_empty",
                "Provide a non-empty CSV import no larger than 2 MB."));
        }

        var parsed = ParseImport(csv);
        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<LocationImportResult>(WmsErrors.Validation(
                "location.import_invalid",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        if (parsed.Rows.Count is < 1 or > MaximumBulkRows)
        {
            return Result.Failure<LocationImportResult>(WmsErrors.Validation(
                "location.import_limit",
                $"Import must contain between 1 and {MaximumBulkRows} data rows."));
        }

        var existing = await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == warehouseId)
            .Select(location => new { location.Id, location.Code, location.Barcode })
            .ToListAsync(cancellationToken);
        var existingCodes = existing.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
        var existingBarcodes = existing
            .Where(item => item.Barcode != null)
            .Select(item => item.Barcode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedCodes = new HashSet<string>(StringComparer.Ordinal);
        var normalizedBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in parsed.Rows)
        {
            var code = NormalizeCode(row.Code);
            if (!normalizedCodes.Add(code) || existingCodes.Contains(code))
            {
                parsed.Errors.Add(new LocationImportError(row.RowNumber, $"Location code '{code}' already exists."));
            }

            if (!string.IsNullOrWhiteSpace(row.Barcode) &&
                (!normalizedBarcodes.Add(row.Barcode.Trim()) || existingBarcodes.Contains(row.Barcode.Trim())))
            {
                parsed.Errors.Add(new LocationImportError(row.RowNumber, "Location barcode already exists."));
            }
        }

        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<LocationImportResult>(WmsErrors.Validation(
                "location.import_conflict",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        var imported = 0;
        var pending = parsed.Rows.ToList();
        var knownCodes = new HashSet<string>(existingCodes, StringComparer.Ordinal);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            while (pending.Count > 0)
            {
                var progressed = false;
                foreach (var row in pending.ToArray())
                {
                    var parentCode = string.IsNullOrWhiteSpace(row.ParentCode)
                        ? null
                        : NormalizeCode(row.ParentCode);
                    if (parentCode is not null && !knownCodes.Contains(parentCode))
                    {
                        continue;
                    }

                    int? parentId = null;
                    if (parentCode is not null)
                    {
                        parentId = await context.Locations
                            .Where(location => location.WarehouseId == warehouseId && location.Code == parentCode)
                            .Select(location => (int?)location.Id)
                            .SingleAsync(cancellationToken);
                    }

                    var parentResult = await ValidateParentAsync(
                        warehouseId,
                        parentId,
                        null,
                        cancellationToken);
                    if (parentResult.IsFailure)
                    {
                        throw new InvalidOperationException(parentResult.Error);
                    }

                    var location = new Location(
                        NormalizeCode(row.Code),
                        row.Name,
                        warehouseId,
                        parentId,
                        row.Type,
                        row.Barcode,
                        row.Priority,
                        row.IsPickable,
                        row.IsReceivable,
                        row.IsCountable,
                        row.AllowMixedItems,
                        row.AllowMixedLots,
                        row.MaxUnits,
                        row.MaxWeightKg,
                        row.MaxVolumeCubicMeters,
                        row.MaxPallets,
                        row.MaxLpns,
                        row.StorageProfile);
                    context.Locations.Add(location);
                    await context.SaveChangesAsync(cancellationToken);
                    await auditWriter.RecordAsync(
                        new AuditRecord(
                            WmsAuditActions.LocationCreated,
                            WmsAuditEntityTypes.Location,
                            location.Id.ToString(CultureInfo.InvariantCulture),
                            warehouseId,
                            After: ToAuditValues(location),
                            ActorUserId: userId),
                        cancellationToken);
                    knownCodes.Add(location.Code);
                    pending.Remove(row);
                    imported++;
                    progressed = true;
                }

                if (!progressed)
                {
                    var unresolved = pending.Select(row => row.Code).ToArray();
                    throw new InvalidOperationException(
                        $"Import contains an unknown or cyclic parent reference: {string.Join(", ", unresolved)}.");
                }
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LocationBulkImported,
                    WmsAuditEntityTypes.Location,
                    warehouseId.ToString(CultureInfo.InvariantCulture),
                    warehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["count"] = imported
                    },
                    ActorUserId: userId),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success(new LocationImportResult(imported, []));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            context.ChangeTracker.Clear();
            if (exception is ArgumentException)
            {
                return Result.Failure<LocationImportResult>(WmsErrors.Validation(
                    "location.definition_invalid",
                    exception.Message));
            }

            logger.LogError(exception, "Could not import locations for warehouse {WarehouseId}", warehouseId);
            return Result.Failure<LocationImportResult>(WmsErrors.FromException(
                exception,
                "location.import_failed",
                "Locations could not be imported."));
        }
    }

    public async Task<Result<IReadOnlyList<LocationLabelDto>>> GetLabelsAsync(
        int warehouseId,
        IReadOnlyCollection<int> locationIds,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<LocationLabelDto>>();
        }

        var ids = locationIds.Distinct().Take(MaximumBulkRows).ToArray();
        if (ids.Length == 0)
        {
            return Result.Success<IReadOnlyList<LocationLabelDto>>([]);
        }

        var locations = await context.Locations
            .AsNoTracking()
            .Include(location => location.ParentLocation)
            .Where(location => location.WarehouseId == warehouseId && ids.Contains(location.Id))
            .OrderBy(location => location.Code)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<LocationLabelDto>>(locations
            .Select(location => new LocationLabelDto(
                location.Id,
                location.Code,
                location.Name,
                location.GetFullPath(),
                location.Barcode,
                location.Type,
                location.WarehouseId))
            .ToArray());
    }

    private async Task<Location?> LoadVisibleLocationAsync(int id, CancellationToken cancellationToken)
    {
        var location = await context.Locations
            .AsNoTracking()
            .Include(item => item.ParentLocation)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            location.WarehouseId,
            cancellationToken);
        return authorization.IsSuccess ? location : null;
    }

    private async Task<Result> ValidateParentAsync(
        int warehouseId,
        int? parentLocationId,
        int? currentLocationId,
        CancellationToken cancellationToken)
    {
        if (!parentLocationId.HasValue)
        {
            return Result.Success();
        }

        var visited = new HashSet<int>();
        if (currentLocationId.HasValue)
        {
            visited.Add(currentLocationId.Value);
        }

        var currentId = parentLocationId.Value;
        for (var depth = 0; depth < MaximumHierarchyDepth; depth++)
        {
            if (!visited.Add(currentId))
            {
                return Result.Failure(WmsErrors.Validation(
                    "location.hierarchy_cycle",
                    "A location cannot be its own ancestor."));
            }

            var parent = await context.Locations
                .AsNoTracking()
                .Where(location => location.Id == currentId)
                .Select(location => new
                {
                    location.Id,
                    location.WarehouseId,
                    location.ParentLocationId,
                    location.IsActive
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (parent is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "location.parent_not_found",
                    "The selected parent location was not found."));
            }

            if (parent.WarehouseId != warehouseId)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "location.parent_warehouse_mismatch",
                    "A parent location must belong to the same warehouse."));
            }

            if (!parent.IsActive)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "location.parent_inactive",
                    "A location cannot be placed under an inactive parent."));
            }

            if (!parent.ParentLocationId.HasValue)
            {
                return Result.Success();
            }

            currentId = parent.ParentLocationId.Value;
        }

        return Result.Failure(WmsErrors.Validation(
            "location.hierarchy_depth_exceeded",
            $"Location hierarchy cannot exceed {MaximumHierarchyDepth} levels."));
    }

    private async Task<LocationStockSnapshot> GetStockSnapshotAsync(
        int locationId,
        CancellationToken cancellationToken)
    {
        var stock = await context.Stock
            .AsNoTracking()
            .Where(item => item.LocationId == locationId)
            .Select(item => new
            {
                Available = item.QuantityAvailable.Value,
                Reserved = item.QuantityReserved.Value
            })
            .ToListAsync(cancellationToken);
        return new LocationStockSnapshot(
            stock.Sum(item => item.Available),
            stock.Sum(item => item.Reserved));
    }

    private async Task<string?> GetUnsafeDeactivationReasonAsync(
        Location location,
        CancellationToken cancellationToken)
    {
        var stock = await GetStockSnapshotAsync(location.Id, cancellationToken);
        if (stock.Units > 0 || stock.ReservedUnits > 0)
        {
            return "A location with stock or reservations cannot be deactivated.";
        }

        if (await context.Locations.AnyAsync(
                child => child.ParentLocationId == location.Id && child.IsActive,
                cancellationToken))
        {
            return "A location with active child locations cannot be deactivated.";
        }

        if (await context.WarehouseOperationalLocations.AnyAsync(
                reference => reference.LocationId == location.Id,
                cancellationToken))
        {
            return "A location referenced by warehouse operations cannot be deactivated.";
        }

        if (await HasRunningWarehouseJobsAsync(location.WarehouseId, cancellationToken))
        {
            return "A location cannot be deactivated while warehouse work is running.";
        }

        return null;
    }

    private Task<bool> HasRunningWarehouseJobsAsync(
        int warehouseId,
        CancellationToken cancellationToken) =>
        context.JobExecutions.AnyAsync(
            job => job.WarehouseId == warehouseId && job.Status == "Running",
            cancellationToken);

    private async Task<bool> CodeExistsAsync(
        int warehouseId,
        string code,
        int? excludedId,
        CancellationToken cancellationToken) =>
        await context.Locations.AnyAsync(
            location => location.WarehouseId == warehouseId &&
                        location.Code == code &&
                        (!excludedId.HasValue || location.Id != excludedId.Value),
            cancellationToken);

    private async Task<bool> BarcodeExistsAsync(
        int warehouseId,
        string? barcode,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return false;
        }

        var normalized = barcode.Trim();
        return await context.Locations.AnyAsync(
            location => location.WarehouseId == warehouseId &&
                        location.Barcode == normalized &&
                        (!excludedId.HasValue || location.Id != excludedId.Value),
            cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        return context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
    }

    private static IQueryable<Location> ApplyScope(
        IQueryable<Location> query,
        WarehouseAccessScope scope) =>
        scope.HasGlobalAccess
            ? query
            : query.Where(location => scope.WarehouseIds.Contains(location.WarehouseId));

    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length is < 1 or > 50)
        {
            throw new ArgumentException("Location code must contain 1-50 characters.", nameof(code));
        }

        return normalized;
    }

    private static Dictionary<string, object?> ToAuditValues(Location location) =>
        new(StringComparer.Ordinal)
        {
            ["code"] = location.Code,
            ["name"] = location.Name,
            ["warehouseId"] = location.WarehouseId,
            ["parentLocationId"] = location.ParentLocationId,
            ["type"] = location.Type.ToString(),
            ["barcode"] = location.Barcode,
            ["priority"] = location.Priority,
            ["isPickable"] = location.IsPickable,
            ["isReceivable"] = location.IsReceivable,
            ["isCountable"] = location.IsCountable,
            ["allowMixedItems"] = location.AllowMixedItems,
            ["allowMixedLots"] = location.AllowMixedLots,
            ["isActive"] = location.IsActive,
            ["maxUnits"] = location.MaxUnits,
            ["maxWeightKg"] = location.MaxWeightKg,
            ["maxVolumeCubicMeters"] = location.MaxVolumeCubicMeters,
            ["maxPallets"] = location.MaxPallets,
            ["maxLpns"] = location.MaxLpns,
            ["storageProfile"] = location.StorageProfile,
            ["minimumTemperatureCelsius"] = location.MinimumTemperatureCelsius,
            ["maximumTemperatureCelsius"] = location.MaximumTemperatureCelsius,
            ["hazardClass"] = location.HazardClass,
            ["accessRestriction"] = location.AccessRestriction
        };

    private static LocationDto MapToDto(Location location) =>
        new(
            location.Id,
            location.Code,
            location.Name,
            location.WarehouseId,
            location.ParentLocationId,
            location.IsPickable,
            location.IsReceivable,
            location.IsActive,
            location.Capacity,
            location.GetFullPath(),
            location.CreatedAt,
            location.UpdatedAt,
            location.Type,
            location.Barcode,
            location.Priority,
            location.IsCountable,
            location.AllowMixedItems,
            location.AllowMixedLots,
            location.MaxUnits,
            location.MaxWeightKg,
            location.MaxVolumeCubicMeters,
            location.MaxPallets,
            location.MaxLpns,
            location.StorageProfile,
            location.MinimumTemperatureCelsius,
            location.MaximumTemperatureCelsius,
            location.HazardClass,
            location.AccessRestriction,
            location.ConstraintAttributesJson);

    private static ParsedImport ParseImport(string csv)
    {
        var rows = new List<ImportedRow>();
        var errors = new List<LocationImportError>();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = lines.Length > 0 &&
                    string.Equals(ParseCsvLine(lines[0]).FirstOrDefault(), "CODE", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        for (var index = start; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            var fields = ParseCsvLine(lines[index]);
            if (fields.Count < 2)
            {
                errors.Add(new LocationImportError(rowNumber, "Expected at least code and name columns."));
                continue;
            }

            var typeValue = NullIfEmpty(fields.ElementAtOrDefault(3));
            if (typeValue is not null &&
                (!Enum.TryParse(typeValue, true, out LocationType type) || !Enum.IsDefined(type)))
            {
                errors.Add(new LocationImportError(rowNumber, "TYPE must be a supported location type."));
                continue;
            }

            var parsedType = typeValue is null
                ? LocationType.Storage
                : Enum.Parse<LocationType>(typeValue, ignoreCase: true);
            ValidateImportFields(fields, rowNumber, errors);

            rows.Add(new ImportedRow(
                rowNumber,
                fields[0],
                fields[1],
                NullIfEmpty(fields.ElementAtOrDefault(2)),
                parsedType,
                NullIfEmpty(fields.ElementAtOrDefault(4)),
                ParseInt(fields.ElementAtOrDefault(5), 0),
                ParseBool(fields.ElementAtOrDefault(6), true),
                ParseBool(fields.ElementAtOrDefault(7), true),
                ParseBool(fields.ElementAtOrDefault(8), true),
                ParseBool(fields.ElementAtOrDefault(9), true),
                ParseBool(fields.ElementAtOrDefault(10), true),
                ParseDecimal(fields.ElementAtOrDefault(11), 1_000),
                ParseDecimal(fields.ElementAtOrDefault(12), null),
                ParseDecimal(fields.ElementAtOrDefault(13), null),
                ParseNullableInt(fields.ElementAtOrDefault(14)),
                ParseNullableInt(fields.ElementAtOrDefault(15)),
                NullIfEmpty(fields.ElementAtOrDefault(16))));
        }

        return new ParsedImport(rows, errors);
    }

    private static void ValidateImportFields(
        List<string> fields,
        int rowNumber,
        List<LocationImportError> errors)
    {
        if (!string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(5)) &&
            !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            errors.Add(new LocationImportError(rowNumber, "PRIORITY must be an integer."));
        }

        for (var index = 6; index <= 10; index++)
        {
            if (!string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(index)) &&
                !bool.TryParse(fields[index], out _))
            {
                errors.Add(new LocationImportError(rowNumber, $"Column {index + 1} must be true or false."));
            }
        }

        for (var index = 11; index <= 13; index++)
        {
            if (!string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(index)) &&
                !decimal.TryParse(fields[index], NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                errors.Add(new LocationImportError(rowNumber, $"Column {index + 1} must be a decimal number."));
            }
        }

        for (var index = 14; index <= 15; index++)
        {
            if (!string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(index)) &&
                !int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                errors.Add(new LocationImportError(rowNumber, $"Column {index + 1} must be an integer."));
            }
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString().Trim());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString().Trim());
        return fields;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ParseBool(string? value, bool defaultValue) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static int ParseInt(string? value, int defaultValue) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static decimal? ParseDecimal(string? value, decimal? defaultValue) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private sealed record LocationStockSnapshot(decimal Units, decimal ReservedUnits);

    private sealed record ImportedRow(
        int RowNumber,
        string Code,
        string Name,
        string? ParentCode,
        LocationType Type,
        string? Barcode,
        int Priority,
        bool IsPickable,
        bool IsReceivable,
        bool IsCountable,
        bool AllowMixedItems,
        bool AllowMixedLots,
        decimal? MaxUnits,
        decimal? MaxWeightKg,
        decimal? MaxVolumeCubicMeters,
        int? MaxPallets,
        int? MaxLpns,
        string? StorageProfile);

    private sealed class ParsedImport(
        List<ImportedRow> rows,
        List<LocationImportError> errors)
    {
        public List<ImportedRow> Rows { get; } = rows;
        public List<LocationImportError> Errors { get; } = errors;
    }
}
