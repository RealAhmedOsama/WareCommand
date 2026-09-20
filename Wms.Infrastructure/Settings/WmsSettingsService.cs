using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Settings;

public sealed class WmsSettingsService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    WmsSettingsCache cache,
    ILogger<WmsSettingsService> logger) : IWmsSettingsService
{
    public async Task<Result<WmsSettingsSnapshot>> GetAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGet(warehouseId, out var cached))
        {
            return Result.Success(cached);
        }

        try
        {
            if (warehouseId.HasValue && !await IsActiveWarehouseAsync(warehouseId.Value, cancellationToken))
            {
                return Result.Failure<WmsSettingsSnapshot>(WmsErrors.NotFound(
                    "settings.warehouse_not_found",
                    "The selected warehouse was not found or is inactive."));
            }

            var globalValues = await LoadGlobalValuesAsync(cancellationToken);
            var overrideEntity = warehouseId.HasValue
                ? await context.WarehouseSettingsOverrides
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        entity => entity.WarehouseId == warehouseId.Value,
                        cancellationToken)
                : null;
            var overrides = overrideEntity is null ? null : ToOverrides(overrideEntity);
            var values = WmsSettingsPrecedence.Apply(globalValues, overrides);
            var validation = WmsSettingsValidation.Validate(values);
            if (validation.Count > 0)
            {
                return InvalidSettings<WmsSettingsSnapshot>(validation);
            }

            var updatedAtUtc = await GetUpdatedAtAsync(overrideEntity, cancellationToken);
            var snapshot = new WmsSettingsSnapshot(
                warehouseId,
                warehouseId.HasValue ? $"warehouse:{warehouseId.Value}" : "global",
                updatedAtUtc,
                values,
                overrides);
            cache.Set(snapshot);
            return Result.Success(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read WMS settings for warehouse {WarehouseId}", warehouseId);
            return Result.Failure<WmsSettingsSnapshot>(WmsErrors.FromException(
                ex,
                "settings.read_failed",
                "Settings could not be loaded. Please try again."));
        }
    }

    public async Task<Result<WmsSettingsExportDocument>> ExportAsync(
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeGlobalAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WmsSettingsExportDocument>();
        }

        try
        {
            var global = await LoadGlobalValuesAsync(cancellationToken);
            var overrideEntities = await context.WarehouseSettingsOverrides
                .AsNoTracking()
                .OrderBy(entity => entity.WarehouseId)
                .ToListAsync(cancellationToken);
            var overrides = overrideEntities
                .Select(entity => new WmsWarehouseSettingsExport
                {
                    WarehouseId = entity.WarehouseId,
                    Values = ToOverrides(entity)
                })
                .ToList();

            return Result.Success(new WmsSettingsExportDocument
            {
                SchemaVersion = 1,
                Global = global,
                WarehouseOverrides = overrides
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not export WMS settings");
            return Result.Failure<WmsSettingsExportDocument>(WmsErrors.FromException(
                ex,
                "settings.export_failed",
                "Settings could not be exported. Please try again."));
        }
    }

    public async Task<Result<WmsSettingsSnapshot>> SaveGlobalAsync(
        WmsSettingsValues values,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeGlobalAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WmsSettingsSnapshot>();
        }

        var validation = WmsSettingsValidation.Validate(values);
        if (validation.Count > 0)
        {
            return InvalidSettings<WmsSettingsSnapshot>(validation);
        }

        try
        {
            var relationalValidation = await ValidateWarehouseReferencesAsync(
                values,
                warehouseId: null,
                cancellationToken);
            if (relationalValidation.Count > 0)
            {
                return InvalidSettings<WmsSettingsSnapshot>(relationalValidation);
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var entity = await context.GlobalSettings
                .SingleOrDefaultAsync(item => item.Id == WmsGlobalSettingsEntity.GlobalId, cancellationToken);
            var before = entity is null ? null : ToAuditValues(entity);
            entity ??= new WmsGlobalSettingsEntity
            {
                Id = WmsGlobalSettingsEntity.GlobalId,
                Revision = 1
            };
            if (entity.Id == WmsGlobalSettingsEntity.GlobalId &&
                context.Entry(entity).State == EntityState.Detached)
            {
                context.GlobalSettings.Add(entity);
            }

            Apply(entity, values);
            entity.UpdatedAtUtc = clock.UtcNow;
            entity.Revision++;
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SettingsChanged,
                    WmsAuditEntityTypes.Settings,
                    "global",
                    values.WarehouseDefaults.DefaultWarehouseId,
                    before,
                    ToAuditValues(values),
                    Details: "Global business settings changed."),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            cache.InvalidateAll();
            return await GetAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save global WMS settings");
            return Result.Failure<WmsSettingsSnapshot>(WmsErrors.FromException(
                ex,
                "settings.save_failed",
                "Settings could not be saved. Please try again."));
        }
    }

    public async Task<Result<WmsSettingsSnapshot>> SaveWarehouseOverrideAsync(
        int warehouseId,
        WmsWarehouseSettingsOverrides values,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WmsSettingsSnapshot>();
        }

        var validation = WmsSettingsValidation.ValidateOverrides(values);
        if (validation.Count > 0)
        {
            return InvalidSettings<WmsSettingsSnapshot>(validation);
        }

        try
        {
            var globalValues = await LoadGlobalValuesAsync(cancellationToken);
            var effective = WmsSettingsPrecedence.Apply(globalValues, values);
            var effectiveValidation = WmsSettingsValidation.Validate(effective);
            var relationalValidation = await ValidateWarehouseReferencesAsync(
                effective,
                warehouseId,
                cancellationToken);
            var allErrors = MergeErrors(effectiveValidation, relationalValidation);
            if (allErrors.Count > 0)
            {
                return InvalidSettings<WmsSettingsSnapshot>(allErrors);
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var entity = await context.WarehouseSettingsOverrides
                .SingleOrDefaultAsync(item => item.WarehouseId == warehouseId, cancellationToken);
            var before = entity is null ? null : ToAuditValues(entity);
            entity ??= new WmsWarehouseSettingsOverrideEntity
            {
                WarehouseId = warehouseId,
                Revision = 1
            };
            if (entity.WarehouseId == warehouseId &&
                context.Entry(entity).State == EntityState.Detached)
            {
                context.WarehouseSettingsOverrides.Add(entity);
            }

            Apply(entity, values);
            entity.UpdatedAtUtc = clock.UtcNow;
            entity.Revision++;
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SettingsChanged,
                    WmsAuditEntityTypes.Settings,
                    $"warehouse:{warehouseId}",
                    warehouseId,
                    before,
                    ToAuditValues(values),
                    Details: "Warehouse settings override changed."),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            cache.Invalidate(warehouseId);
            return await GetAsync(warehouseId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save WMS settings override for warehouse {WarehouseId}", warehouseId);
            return Result.Failure<WmsSettingsSnapshot>(WmsErrors.FromException(
                ex,
                "settings.save_override_failed",
                "Warehouse settings could not be saved. Please try again."));
        }
    }

    public async Task<Result<WmsSettingsSnapshot>> ImportAsync(
        WmsSettingsExportDocument document,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeGlobalAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WmsSettingsSnapshot>();
        }

        if (document is null || document.SchemaVersion != 1 || document.Global is null)
        {
            return Result.Failure<WmsSettingsSnapshot>(WmsErrors.Validation(
                "settings.schema_invalid",
                "The settings document is missing or uses an unsupported schema version."));
        }

        var globalValidation = WmsSettingsValidation.Validate(document.Global);
        var overrides = document.WarehouseOverrides ?? [];
        var duplicateWarehouseIds = overrides
            .GroupBy(item => item.WarehouseId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        var allErrors = new Dictionary<string, string[]>(globalValidation, StringComparer.Ordinal);
        if (duplicateWarehouseIds.Length > 0)
        {
            allErrors["WarehouseOverrides"] = [
                $"Each warehouse may appear only once. Duplicate IDs: {string.Join(", ", duplicateWarehouseIds)}."
            ];
        }

        foreach (var item in overrides)
        {
            if (item.Values is null)
            {
                allErrors[$"WarehouseOverrides[{item.WarehouseId}].Values"] = [
                    "Warehouse override values are required."
                ];
                continue;
            }

            foreach (var error in WmsSettingsValidation.ValidateOverrides(item.Values))
            {
                allErrors[$"WarehouseOverrides[{item.WarehouseId}].{error.Key}"] = error.Value;
            }
        }

        if (allErrors.Count > 0)
        {
            return InvalidSettings<WmsSettingsSnapshot>(allErrors);
        }

        try
        {
            var activeWarehouseIds = await context.Warehouses
                .Where(warehouse => warehouse.IsActive)
                .Select(warehouse => warehouse.Id)
                .ToHashSetAsync(cancellationToken);
            var unknownWarehouseIds = overrides
                .Select(item => item.WarehouseId)
                .Where(id => !activeWarehouseIds.Contains(id))
                .Distinct()
                .ToArray();
            if (unknownWarehouseIds.Length > 0)
            {
                return Result.Failure<WmsSettingsSnapshot>(WmsErrors.NotFound(
                    "settings.override_warehouse_not_found",
                    $"Settings reference missing or inactive warehouse IDs: {string.Join(", ", unknownWarehouseIds)}."));
            }

            var relationalValidation = await ValidateWarehouseReferencesAsync(
                document.Global,
                warehouseId: null,
                cancellationToken);
            allErrors = MergeErrors(allErrors, relationalValidation);
            foreach (var item in overrides)
            {
                var effective = WmsSettingsPrecedence.Apply(
                    document.Global,
                    item.Values);
                allErrors = MergeErrors(
                    allErrors,
                    await ValidateWarehouseReferencesAsync(
                        effective,
                        item.WarehouseId,
                        cancellationToken));
            }

            if (allErrors.Count > 0)
            {
                return InvalidSettings<WmsSettingsSnapshot>(allErrors);
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var globalEntity = await context.GlobalSettings
                .SingleOrDefaultAsync(item => item.Id == WmsGlobalSettingsEntity.GlobalId, cancellationToken);
            var globalBefore = globalEntity is null ? null : ToAuditValues(globalEntity);
            globalEntity ??= new WmsGlobalSettingsEntity
            {
                Id = WmsGlobalSettingsEntity.GlobalId,
                Revision = 1
            };
            if (globalEntity.Id == WmsGlobalSettingsEntity.GlobalId &&
                context.Entry(globalEntity).State == EntityState.Detached)
            {
                context.GlobalSettings.Add(globalEntity);
            }

            Apply(globalEntity, document.Global);
            globalEntity.UpdatedAtUtc = clock.UtcNow;
            globalEntity.Revision++;
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SettingsChanged,
                    WmsAuditEntityTypes.Settings,
                    "global",
                    document.Global.WarehouseDefaults.DefaultWarehouseId,
                    globalBefore,
                    ToAuditValues(document.Global),
                    Details: "Global business settings imported."),
                cancellationToken);

            var existingOverrides = await context.WarehouseSettingsOverrides
                .ToDictionaryAsync(item => item.WarehouseId, cancellationToken);
            var importedIds = overrides.Select(item => item.WarehouseId).ToHashSet();
            foreach (var existing in existingOverrides.Values.Where(item => !importedIds.Contains(item.WarehouseId)))
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SettingsChanged,
                        WmsAuditEntityTypes.Settings,
                        $"warehouse:{existing.WarehouseId}",
                        existing.WarehouseId,
                        ToAuditValues(existing),
                        Details: "Warehouse settings override removed during import."),
                    cancellationToken);
                context.WarehouseSettingsOverrides.Remove(existing);
            }

            foreach (var imported in overrides)
            {
                existingOverrides.TryGetValue(imported.WarehouseId, out var entity);
                var before = entity is null ? null : ToAuditValues(entity);
                entity ??= new WmsWarehouseSettingsOverrideEntity
                {
                    WarehouseId = imported.WarehouseId,
                    Revision = 1
                };
                if (entity.WarehouseId == imported.WarehouseId &&
                    context.Entry(entity).State == EntityState.Detached)
                {
                    context.WarehouseSettingsOverrides.Add(entity);
                }

                Apply(entity, imported.Values);
                entity.UpdatedAtUtc = clock.UtcNow;
                entity.Revision++;
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SettingsChanged,
                        WmsAuditEntityTypes.Settings,
                        $"warehouse:{imported.WarehouseId}",
                        imported.WarehouseId,
                        before,
                        ToAuditValues(imported.Values),
                        Details: "Warehouse settings override imported."),
                    cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            cache.InvalidateAll();
            return await GetAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not import WMS settings");
            return Result.Failure<WmsSettingsSnapshot>(WmsErrors.FromException(
                ex,
                "settings.import_failed",
                "Settings could not be imported. Please try again."));
        }
    }

    private async Task<Result> AuthorizeAsync(int? warehouseId, CancellationToken cancellationToken)
    {
        return await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SettingsManage,
            warehouseId,
            cancellationToken);
    }

    private async Task<Result> AuthorizeGlobalAsync(CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        return scope.HasGlobalAccess
            ? Result.Success()
            : Result.Failure(WmsErrors.Forbidden(
                "authorization.global_settings_scope_denied",
                "Global settings require global warehouse scope."));
    }

    private async Task<WmsSettingsValues> LoadGlobalValuesAsync(CancellationToken cancellationToken)
    {
        var entity = await context.GlobalSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == WmsGlobalSettingsEntity.GlobalId, cancellationToken);
        return entity is null ? WmsSettingsDefaults.Create() : ToValues(entity);
    }

    private async Task<bool> IsActiveWarehouseAsync(
        int warehouseId,
        CancellationToken cancellationToken) =>
        await context.Warehouses
            .AsNoTracking()
            .AnyAsync(warehouse => warehouse.Id == warehouseId && warehouse.IsActive, cancellationToken);

    private async Task<DateTimeOffset> GetUpdatedAtAsync(
        WmsWarehouseSettingsOverrideEntity? overrideEntity,
        CancellationToken cancellationToken)
    {
        var globalUpdatedAt = await context.GlobalSettings
            .AsNoTracking()
            .Where(entity => entity.Id == WmsGlobalSettingsEntity.GlobalId)
            .Select(entity => (DateTimeOffset?)entity.UpdatedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
        return new[]
        {
            globalUpdatedAt ?? DateTimeOffset.MinValue,
            overrideEntity?.UpdatedAtUtc ?? DateTimeOffset.MinValue
        }.Max();
    }

    private async Task<IReadOnlyDictionary<string, string[]>> ValidateWarehouseReferencesAsync(
        WmsSettingsValues values,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var targetWarehouseId = warehouseId ?? values.WarehouseDefaults.DefaultWarehouseId;
        if (!targetWarehouseId.HasValue)
        {
            return errors;
        }

        var warehouseExists = await IsActiveWarehouseAsync(targetWarehouseId.Value, cancellationToken);
        if (!warehouseExists)
        {
            errors["WarehouseDefaults.DefaultWarehouseId"] = [
                "The selected default warehouse was not found or is inactive."
            ];
            return errors;
        }

        var receivingCode = values.WarehouseDefaults.DefaultReceivingLocationCode.Trim().ToUpperInvariant();
        var receivingExists = await context.Locations.AnyAsync(
            location => location.WarehouseId == targetWarehouseId.Value &&
                        location.IsActive &&
                        location.IsReceivable &&
                        location.Code == receivingCode,
            cancellationToken);
        if (!receivingExists)
        {
            errors["WarehouseDefaults.DefaultReceivingLocationCode"] = [
                "The receiving location must be an active receivable location in the selected warehouse."
            ];
        }

        if (!string.IsNullOrWhiteSpace(values.WarehouseDefaults.DefaultShippingLocationCode))
        {
            var shippingCode = values.WarehouseDefaults.DefaultShippingLocationCode.Trim().ToUpperInvariant();
            var shippingExists = await context.Locations.AnyAsync(
                location => location.WarehouseId == targetWarehouseId.Value &&
                            location.IsActive &&
                            location.Code == shippingCode,
                cancellationToken);
            if (!shippingExists)
            {
                errors["WarehouseDefaults.DefaultShippingLocationCode"] = [
                    "The shipping location must be an active location in the selected warehouse."
                ];
            }
        }

        return errors;
    }

    private static Result<T> InvalidSettings<T>(
        IReadOnlyDictionary<string, string[]> errors) =>
        Result.Failure<T>(WmsErrors.Validation(
            "settings.invalid",
            "The settings contain one or more invalid values.",
            errors));

    private static Dictionary<string, string[]> MergeErrors(
        IReadOnlyDictionary<string, string[]> first,
        IReadOnlyDictionary<string, string[]> second)
    {
        var merged = new Dictionary<string, string[]>(first, StringComparer.Ordinal);
        foreach (var pair in second)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static WmsSettingsValues ToValues(WmsGlobalSettingsEntity entity) => new()
    {
        Company = new WmsCompanySettings
        {
            Name = entity.CompanyName,
            Code = entity.CompanyCode
        },
        WarehouseDefaults = new WmsWarehouseDefaultsSettings
        {
            DefaultWarehouseId = entity.DefaultWarehouseId,
            DefaultReceivingLocationCode = entity.DefaultReceivingLocationCode,
            DefaultShippingLocationCode = entity.DefaultShippingLocationCode
        },
        Numbering = new WmsNumberingSettings
        {
            ReceivingPrefix = entity.ReceivingPrefix,
            NextReceivingNumber = entity.NextReceivingNumber,
            ShippingPrefix = entity.ShippingPrefix,
            NextShippingNumber = entity.NextShippingNumber,
            AdjustmentPrefix = entity.AdjustmentPrefix,
            NextAdjustmentNumber = entity.NextAdjustmentNumber
        },
        Dashboard = new WmsDashboardSettings
        {
            LowStockThreshold = entity.LowStockThreshold,
            LowStockAlertLimit = entity.LowStockAlertLimit,
            RecentMovementLimit = entity.RecentMovementLimit,
            RefreshIntervalSeconds = entity.DashboardRefreshIntervalSeconds
        },
        Inventory = new WmsInventorySettings
        {
            AllowNegativeStock = entity.AllowNegativeStock,
            RequireLocationForAdjustment = entity.RequireLocationForAdjustment,
            MaximumAdjustmentQuantity = entity.MaximumAdjustmentQuantity
        },
        Expiry = new WmsExpirySettings
        {
            WarningDays = entity.ExpiryWarningDays,
            BlockExpiredReceipt = entity.BlockExpiredReceipt
        },
        Scanner = new WmsScannerSettings
        {
            TimeoutMilliseconds = entity.ScannerTimeoutMilliseconds,
            MinimumBarcodeLength = entity.MinimumBarcodeLength,
            MaximumBarcodeLength = entity.MaximumBarcodeLength,
            EnableAudioFeedback = entity.EnableAudioFeedback
        },
        Labels = new WmsLabelSettings
        {
            TemplateName = entity.LabelTemplateName,
            PaperSize = entity.LabelPaperSize,
            IncludeCompanyName = entity.IncludeCompanyNameOnLabels
        },
        Reports = new WmsReportSettings
        {
            DefaultPeriodDays = entity.DefaultReportPeriodDays,
            MaximumRows = entity.MaximumReportRows
        },
        Localization = new WmsLocalizationSettings
        {
            Locale = entity.DefaultLocale,
            TimeZone = entity.DefaultTimeZone,
            CurrencyCode = entity.CurrencyCode
        },
        Integrations = new WmsIntegrationSettings
        {
            Enabled = entity.IntegrationsEnabled,
            EndpointUrl = entity.IntegrationEndpointUrl,
            TimeoutSeconds = entity.IntegrationTimeoutSeconds
        }
    };

    private static WmsWarehouseSettingsOverrides ToOverrides(WmsWarehouseSettingsOverrideEntity entity) => new()
    {
        DefaultReceivingLocationCode = entity.DefaultReceivingLocationCode,
        DefaultShippingLocationCode = entity.DefaultShippingLocationCode,
        LowStockThreshold = entity.LowStockThreshold,
        LowStockAlertLimit = entity.LowStockAlertLimit,
        RecentMovementLimit = entity.RecentMovementLimit,
        RefreshIntervalSeconds = entity.DashboardRefreshIntervalSeconds,
        ExpiryWarningDays = entity.ExpiryWarningDays,
        BlockExpiredReceipt = entity.BlockExpiredReceipt,
        ScannerTimeoutMilliseconds = entity.ScannerTimeoutMilliseconds,
        MinimumBarcodeLength = entity.MinimumBarcodeLength,
        MaximumBarcodeLength = entity.MaximumBarcodeLength,
        EnableAudioFeedback = entity.EnableAudioFeedback,
        DefaultReportPeriodDays = entity.DefaultReportPeriodDays,
        MaximumReportRows = entity.MaximumReportRows,
        Locale = entity.Locale,
        TimeZone = entity.TimeZone
    };

    private static void Apply(WmsGlobalSettingsEntity entity, WmsSettingsValues values)
    {
        entity.CompanyName = values.Company.Name.Trim();
        entity.CompanyCode = values.Company.Code.Trim().ToUpperInvariant();
        entity.DefaultWarehouseId = values.WarehouseDefaults.DefaultWarehouseId;
        entity.DefaultReceivingLocationCode = values.WarehouseDefaults.DefaultReceivingLocationCode.Trim().ToUpperInvariant();
        entity.DefaultShippingLocationCode = NormalizeOptional(values.WarehouseDefaults.DefaultShippingLocationCode);
        entity.ReceivingPrefix = values.Numbering.ReceivingPrefix.Trim();
        entity.NextReceivingNumber = values.Numbering.NextReceivingNumber;
        entity.ShippingPrefix = values.Numbering.ShippingPrefix.Trim();
        entity.NextShippingNumber = values.Numbering.NextShippingNumber;
        entity.AdjustmentPrefix = values.Numbering.AdjustmentPrefix.Trim();
        entity.NextAdjustmentNumber = values.Numbering.NextAdjustmentNumber;
        entity.LowStockThreshold = values.Dashboard.LowStockThreshold;
        entity.LowStockAlertLimit = values.Dashboard.LowStockAlertLimit;
        entity.RecentMovementLimit = values.Dashboard.RecentMovementLimit;
        entity.DashboardRefreshIntervalSeconds = values.Dashboard.RefreshIntervalSeconds;
        entity.AllowNegativeStock = values.Inventory.AllowNegativeStock;
        entity.RequireLocationForAdjustment = values.Inventory.RequireLocationForAdjustment;
        entity.MaximumAdjustmentQuantity = values.Inventory.MaximumAdjustmentQuantity;
        entity.ExpiryWarningDays = values.Expiry.WarningDays;
        entity.BlockExpiredReceipt = values.Expiry.BlockExpiredReceipt;
        entity.ScannerTimeoutMilliseconds = values.Scanner.TimeoutMilliseconds;
        entity.MinimumBarcodeLength = values.Scanner.MinimumBarcodeLength;
        entity.MaximumBarcodeLength = values.Scanner.MaximumBarcodeLength;
        entity.EnableAudioFeedback = values.Scanner.EnableAudioFeedback;
        entity.LabelTemplateName = values.Labels.TemplateName.Trim();
        entity.LabelPaperSize = values.Labels.PaperSize.Trim();
        entity.IncludeCompanyNameOnLabels = values.Labels.IncludeCompanyName;
        entity.DefaultReportPeriodDays = values.Reports.DefaultPeriodDays;
        entity.MaximumReportRows = values.Reports.MaximumRows;
        entity.DefaultLocale = values.Localization.Locale.Trim();
        entity.DefaultTimeZone = WmsTimeZoneCatalog.Normalize(values.Localization.TimeZone);
        entity.CurrencyCode = values.Localization.CurrencyCode.Trim().ToUpperInvariant();
        entity.IntegrationsEnabled = values.Integrations.Enabled;
        entity.IntegrationEndpointUrl = NormalizeOptional(values.Integrations.EndpointUrl);
        entity.IntegrationTimeoutSeconds = values.Integrations.TimeoutSeconds;
    }

    private static void Apply(
        WmsWarehouseSettingsOverrideEntity entity,
        WmsWarehouseSettingsOverrides values)
    {
        entity.DefaultReceivingLocationCode = NormalizeOptional(values.DefaultReceivingLocationCode, upperCase: true);
        entity.DefaultShippingLocationCode = NormalizeOptional(values.DefaultShippingLocationCode, upperCase: true);
        entity.LowStockThreshold = values.LowStockThreshold;
        entity.LowStockAlertLimit = values.LowStockAlertLimit;
        entity.RecentMovementLimit = values.RecentMovementLimit;
        entity.DashboardRefreshIntervalSeconds = values.RefreshIntervalSeconds;
        entity.ExpiryWarningDays = values.ExpiryWarningDays;
        entity.BlockExpiredReceipt = values.BlockExpiredReceipt;
        entity.ScannerTimeoutMilliseconds = values.ScannerTimeoutMilliseconds;
        entity.MinimumBarcodeLength = values.MinimumBarcodeLength;
        entity.MaximumBarcodeLength = values.MaximumBarcodeLength;
        entity.EnableAudioFeedback = values.EnableAudioFeedback;
        entity.DefaultReportPeriodDays = values.DefaultReportPeriodDays;
        entity.MaximumReportRows = values.MaximumReportRows;
        entity.Locale = NormalizeOptional(values.Locale);
        entity.TimeZone = string.IsNullOrWhiteSpace(values.TimeZone)
            ? null
            : WmsTimeZoneCatalog.Normalize(values.TimeZone);
    }

    private static string? NormalizeOptional(string? value, bool upperCase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return upperCase ? trimmed.ToUpperInvariant() : trimmed;
    }

    private static Dictionary<string, object?> ToAuditValues(WmsGlobalSettingsEntity entity) =>
        ToAuditValues(ToValues(entity));

    private static Dictionary<string, object?> ToAuditValues(WmsSettingsValues values) => new()
    {
        ["companyName"] = values.Company.Name,
        ["companyCode"] = values.Company.Code,
        ["defaultWarehouseId"] = values.WarehouseDefaults.DefaultWarehouseId,
        ["defaultReceivingLocationCode"] = values.WarehouseDefaults.DefaultReceivingLocationCode,
        ["defaultShippingLocationCode"] = values.WarehouseDefaults.DefaultShippingLocationCode,
        ["receivingPrefix"] = values.Numbering.ReceivingPrefix,
        ["nextReceivingNumber"] = values.Numbering.NextReceivingNumber,
        ["shippingPrefix"] = values.Numbering.ShippingPrefix,
        ["nextShippingNumber"] = values.Numbering.NextShippingNumber,
        ["adjustmentPrefix"] = values.Numbering.AdjustmentPrefix,
        ["nextAdjustmentNumber"] = values.Numbering.NextAdjustmentNumber,
        ["lowStockThreshold"] = values.Dashboard.LowStockThreshold,
        ["lowStockAlertLimit"] = values.Dashboard.LowStockAlertLimit,
        ["recentMovementLimit"] = values.Dashboard.RecentMovementLimit,
        ["refreshIntervalSeconds"] = values.Dashboard.RefreshIntervalSeconds,
        ["allowNegativeStock"] = values.Inventory.AllowNegativeStock,
        ["requireLocationForAdjustment"] = values.Inventory.RequireLocationForAdjustment,
        ["maximumAdjustmentQuantity"] = values.Inventory.MaximumAdjustmentQuantity,
        ["expiryWarningDays"] = values.Expiry.WarningDays,
        ["blockExpiredReceipt"] = values.Expiry.BlockExpiredReceipt,
        ["scannerTimeoutMilliseconds"] = values.Scanner.TimeoutMilliseconds,
        ["minimumBarcodeLength"] = values.Scanner.MinimumBarcodeLength,
        ["maximumBarcodeLength"] = values.Scanner.MaximumBarcodeLength,
        ["enableAudioFeedback"] = values.Scanner.EnableAudioFeedback,
        ["labelTemplateName"] = values.Labels.TemplateName,
        ["labelPaperSize"] = values.Labels.PaperSize,
        ["includeCompanyNameOnLabels"] = values.Labels.IncludeCompanyName,
        ["defaultReportPeriodDays"] = values.Reports.DefaultPeriodDays,
        ["maximumReportRows"] = values.Reports.MaximumRows,
        ["locale"] = values.Localization.Locale,
        ["timeZone"] = values.Localization.TimeZone,
        ["currencyCode"] = values.Localization.CurrencyCode,
        ["integrationsEnabled"] = values.Integrations.Enabled,
        ["integrationEndpointUrl"] = values.Integrations.EndpointUrl,
        ["integrationTimeoutSeconds"] = values.Integrations.TimeoutSeconds
    };

    private static Dictionary<string, object?> ToAuditValues(WmsWarehouseSettingsOverrideEntity entity) =>
        ToAuditValues(ToOverrides(entity));

    private static Dictionary<string, object?> ToAuditValues(WmsWarehouseSettingsOverrides values) => new()
    {
        ["defaultReceivingLocationCode"] = values.DefaultReceivingLocationCode,
        ["defaultShippingLocationCode"] = values.DefaultShippingLocationCode,
        ["lowStockThreshold"] = values.LowStockThreshold,
        ["lowStockAlertLimit"] = values.LowStockAlertLimit,
        ["recentMovementLimit"] = values.RecentMovementLimit,
        ["refreshIntervalSeconds"] = values.RefreshIntervalSeconds,
        ["expiryWarningDays"] = values.ExpiryWarningDays,
        ["blockExpiredReceipt"] = values.BlockExpiredReceipt,
        ["scannerTimeoutMilliseconds"] = values.ScannerTimeoutMilliseconds,
        ["minimumBarcodeLength"] = values.MinimumBarcodeLength,
        ["maximumBarcodeLength"] = values.MaximumBarcodeLength,
        ["enableAudioFeedback"] = values.EnableAudioFeedback,
        ["defaultReportPeriodDays"] = values.DefaultReportPeriodDays,
        ["maximumReportRows"] = values.MaximumReportRows,
        ["locale"] = values.Locale,
        ["timeZone"] = values.TimeZone
    };
}
