using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Application.Identification;
using Wms.Application.Identity;
using Wms.Application.Items;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Items;

public sealed class ItemManagementService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<ItemManagementService> logger,
    IIdentificationRegistry? identificationRegistry = null) : IItemManagementService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;
    private static readonly JsonSerializerOptions PackagingJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<Result<ItemCatalogCountsDto>> GetDashboardCountsAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemCatalogCountsDto>();
        }

        try
        {
            var counts = await context.Items
                .AsNoTracking()
                .GroupBy(item => item.IsActive)
                .Select(group => new
                {
                    IsActive = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            var activeItems = counts
                .Where(row => row.IsActive)
                .Select(row => row.Count)
                .SingleOrDefault();
            return Result.Success(new ItemCatalogCountsDto(
                counts.Sum(row => row.Count),
                activeItems));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Item dashboard counts could not be loaded");
            return Result.Failure<ItemCatalogCountsDto>(WmsErrors.FromException(
                exception,
                "items.dashboard_counts_failed",
                "Item dashboard counts could not be loaded. Please try again."));
        }
    }

    public async Task<Result<ItemPageDto>> ListAsync(
        ItemListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemPageDto>();
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var items = context.Items
            .AsNoTracking()
            .Include(item => item.Packagings)
            .AsQueryable();

        if (!request.IncludeInactive)
        {
            items = items.Where(item => item.IsActive);
        }

        if (request.Type.HasValue)
        {
            items = items.Where(item => item.Type == request.Type.Value);
        }

        if (request.LifecycleStatus.HasValue)
        {
            items = items.Where(item => item.LifecycleStatus == request.LifecycleStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            items = items.Where(item => item.Category == request.Category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            items = string.Equals(
                context.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal)
                ? items.Where(item =>
                    EF.Functions.ILike(item.Sku, pattern) ||
                    EF.Functions.ILike(item.Name, pattern) ||
                    EF.Functions.ILike(item.LocalizedName, pattern) ||
                    (item.Category != null && EF.Functions.ILike(item.Category, pattern)) ||
                    (item.Brand != null && EF.Functions.ILike(item.Brand, pattern)) ||
                    item.Barcodes.Any(barcode => EF.Functions.ILike(barcode.Value, pattern)))
                : items.Where(item =>
                    EF.Functions.Like(item.Sku, pattern) ||
                    EF.Functions.Like(item.Name, pattern) ||
                    EF.Functions.Like(item.LocalizedName, pattern) ||
                    (item.Category != null && EF.Functions.Like(item.Category, pattern)) ||
                    (item.Brand != null && EF.Functions.Like(item.Brand, pattern)) ||
                    item.Barcodes.Any(barcode => EF.Functions.Like(barcode.Value, pattern)));
        }

        var totalCount = await items.CountAsync(cancellationToken);
        var ordered = ApplyOrdering(items, request);
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new ItemPageDto(
            rows.Select(MapToDto).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<ItemDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemDto>();
        }

        var item = await LoadItemAsync(id, cancellationToken);
        return item is null
            ? Result.Failure<ItemDto>(WmsErrors.NotFound(
                "item.not_found",
                "The requested item was not found."))
            : Result.Success(MapToDto(item));
    }

    public async Task<Result<ItemDto>> CreateAsync(
        ItemCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemDto>();
        }

        try
        {
            var normalizedSku = NormalizeSku(request.Sku);
            if (await context.Items.AnyAsync(item => item.Sku == normalizedSku, cancellationToken))
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.sku_conflict",
                    $"Item SKU '{normalizedSku}' already exists."));
            }

            var barcodeValues = ValidateBarcodes(request.Barcodes);
            var packaging = ValidatePackaging(request.Packagings);
            var barcodeConflict = await FindBarcodeConflictAsync(barcodeValues, null, cancellationToken);
            if (barcodeConflict is not null)
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.barcode_conflict",
                    $"Barcode '{barcodeConflict}' is already assigned to another item."));
            }

            var packagingIdentifierConflict = await FindPackagingIdentifierConflictAsync(
                PackagingIdentifiers(packaging),
                null,
                cancellationToken);
            if (packagingIdentifierConflict is not null || HasBarcodeOverlap(
                    barcodeValues,
                    PackagingIdentifiers(packaging)))
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.barcode_conflict",
                    "Every item and packaging barcode and GTIN must be globally unique."));
            }

            var item = BuildItem(request.Sku, request.Commercial, request.Measurements,
                request.Tracking, request.Storage, request.Planning);
            AddBarcodes(item, barcodeValues);
            AddPackagings(item, packaging);
            await context.Items.AddAsync(item, cancellationToken);
            if (identificationRegistry is not null)
            {
                var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
                if (identifierSync.IsFailure)
                {
                    return identifierSync.ToFailure<ItemDto>();
                }
            }
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ItemCreated,
                    WmsAuditEntityTypes.Item,
                    normalizedSku,
                    After: AuditSnapshot(item),
                    ActorUserId: userId),
                cancellationToken);
            if (packaging.Count > 0)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.ItemPackagingsChanged,
                        WmsAuditEntityTypes.Item,
                        normalizedSku,
                        After: new Dictionary<string, object?>
                        {
                            ["packagings"] = packaging.Select(PackagingAuditSnapshot).ToArray()
                        },
                        ActorUserId: userId),
                    cancellationToken);
            }
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Item master created for {ItemSku} by {UserId}", item.Sku, userId);
            return Result.Success(MapToDto(item));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ItemDto>(WmsErrors.Validation(
                "item.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Item master creation failed for {ItemSku}", request.Sku);
            return Result.Failure<ItemDto>(WmsErrors.FromException(
                exception,
                "item.create_failed",
                "The item could not be created."));
        }
    }

    public async Task<Result<ItemDto>> UpdateAsync(
        ItemUpdateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemDto>();
        }

        try
        {
            var item = await LoadItemAsync(request.Id, cancellationToken);
            if (item is null)
            {
                return Result.Failure<ItemDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The requested item was not found."));
            }

            var hasTransactionalHistory = await HasTransactionalHistoryAsync(item.Id, cancellationToken);
            var trackingChanged = TrackingPolicyChanged(item, request.Tracking);
            if (trackingChanged && hasTransactionalHistory)
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.tracking_policy_locked",
                    "Lot, serial, expiry, and FEFO controls cannot be changed after stock or history exists."));
            }

            if (hasTransactionalHistory &&
                !string.Equals(
                    item.UnitOfMeasure,
                    request.Measurements.BaseUnit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.base_unit_locked",
                    "The canonical base unit cannot change after stock or transaction history exists."));
            }

            if (hasTransactionalHistory &&
                item.AllowFractionalQuantity != request.Tracking.AllowFractionalQuantity)
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.fraction_policy_locked",
                    "The fractional-quantity policy cannot change after stock or transaction history exists."));
            }

            var barcodeValues = ValidateBarcodes(request.Barcodes);
            var packaging = ValidatePackaging(request.Packagings);
            var barcodeConflict = await FindBarcodeConflictAsync(barcodeValues, item.Id, cancellationToken);
            if (barcodeConflict is not null)
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.barcode_conflict",
                    $"Barcode '{barcodeConflict}' is already assigned to another item."));
            }

            var packagingIdentifierConflict = await FindPackagingIdentifierConflictAsync(
                PackagingIdentifiers(packaging),
                item.Id,
                cancellationToken);
            if (packagingIdentifierConflict is not null ||
                HasBarcodeOverlap(
                    barcodeValues,
                    PackagingIdentifiers(packaging)))
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.barcode_conflict",
                    "Every item and packaging barcode and GTIN must be globally unique."));
            }

            var before = AuditSnapshot(item);
            var beforePackagings = item.Packagings.Select(PackagingAuditSnapshot).ToArray();
            item.UpdateMasterData(ToDetails(request.Commercial, request.Measurements, request.Tracking,
                request.Storage, request.Planning));
            ReplaceBarcodes(item, barcodeValues);
            ReplacePackagings(item, packaging);
            if (identificationRegistry is not null)
            {
                var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
                if (identifierSync.IsFailure)
                {
                    return identifierSync.ToFailure<ItemDto>();
                }
            }
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ItemUpdated,
                    WmsAuditEntityTypes.Item,
                    item.Sku,
                    Before: before,
                    After: AuditSnapshot(item),
                    ActorUserId: userId),
                cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ItemPackagingsChanged,
                    WmsAuditEntityTypes.Item,
                    item.Sku,
                    Before: new Dictionary<string, object?> { ["packagings"] = beforePackagings },
                    After: new Dictionary<string, object?>
                    {
                        ["packagings"] = item.Packagings.Select(PackagingAuditSnapshot).ToArray()
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Item master updated for {ItemSku} by {UserId}", item.Sku, userId);
            return Result.Success(MapToDto(item));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ItemDto>(WmsErrors.Validation(
                "item.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Item master update failed for {ItemId}", request.Id);
            return Result.Failure<ItemDto>(WmsErrors.FromException(
                exception,
                "item.update_failed",
                "The item could not be updated."));
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

        var item = await LoadItemAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(WmsErrors.NotFound("item.not_found", "The requested item was not found."));
        }

        var before = AuditSnapshot(item);
        if (active)
        {
            item.Activate();
        }
        else
        {
            item.Deactivate();
        }

        if (identificationRegistry is not null)
        {
            var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
            if (identifierSync.IsFailure)
            {
                return identifierSync;
            }
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                active ? WmsAuditActions.ItemActivated : WmsAuditActions.ItemDeactivated,
                WmsAuditEntityTypes.Item,
                item.Sku,
                Before: before,
                After: AuditSnapshot(item),
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
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var item = await LoadItemAsync(id, cancellationToken);
        if (item is null)
        {
            return Result.Failure(WmsErrors.NotFound("item.not_found", "The requested item was not found."));
        }

        if (await HasTransactionalHistoryAsync(item.Id, cancellationToken))
        {
            return Result.Failure(WmsErrors.Conflict(
                "item.history_exists",
                "The item cannot be deleted because stock, lots, or movement history references it. Deactivate it instead."));
        }

        item.Deactivate();
        if (identificationRegistry is not null)
        {
            var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
            if (identifierSync.IsFailure)
            {
                return identifierSync;
            }
        }

        context.Items.Remove(item);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ItemDeleted,
                WmsAuditEntityTypes.Item,
                item.Sku,
                Before: AuditSnapshot(item),
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<ItemDto>> DuplicateAsync(
        ItemDuplicateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemDto>();
        }

        try
        {
            var source = await LoadItemAsync(request.SourceItemId, cancellationToken);
            if (source is null)
            {
                return Result.Failure<ItemDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The source item was not found."));
            }

            var normalizedSku = NormalizeSku(request.Sku);
            if (await context.Items.AnyAsync(item => item.Sku == normalizedSku, cancellationToken))
            {
                return Result.Failure<ItemDto>(WmsErrors.Conflict(
                    "item.sku_conflict",
                    $"Item SKU '{normalizedSku}' already exists."));
            }

            var item = new Item(normalizedSku, request.Name ?? source.Name, source.UnitOfMeasure,
                source.RequiresLot, source.RequiresSerial);
            item.UpdateMasterData(new ItemMasterDetails(
                Name: item.Name,
                LocalizedName: source.LocalizedName,
                Description: source.Description,
                LocalizedDescription: source.LocalizedDescription,
                Category: source.Category,
                Brand: source.Brand,
                ImageReference: source.ImageReference,
                DocumentReference: source.DocumentReference,
                Type: source.Type,
                LifecycleStatus: ItemLifecycleStatus.Draft,
                PurchaseUnit: source.PurchaseUnit,
                SalesUnit: source.SalesUnit,
                NetWeightKg: source.NetWeightKg,
                LengthCm: source.LengthCm,
                WidthCm: source.WidthCm,
                HeightCm: source.HeightCm,
                VolumeCubicMeters: source.VolumeCubicMeters,
                StandardCost: source.StandardCost,
                SalesPrice: source.SalesPrice,
                CountryOfOrigin: source.CountryOfOrigin,
                CustomsCode: source.CustomsCode,
                RequiresExpiry: source.RequiresExpiry,
                UseFefo: source.UseFefo,
                QualityInspectionRequired: source.QualityInspectionRequired,
                IsHazardous: source.IsHazardous,
                TemperatureControlled: source.TemperatureControlled,
                SpecialHandlingRequired: source.SpecialHandlingRequired,
                MinimumTemperatureCelsius: source.MinimumTemperatureCelsius,
                MaximumTemperatureCelsius: source.MaximumTemperatureCelsius,
                StorageProfile: source.StorageProfile,
                PutawayProfile: source.PutawayProfile,
                DefaultSupplierCode: source.DefaultSupplierCode,
                ReorderPolicy: source.ReorderPolicy,
                MinimumStock: source.MinimumStock,
                MaximumStock: source.MaximumStock,
                SafetyStock: source.SafetyStock,
                LeadTimeDays: source.LeadTimeDays,
                ShelfLifeDays: source.ShelfLifeDays,
                RequiresLot: source.RequiresLot,
                RequiresSerial: source.RequiresSerial,
                AllowFractionalQuantity: source.AllowFractionalQuantity));

            if (request.CopyPackagings)
            {
                foreach (var packaging in source.Packagings)
                {
                    item.AddPackaging(new ItemPackaging(
                        packaging.Code,
                        packaging.UnitOfMeasure,
                        packaging.UnitsPerPackage,
                        barcode: null,
                        grossWeightKg: packaging.GrossWeightKg,
                        lengthCm: packaging.LengthCm,
                        widthCm: packaging.WidthCm,
                        heightCm: packaging.HeightCm,
                        isDefault: packaging.IsDefault,
                        name: packaging.Name,
                        localizedName: packaging.LocalizedName,
                        gtin: null,
                        parentPackagingCode: packaging.ParentPackagingCode,
                        type: packaging.Type,
                        partialPackagePolicy: packaging.PartialPackagePolicy,
                        isDefaultReceiving: packaging.IsDefaultReceiving,
                        isDefaultStorage: packaging.IsDefaultStorage,
                        isDefaultPicking: packaging.IsDefaultPicking,
                        isDefaultShipping: packaging.IsDefaultShipping,
                        isActive: packaging.IsActive,
                        version: packaging.Version));
                }
            }

            foreach (var conversion in source.UnitConversions.Where(conversion => conversion.IsActive))
            {
                item.AddUnitConversion(new ItemUnitConversion(
                    item.Id,
                    conversion.FromUnitOfMeasure,
                    conversion.ToUnitOfMeasure,
                    conversion.ConversionFactor,
                    conversion.ResultPrecision,
                    conversion.RoundingMode));
            }

            await context.Items.AddAsync(item, cancellationToken);
            if (identificationRegistry is not null)
            {
                var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
                if (identifierSync.IsFailure)
                {
                    return identifierSync.ToFailure<ItemDto>();
                }
            }
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ItemDuplicated,
                    WmsAuditEntityTypes.Item,
                    item.Sku,
                    After: new Dictionary<string, object?>
                    {
                        ["sourceItemId"] = source.Id,
                        ["sourceSku"] = source.Sku,
                        ["sku"] = item.Sku,
                        ["lifecycleStatus"] = item.LifecycleStatus.ToString()
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(item));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ItemDto>(WmsErrors.Validation("item.definition_invalid", exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Item duplication failed for {ItemId}", request.SourceItemId);
            return Result.Failure<ItemDto>(WmsErrors.FromException(
                exception,
                "item.duplicate_failed",
                "The item could not be duplicated."));
        }
    }

    public async Task<Result<ItemImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsManage, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ItemImportResult>();
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000)
        {
            return Result.Failure<ItemImportResult>(WmsErrors.Validation(
                "item.import_empty",
                "Provide a non-empty CSV import no larger than 2 MB."));
        }

        var parsed = ParseImport(csv);
        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<ItemImportResult>(WmsErrors.Validation(
                "item.import_invalid",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        if (parsed.Rows.Count is < 1 or > MaximumImportRows)
        {
            return Result.Failure<ItemImportResult>(WmsErrors.Validation(
                "item.import_limit",
                $"Import must contain between 1 and {MaximumImportRows} data rows."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existingSkus = await context.Items
                .AsNoTracking()
                .Where(item => parsed.Rows.Select(row => row.Sku).Contains(item.Sku))
                .Select(item => item.Sku)
                .ToHashSetAsync(cancellationToken);
            if (existingSkus.Count > 0)
            {
                return Result.Failure<ItemImportResult>(WmsErrors.Conflict(
                    "item.sku_conflict",
                    $"These SKUs already exist: {string.Join(", ", existingSkus.OrderBy(value => value))}."));
            }

            var items = new List<Item>();
            var importedPackagingIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in parsed.Rows)
            {
                if (!existingSkus.Add(row.Sku))
                {
                    return Result.Failure<ItemImportResult>(WmsErrors.Conflict(
                        "item.sku_conflict",
                        $"SKU '{row.Sku}' appears more than once in the import."));
                }

                var item = BuildItem(
                    row.Sku,
                    new ItemCommercialRequest(row.Name, Category: row.Category, Brand: row.Brand, Type: row.Type,
                        LifecycleStatus: row.Status),
                    new ItemMeasurementRequest(row.BaseUnit, row.PurchaseUnit, row.SalesUnit,
                        StandardCost: row.StandardCost),
                    new ItemTrackingRequest(row.RequiresLot, row.RequiresSerial, row.RequiresExpiry,
                        row.ShelfLifeDays, row.UseFefo),
                    new ItemStorageRequest(row.StorageProfile, row.PutawayProfile, row.DefaultSupplierCode),
                    new ItemPlanningRequest(ItemReorderPolicy.None, row.MinimumStock, row.MaximumStock,
                        row.SafetyStock, row.LeadTimeDays));
                var packaging = ValidatePackaging(row.Packagings);
                var existingPackagingIdentifier = await FindPackagingIdentifierConflictAsync(
                    PackagingIdentifiers(packaging),
                    null,
                    cancellationToken);
                if (existingPackagingIdentifier is not null)
                {
                    return Result.Failure<ItemImportResult>(WmsErrors.Conflict(
                        "item.barcode_conflict",
                        $"Packaging identifier '{existingPackagingIdentifier}' is already assigned."));
                }

                foreach (var identifier in PackagingIdentifiers(packaging))
                {
                    if (!importedPackagingIdentifiers.Add(identifier))
                    {
                        return Result.Failure<ItemImportResult>(WmsErrors.Conflict(
                            "item.barcode_conflict",
                            $"Packaging identifier '{identifier}' appears more than once in the import."));
                    }
                }

                AddPackagings(item, packaging);
                items.Add(item);
                await context.Items.AddAsync(item, cancellationToken);
                if (identificationRegistry is not null)
                {
                    var identifierSync = await identificationRegistry.SyncItemAsync(item, cancellationToken);
                    if (identifierSync.IsFailure)
                    {
                        return identifierSync.ToFailure<ItemImportResult>();
                    }
                }
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.ItemBulkImported,
                        WmsAuditEntityTypes.Item,
                        row.Sku,
                        After: AuditSnapshot(item),
                        ActorUserId: userId),
                    cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Success(new ItemImportResult(items.Count, []));
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<ItemImportResult>(WmsErrors.Validation("item.definition_invalid", exception.Message));
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Item import failed");
            return Result.Failure<ItemImportResult>(WmsErrors.FromException(
                exception,
                "item.import_failed",
                "The item import could not be completed."));
        }
    }

    public async Task<Result<string>> ExportAsync(
        ItemListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(WmsPermissions.ItemsRead, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var items = await QueryItems(request)
            .OrderBy(item => item.Sku)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("SKU,NAME,LOCALIZED_NAME,CATEGORY,BRAND,TYPE,STATUS,BASE_UNIT,PURCHASE_UNIT,SALES_UNIT,REQUIRES_LOT,REQUIRES_SERIAL,REQUIRES_EXPIRY,SHELF_LIFE_DAYS,USE_FEFO,STANDARD_COST,MINIMUM_STOCK,MAXIMUM_STOCK,SAFETY_STOCK,LEAD_TIME_DAYS,STORAGE_PROFILE,PUTAWAY_PROFILE,DEFAULT_SUPPLIER_CODE,PACKAGINGS_JSON");
        foreach (var item in items)
        {
            builder.AppendLine(string.Join(",", [
                EscapeCsv(item.Sku),
                EscapeCsv(item.Name),
                EscapeCsv(item.LocalizedName),
                EscapeCsv(item.Category),
                EscapeCsv(item.Brand),
                EscapeCsv(item.Type.ToString()),
                EscapeCsv(item.LifecycleStatus.ToString()),
                EscapeCsv(item.UnitOfMeasure),
                EscapeCsv(item.PurchaseUnit),
                EscapeCsv(item.SalesUnit),
                item.RequiresLot.ToString(),
                item.RequiresSerial.ToString(),
                item.RequiresExpiry.ToString(),
                item.ShelfLifeDays.ToString(CultureInfo.InvariantCulture),
                item.UseFefo.ToString(),
                item.StandardCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.MinimumStock?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.MaximumStock?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.SafetyStock?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                item.LeadTimeDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                EscapeCsv(item.StorageProfile),
                EscapeCsv(item.PutawayProfile),
                EscapeCsv(item.DefaultSupplierCode),
                EscapeCsv(JsonSerializer.Serialize(
                    item.Packagings.Select(ToPackagingJson).ToArray(),
                    PackagingJsonOptions))
            ]));
        }

        return Result.Success(builder.ToString());
    }

    private async Task<Result> AuthorizeAsync(string permission, CancellationToken cancellationToken)
    {
        return await warehouseAccessService.AuthorizeAsync(permission, cancellationToken: cancellationToken);
    }

    private async Task<Item?> LoadItemAsync(int id, CancellationToken cancellationToken)
    {
        return await context.Items
            .Include(item => item.Packagings)
            .Include(item => item.UnitConversions)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    private IQueryable<Item> QueryItems(ItemListQuery request)
    {
        var items = context.Items
            .AsNoTracking()
            .Include(item => item.Packagings)
            .AsQueryable();
        if (!request.IncludeInactive)
        {
            items = items.Where(item => item.IsActive);
        }

        if (request.Type.HasValue)
        {
            items = items.Where(item => item.Type == request.Type.Value);
        }

        if (request.LifecycleStatus.HasValue)
        {
            items = items.Where(item => item.LifecycleStatus == request.LifecycleStatus.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            items = items.Where(item => item.Category == request.Category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            items = string.Equals(context.Database.ProviderName, PostgreSqlProviderName, StringComparison.Ordinal)
                ? items.Where(item => EF.Functions.ILike(item.Sku, pattern) || EF.Functions.ILike(item.Name, pattern))
                : items.Where(item => EF.Functions.Like(item.Sku, pattern) || EF.Functions.Like(item.Name, pattern));
        }

        return items;
    }

    private static IOrderedQueryable<Item> ApplyOrdering(IQueryable<Item> items, ItemListQuery request)
    {
        return request.SortBy switch
        {
            ItemSortField.Name when request.Descending => items.OrderByDescending(item => item.Name),
            ItemSortField.Name => items.OrderBy(item => item.Name),
            ItemSortField.Category when request.Descending => items.OrderByDescending(item => item.Category),
            ItemSortField.Category => items.OrderBy(item => item.Category),
            ItemSortField.UpdatedAt when request.Descending => items.OrderByDescending(item => item.UpdatedAt),
            ItemSortField.UpdatedAt => items.OrderBy(item => item.UpdatedAt),
            ItemSortField.CreatedAt when request.Descending => items.OrderByDescending(item => item.CreatedAt),
            ItemSortField.CreatedAt => items.OrderBy(item => item.CreatedAt),
            ItemSortField.Sku when request.Descending => items.OrderByDescending(item => item.Sku),
            _ => items.OrderBy(item => item.Sku)
        };
    }

    private static Item BuildItem(
        string sku,
        ItemCommercialRequest commercial,
        ItemMeasurementRequest measurements,
        ItemTrackingRequest tracking,
        ItemStorageRequest storage,
        ItemPlanningRequest planning)
    {
        var item = new Item(sku, commercial.Name, measurements.BaseUnit,
            tracking.RequiresLot, tracking.RequiresSerial);
        item.UpdateMasterData(ToDetails(commercial, measurements, tracking, storage, planning));
        return item;
    }

    private static ItemMasterDetails ToDetails(
        ItemCommercialRequest commercial,
        ItemMeasurementRequest measurements,
        ItemTrackingRequest tracking,
        ItemStorageRequest storage,
        ItemPlanningRequest planning) =>
        new(
            Name: commercial.Name,
            LocalizedName: commercial.LocalizedName,
            Description: commercial.Description,
            LocalizedDescription: commercial.LocalizedDescription,
            Category: commercial.Category,
            Brand: commercial.Brand,
            ImageReference: commercial.ImageReference,
            DocumentReference: commercial.DocumentReference,
            Type: commercial.Type,
            LifecycleStatus: commercial.LifecycleStatus,
            PurchaseUnit: measurements.PurchaseUnit,
            SalesUnit: measurements.SalesUnit,
            NetWeightKg: measurements.NetWeightKg,
            LengthCm: measurements.LengthCm,
            WidthCm: measurements.WidthCm,
            HeightCm: measurements.HeightCm,
            VolumeCubicMeters: measurements.VolumeCubicMeters,
            StandardCost: measurements.StandardCost,
            SalesPrice: measurements.SalesPrice,
            CountryOfOrigin: measurements.CountryOfOrigin,
            CustomsCode: measurements.CustomsCode,
            RequiresExpiry: tracking.RequiresExpiry,
            UseFefo: tracking.UseFefo,
            QualityInspectionRequired: tracking.QualityInspectionRequired,
            IsHazardous: tracking.IsHazardous,
            TemperatureControlled: tracking.TemperatureControlled,
            SpecialHandlingRequired: tracking.SpecialHandlingRequired,
            MinimumTemperatureCelsius: tracking.MinimumTemperatureCelsius,
            MaximumTemperatureCelsius: tracking.MaximumTemperatureCelsius,
            StorageProfile: storage.StorageProfile,
            PutawayProfile: storage.PutawayProfile,
            DefaultSupplierCode: storage.DefaultSupplierCode,
            ReorderPolicy: planning.ReorderPolicy,
            MinimumStock: planning.MinimumStock,
            MaximumStock: planning.MaximumStock,
            SafetyStock: planning.SafetyStock,
            LeadTimeDays: planning.LeadTimeDays,
            ShelfLifeDays: tracking.ShelfLifeDays,
            RequiresLot: tracking.RequiresLot,
            RequiresSerial: tracking.RequiresSerial,
            AllowFractionalQuantity: tracking.AllowFractionalQuantity);

    private static bool TrackingPolicyChanged(Item item, ItemTrackingRequest request) =>
        item.RequiresLot != request.RequiresLot ||
        item.RequiresSerial != request.RequiresSerial ||
        item.AllowFractionalQuantity != request.AllowFractionalQuantity ||
        item.RequiresExpiry != request.RequiresExpiry ||
        item.UseFefo != request.UseFefo ||
        item.ShelfLifeDays != request.ShelfLifeDays;

    private async Task<bool> HasTransactionalHistoryAsync(int itemId, CancellationToken cancellationToken)
    {
        return await context.Stock.AnyAsync(stock => stock.ItemId == itemId, cancellationToken) ||
               await context.Lots.AnyAsync(lot => lot.ItemId == itemId, cancellationToken) ||
               await context.Movements.AnyAsync(movement => movement.ItemId == itemId, cancellationToken);
    }

    private static List<string> ValidateBarcodes(IReadOnlyList<string>? values)
    {
        var normalized = new List<string>();
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var barcode = new Barcode(value).Value;
            if (!normalized.Contains(barcode, StringComparer.Ordinal))
            {
                normalized.Add(barcode);
            }
        }

        return normalized;
    }

    private static List<ItemPackaging> ValidatePackaging(
        IReadOnlyList<ItemPackagingRequest>? values)
    {
        var packaging = new List<ItemPackaging>();
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var current = new ItemPackaging(
                value.Code,
                value.UnitOfMeasure,
                value.UnitsPerPackage,
                value.Barcode,
                value.GrossWeightKg,
                value.LengthCm,
                value.WidthCm,
                value.HeightCm,
                value.IsDefault,
                value.Name,
                value.LocalizedName,
                value.Gtin,
                value.ParentPackagingCode,
                value.Type,
                value.PartialPackagePolicy,
                value.IsDefaultReceiving,
                value.IsDefaultStorage,
                value.IsDefaultPicking,
                value.IsDefaultShipping,
                value.IsActive);

            ValidateDefault(defaults, nameof(ItemPackaging.IsDefaultReceiving), current.IsDefaultReceiving, current.Code);
            ValidateDefault(defaults, nameof(ItemPackaging.IsDefaultStorage), current.IsDefaultStorage, current.Code);
            ValidateDefault(defaults, nameof(ItemPackaging.IsDefaultPicking), current.IsDefaultPicking, current.Code);
            ValidateDefault(defaults, nameof(ItemPackaging.IsDefaultShipping), current.IsDefaultShipping, current.Code);

            foreach (var identifier in new[] { current.Barcode, current.Gtin }.Where(value => value is not null))
            {
                if (!identifiers.Add(identifier!))
                {
                    throw new ArgumentException(
                        $"Packaging identifier '{identifier}' appears more than once.");
                }
            }

            if (packaging.Any(item => string.Equals(item.Code, current.Code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Packaging code '{current.Code}' appears more than once.");
            }

            packaging.Add(current);
        }

        var byCode = packaging.ToDictionary(value => value.Code, StringComparer.Ordinal);
        foreach (var value in packaging)
        {
            value.ValidateNesting(byCode);
        }

        return packaging;
    }

    private static void ValidateDefault(
        Dictionary<string, string> defaults,
        string role,
        bool isDefault,
        string code)
    {
        if (!isDefault)
        {
            return;
        }

        if (defaults.TryGetValue(role, out var existing) && !string.IsNullOrEmpty(existing))
        {
            throw new ArgumentException(
                $"Only one packaging definition can be the default for {role.Replace("IsDefault", string.Empty, StringComparison.Ordinal)}.");
        }

        defaults[role] = code;
    }

    private static string[] PackagingIdentifiers(
        IEnumerable<ItemPackaging> packaging) =>
        packaging
            .SelectMany(value => new[] { value.Barcode, value.Gtin })
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static bool HasBarcodeOverlap(
        IEnumerable<string> itemBarcodes,
        IEnumerable<string> packagingBarcodes) =>
        itemBarcodes.ToHashSet(StringComparer.Ordinal)
            .Overlaps(packagingBarcodes);

    private static void AddBarcodes(Item item, IReadOnlyList<string> barcodes)
    {
        foreach (var barcode in barcodes)
        {
            item.AddBarcode(new Barcode(barcode));
        }
    }

    private static void AddPackagings(Item item, IReadOnlyList<ItemPackaging> packaging)
    {
        foreach (var value in packaging)
        {
            item.AddPackaging(value);
        }
    }

    private static void ReplaceBarcodes(Item item, IReadOnlyList<string> values)
    {
        foreach (var barcode in item.Barcodes.ToArray())
        {
            item.RemoveBarcode(barcode);
        }

        AddBarcodes(item, values);
    }

    private void ReplacePackagings(Item item, IReadOnlyList<ItemPackaging> values)
    {
        var requestedCodes = values
            .Select(value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var existing in item.Packagings.ToArray())
        {
            if (requestedCodes.Contains(existing.Code))
            {
                continue;
            }

            context.ItemPackagings.Remove(existing);
            item.RemovePackaging(existing);
        }

        foreach (var value in values)
        {
            var existing = item.Packagings.FirstOrDefault(packaging =>
                string.Equals(packaging.Code, value.Code, StringComparison.Ordinal));
            if (existing is null)
            {
                item.AddPackaging(value);
                continue;
            }

            existing.Update(
                value.Code,
                value.UnitOfMeasure,
                value.UnitsPerPackage,
                value.Barcode,
                value.GrossWeightKg,
                value.LengthCm,
                value.WidthCm,
                value.HeightCm,
                value.IsDefault,
                value.Name,
                value.LocalizedName,
                value.Gtin,
                value.ParentPackagingCode,
                value.Type,
                value.PartialPackagePolicy,
                value.IsDefaultReceiving,
                value.IsDefaultStorage,
                value.IsDefaultPicking,
                value.IsDefaultShipping,
                value.IsActive);
        }
    }

    private async Task<string?> FindBarcodeConflictAsync(
        List<string> barcodes,
        int? excludedItemId,
        CancellationToken cancellationToken)
    {
        if (barcodes.Count == 0)
        {
            return null;
        }

        return await context.Items
            .AsNoTracking()
            .Where(item => (!excludedItemId.HasValue || item.Id != excludedItemId.Value) &&
                          item.Barcodes.Any(barcode => barcodes.Contains(barcode.Value)))
            .SelectMany(item => item.Barcodes)
            .Where(barcode => barcodes.Contains(barcode.Value))
            .Select(barcode => barcode.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> FindPackagingIdentifierConflictAsync(
        IEnumerable<string> identifiers,
        int? excludedItemId,
        CancellationToken cancellationToken)
    {
        var values = identifiers.Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        var packagingConflict = await context.ItemPackagings
            .AsNoTracking()
            .Where(packaging => (!excludedItemId.HasValue || packaging.ItemId != excludedItemId.Value) &&
                                ((packaging.Barcode != null && values.Contains(packaging.Barcode)) ||
                                 (packaging.Gtin != null && values.Contains(packaging.Gtin))))
            .Select(packaging => packaging.Barcode ?? packaging.Gtin)
            .FirstOrDefaultAsync(cancellationToken);
        if (packagingConflict is not null)
        {
            return packagingConflict;
        }

        return await context.Items
            .AsNoTracking()
            .Where(item => (!excludedItemId.HasValue || item.Id != excludedItemId.Value) &&
                           item.Barcodes.Any(barcode => values.Contains(barcode.Value)))
            .SelectMany(item => item.Barcodes)
            .Where(barcode => values.Contains(barcode.Value))
            .Select(barcode => barcode.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Dictionary<string, object?> AuditSnapshot(Item item) => new()
    {
        ["sku"] = item.Sku,
        ["name"] = item.Name,
        ["type"] = item.Type.ToString(),
        ["lifecycleStatus"] = item.LifecycleStatus.ToString(),
        ["isActive"] = item.IsActive,
        ["requiresLot"] = item.RequiresLot,
        ["requiresSerial"] = item.RequiresSerial,
        ["allowFractionalQuantity"] = item.AllowFractionalQuantity,
        ["requiresExpiry"] = item.RequiresExpiry,
        ["shelfLifeDays"] = item.ShelfLifeDays,
        ["barcodeCount"] = item.Barcodes.Count,
        ["packagingCount"] = item.Packagings.Count,
        ["packagings"] = item.Packagings.Select(PackagingAuditSnapshot).ToArray(),
        ["storageProfile"] = item.StorageProfile
    };

    private static Dictionary<string, object?> PackagingAuditSnapshot(ItemPackaging packaging) => new()
    {
        ["id"] = packaging.Id,
        ["code"] = packaging.Code,
        ["name"] = packaging.Name,
        ["localizedName"] = packaging.LocalizedName,
        ["type"] = packaging.Type.ToString(),
        ["unitOfMeasure"] = packaging.UnitOfMeasure,
        ["unitsPerPackage"] = packaging.UnitsPerPackage,
        ["parentPackagingCode"] = packaging.ParentPackagingCode,
        ["barcode"] = packaging.Barcode,
        ["gtin"] = packaging.Gtin,
        ["partialPackagePolicy"] = packaging.PartialPackagePolicy.ToString(),
        ["isDefaultReceiving"] = packaging.IsDefaultReceiving,
        ["isDefaultStorage"] = packaging.IsDefaultStorage,
        ["isDefaultPicking"] = packaging.IsDefaultPicking,
        ["isDefaultShipping"] = packaging.IsDefaultShipping,
        ["isActive"] = packaging.IsActive,
        ["version"] = packaging.Version,
        ["grossWeightKg"] = packaging.GrossWeightKg,
        ["volumeCubicMeters"] = packaging.VolumeCubicMeters
    };

    private static ItemDto MapToDto(Item item) =>
        new(
            item.Id,
            item.Sku,
            item.Name,
            item.Description,
            item.UnitOfMeasure,
            item.IsActive,
            item.RequiresLot,
            item.RequiresSerial,
            item.ShelfLifeDays,
            item.Barcodes.Select(barcode => barcode.Value).ToList(),
            item.CreatedAt,
            item.UpdatedAt)
        {
            LocalizedName = item.LocalizedName,
            LocalizedDescription = item.LocalizedDescription,
            Category = item.Category,
            Brand = item.Brand,
            Type = item.Type,
            LifecycleStatus = item.LifecycleStatus,
            ImageReference = item.ImageReference,
            DocumentReference = item.DocumentReference,
            PurchaseUnit = item.PurchaseUnit,
            SalesUnit = item.SalesUnit,
            RequiresExpiry = item.RequiresExpiry,
            AllowFractionalQuantity = item.AllowFractionalQuantity,
            UseFefo = item.UseFefo,
            QualityInspectionRequired = item.QualityInspectionRequired,
            IsHazardous = item.IsHazardous,
            TemperatureControlled = item.TemperatureControlled,
            SpecialHandlingRequired = item.SpecialHandlingRequired,
            NetWeightKg = item.NetWeightKg,
            LengthCm = item.LengthCm,
            WidthCm = item.WidthCm,
            HeightCm = item.HeightCm,
            VolumeCubicMeters = item.VolumeCubicMeters,
            StandardCost = item.StandardCost,
            SalesPrice = item.SalesPrice,
            CountryOfOrigin = item.CountryOfOrigin,
            CustomsCode = item.CustomsCode,
            MinimumTemperatureCelsius = item.MinimumTemperatureCelsius,
            MaximumTemperatureCelsius = item.MaximumTemperatureCelsius,
            StorageProfile = item.StorageProfile,
            PutawayProfile = item.PutawayProfile,
            DefaultSupplierCode = item.DefaultSupplierCode,
            ReorderPolicy = item.ReorderPolicy,
            MinimumStock = item.MinimumStock,
            MaximumStock = item.MaximumStock,
            SafetyStock = item.SafetyStock,
            LeadTimeDays = item.LeadTimeDays,
            Packagings = item.Packagings.Select(packaging => new ItemPackagingDto(
                packaging.Id,
                packaging.Code,
                packaging.UnitOfMeasure,
                packaging.UnitsPerPackage,
                packaging.Barcode,
                packaging.GrossWeightKg,
                packaging.LengthCm,
                packaging.WidthCm,
                packaging.HeightCm,
                packaging.IsDefault)
            {
                Name = packaging.Name,
                LocalizedName = packaging.LocalizedName,
                Gtin = packaging.Gtin,
                ParentPackagingCode = packaging.ParentPackagingCode,
                Type = packaging.Type,
                PartialPackagePolicy = packaging.PartialPackagePolicy,
                VolumeCubicMeters = packaging.VolumeCubicMeters,
                IsDefaultReceiving = packaging.IsDefaultReceiving,
                IsDefaultStorage = packaging.IsDefaultStorage,
                IsDefaultPicking = packaging.IsDefaultPicking,
                IsDefaultShipping = packaging.IsDefaultShipping,
                IsActive = packaging.IsActive,
                Version = packaging.Version
            }).ToArray()
        };

    private static string NormalizeSku(string sku) =>
        new Item(sku, "Validation", "EA").Sku;

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',', StringComparison.Ordinal) ||
               value.Contains('"', StringComparison.Ordinal) ||
               value.Contains('\n', StringComparison.Ordinal) ||
               value.Contains('\r', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static ParsedImport ParseImport(string csv)
    {
        var rows = new List<ImportedRow>();
        var errors = new List<ItemImportError>();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = lines.Length > 0 &&
                    string.Equals(ParseCsvLine(lines[0]).FirstOrDefault(), "SKU", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        for (var index = start; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            var fields = ParseCsvLine(lines[index]);
            if (fields.Count < 3)
            {
                errors.Add(new ItemImportError(rowNumber, "Expected at least SKU, name, and base unit columns."));
                continue;
            }

            try
            {
                var type = ParseEnum(fields.ElementAtOrDefault(5), ItemType.Stock);
                var status = ParseEnum(fields.ElementAtOrDefault(6), ItemLifecycleStatus.Active);
                var requiresLot = ParseBool(fields.ElementAtOrDefault(9), false);
                var requiresSerial = ParseBool(fields.ElementAtOrDefault(10), false);
                var requiresExpiry = ParseBool(fields.ElementAtOrDefault(11), false);
                var useFefo = ParseBool(fields.ElementAtOrDefault(13), false);
                rows.Add(new ImportedRow(
                    NormalizeSku(fields[0]),
                    fields[1],
                    NullIfEmpty(fields.ElementAtOrDefault(2)) ?? "EA",
                    NullIfEmpty(fields.ElementAtOrDefault(3)),
                    NullIfEmpty(fields.ElementAtOrDefault(4)),
                    type,
                    status,
                    NullIfEmpty(fields.ElementAtOrDefault(7)),
                    NullIfEmpty(fields.ElementAtOrDefault(8)),
                    requiresLot,
                    requiresSerial,
                    requiresExpiry,
                    ParseInt(fields.ElementAtOrDefault(12), 0),
                    useFefo,
                    ParseDecimal(fields.ElementAtOrDefault(14)),
                    ParseDecimal(fields.ElementAtOrDefault(15)),
                    ParseDecimal(fields.ElementAtOrDefault(16)),
                    ParseDecimal(fields.ElementAtOrDefault(17)),
                    ParseNullableInt(fields.ElementAtOrDefault(18)),
                    NullIfEmpty(fields.ElementAtOrDefault(19)),
                    NullIfEmpty(fields.ElementAtOrDefault(20)),
                    NullIfEmpty(fields.ElementAtOrDefault(21)),
                    ParsePackagingJson(fields.ElementAtOrDefault(23))));
            }
            catch (ArgumentException exception)
            {
                errors.Add(new ItemImportError(rowNumber, exception.Message));
            }
        }

        return new ParsedImport(rows, errors);
    }

    private static ItemPackagingRequest[] ParsePackagingJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            var rows = JsonSerializer.Deserialize<List<ImportedPackaging>>(
                value,
                PackagingJsonOptions) ?? [];
            return rows.Select(row => new ItemPackagingRequest(
                row.Code ?? string.Empty,
                row.UnitOfMeasure ?? string.Empty,
                row.UnitsPerPackage,
                row.Barcode,
                row.GrossWeightKg,
                row.LengthCm,
                row.WidthCm,
                row.HeightCm,
                row.IsDefault,
                row.Name,
                row.LocalizedName,
                row.Gtin,
                row.ParentPackagingCode,
                row.Type,
                row.PartialPackagePolicy,
                row.IsDefaultReceiving,
                row.IsDefaultStorage,
                row.IsDefaultPicking,
                row.IsDefaultShipping,
                row.IsActive)).ToArray();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                $"PACKAGINGS_JSON is not valid JSON: {exception.Message}",
                nameof(value),
                exception);
        }
    }

    private static ImportedPackaging ToPackagingJson(ItemPackaging packaging) => new()
    {
        Code = packaging.Code,
        UnitOfMeasure = packaging.UnitOfMeasure,
        UnitsPerPackage = packaging.UnitsPerPackage,
        Barcode = packaging.Barcode,
        GrossWeightKg = packaging.GrossWeightKg,
        LengthCm = packaging.LengthCm,
        WidthCm = packaging.WidthCm,
        HeightCm = packaging.HeightCm,
        IsDefault = packaging.IsDefault,
        Name = packaging.Name,
        LocalizedName = packaging.LocalizedName,
        Gtin = packaging.Gtin,
        ParentPackagingCode = packaging.ParentPackagingCode,
        Type = packaging.Type,
        PartialPackagePolicy = packaging.PartialPackagePolicy,
        IsDefaultReceiving = packaging.IsDefaultReceiving,
        IsDefaultStorage = packaging.IsDefaultStorage,
        IsDefaultPicking = packaging.IsDefaultPicking,
        IsDefaultShipping = packaging.IsDefaultShipping,
        IsActive = packaging.IsActive
    };

    private static T ParseEnum<T>(string? value, T defaultValue) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!Enum.TryParse<T>(value, true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException($"'{value}' is not a supported {typeof(T).Name} value.");
        }

        return parsed;
    }

    private static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"'{value}' must be true or false.");
        }

        return parsed;
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"'{value}' must be an integer.");
        }

        return parsed;
    }

    private static int? ParseNullableInt(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseInt(value, 0);

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"'{value}' must be a decimal number.");
        }

        return parsed;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
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

    private sealed record ImportedRow(
        string Sku,
        string Name,
        string BaseUnit,
        string? Category,
        string? Brand,
        ItemType Type,
        ItemLifecycleStatus Status,
        string? PurchaseUnit,
        string? SalesUnit,
        bool RequiresLot,
        bool RequiresSerial,
        bool RequiresExpiry,
        int ShelfLifeDays,
        bool UseFefo,
        decimal? StandardCost,
        decimal? MinimumStock,
        decimal? MaximumStock,
        decimal? SafetyStock,
        int? LeadTimeDays,
        string? StorageProfile,
        string? PutawayProfile,
        string? DefaultSupplierCode,
        IReadOnlyList<ItemPackagingRequest> Packagings);

    private sealed class ImportedPackaging
    {
        public string? Code { get; init; }
        public string? UnitOfMeasure { get; init; }
        public decimal UnitsPerPackage { get; init; }
        public string? Barcode { get; init; }
        public decimal? GrossWeightKg { get; init; }
        public decimal? LengthCm { get; init; }
        public decimal? WidthCm { get; init; }
        public decimal? HeightCm { get; init; }
        public bool IsDefault { get; init; }
        public string? Name { get; init; }
        public string? LocalizedName { get; init; }
        public string? Gtin { get; init; }
        public string? ParentPackagingCode { get; init; }
        public PackagingType Type { get; init; }
        public PackagingPartialPolicy PartialPackagePolicy { get; init; }
        public bool IsDefaultReceiving { get; init; }
        public bool IsDefaultStorage { get; init; }
        public bool IsDefaultPicking { get; init; }
        public bool IsDefaultShipping { get; init; }
        public bool IsActive { get; init; } = true;
    }

    private sealed record ParsedImport(
        IReadOnlyList<ImportedRow> Rows,
        IReadOnlyList<ItemImportError> Errors);
}
