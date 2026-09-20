using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Suppliers;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Suppliers;

public sealed class SupplierManagementService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<SupplierManagementService> logger) : ISupplierManagementService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;
    private const int MaximumReferencesPerSupplier = 1_000;

    public async Task<Result<SupplierPageDto>> ListAsync(
        SupplierListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(request.PreferredWarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierPageDto>();
        }

        var query = await ApplyVisibilityAsync(
            QuerySuppliers(),
            request.PreferredWarehouseId,
            cancellationToken);
        query = ApplyFilters(query, request);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await ApplyOrdering(query, request)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var dtos = await MapToDtosAsync(rows, cancellationToken);

        return Result.Success(new SupplierPageDto(
            dtos,
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<SupplierDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierDto>();
        }

        var supplier = await LoadSupplierAsync(id, asNoTracking: true, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<SupplierDto>(WmsErrors.NotFound(
                "supplier.not_found",
                "The requested supplier was not found."));
        }

        var warehouseAuthorization = await AuthorizeReadAsync(
            supplier.PreferredWarehouseId,
            cancellationToken);
        if (warehouseAuthorization.IsFailure)
        {
            return warehouseAuthorization.ToFailure<SupplierDto>();
        }

        return Result.Success((await MapToDtosAsync(new List<Supplier> { supplier }, cancellationToken)).Single());
    }

    public async Task<Result<SupplierDto>> CreateAsync(
        SupplierInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierDto>();
        }

        try
        {
            var code = NormalizeRequired(input.Code, "code");
            if (await context.Suppliers.AnyAsync(
                    supplier => supplier.Code == code,
                    cancellationToken))
            {
                return Result.Failure<SupplierDto>(WmsErrors.Conflict(
                    "supplier.code_conflict",
                    $"Supplier code '{code}' already exists."));
            }

            var externalErpIdentifier = NormalizeOptionalUpper(input.ExternalErpIdentifier);
            if (externalErpIdentifier is not null &&
                await context.Suppliers.AnyAsync(
                    supplier => supplier.ExternalErpIdentifier == externalErpIdentifier,
                    cancellationToken))
            {
                return Result.Failure<SupplierDto>(WmsErrors.Conflict(
                    "supplier.external_erp_conflict",
                    $"External ERP identifier '{externalErpIdentifier}' is already assigned."));
            }

            var defaults = input.ReceivingDefaults ?? new SupplierReceivingDefaultsInput();
            var defaultsValidation = await ValidateReceivingDefaultsAsync(defaults, cancellationToken);
            if (defaultsValidation.IsFailure)
            {
                return defaultsValidation.ToFailure<SupplierDto>();
            }

            var referenceValidation = await ValidateItemReferencesAsync(
                input.ItemReferences,
                cancellationToken);
            if (referenceValidation.IsFailure)
            {
                return referenceValidation.ToFailure<SupplierDto>();
            }

            var supplier = new Supplier(
                code,
                input.LegalName,
                input.LocalizedName,
                input.TaxRegistrationNumber,
                externalErpIdentifier,
                input.AddressLine1,
                input.AddressLine2,
                input.City,
                input.StateOrProvince,
                input.PostalCode,
                input.CountryCode,
                input.ContactName,
                input.ContactEmail,
                input.ContactPhone,
                defaults.PreferredWarehouseId,
                defaults.PreferredDockLocationId,
                defaults.DefaultLeadTimeDays,
                defaults.OverDeliveryTolerancePercent,
                defaults.UnderDeliveryTolerancePercent,
                defaults.RequiresLot,
                defaults.RequiresExpiry,
                defaults.QualityProfile,
                defaults.LabelRule,
                defaults.DefaultCurrencyCode,
                input.Notes);
            if (!input.IsActive)
            {
                supplier.Deactivate();
            }

            foreach (var referenceInput in input.ItemReferences ?? [])
            {
                var reference = BuildReference(referenceInput);
                if (!referenceInput.IsActive)
                {
                    reference.SetActive(false);
                }

                supplier.AddItemReference(reference);
            }

            context.Suppliers.Add(supplier);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierCreated,
                    WmsAuditEntityTypes.Supplier,
                    code,
                    WarehouseId: supplier.PreferredWarehouseId,
                    After: AuditSnapshot(supplier),
                    ActorUserId: userId),
                cancellationToken);
            if (supplier.ItemReferences.Count > 0)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SupplierItemReferencesChanged,
                        WmsAuditEntityTypes.Supplier,
                        code,
                        WarehouseId: supplier.PreferredWarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["referenceCount"] = supplier.ItemReferences.Count
                        },
                        ActorUserId: userId),
                    cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Supplier master created for {SupplierCode} by {UserId}", code, userId);
            var saved = await LoadSupplierAsync(supplier.Id, asNoTracking: true, cancellationToken);
            return Result.Success((await MapToDtosAsync(new List<Supplier> { saved! }, cancellationToken)).Single());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SupplierDto>(WmsErrors.Validation(
                "supplier.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Supplier creation failed for {SupplierCode}", input.Code);
            return Result.Failure<SupplierDto>(WmsErrors.FromException(
                exception,
                "supplier.create_failed",
                "The supplier could not be created."));
        }
    }

    public async Task<Result<SupplierDto>> UpdateAsync(
        int id,
        SupplierInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierDto>();
        }

        try
        {
            var supplier = await LoadSupplierAsync(id, asNoTracking: false, cancellationToken);
            if (supplier is null)
            {
                return Result.Failure<SupplierDto>(WmsErrors.NotFound(
                    "supplier.not_found",
                    "The requested supplier was not found."));
            }

            var existingWarehouseAuthorization = await AuthorizeManageAsync(
                supplier.PreferredWarehouseId,
                cancellationToken);
            if (existingWarehouseAuthorization.IsFailure)
            {
                return existingWarehouseAuthorization.ToFailure<SupplierDto>();
            }

            var code = NormalizeRequired(input.Code, "code");
            if (!string.Equals(code, supplier.Code, StringComparison.Ordinal))
            {
                return Result.Failure<SupplierDto>(WmsErrors.Conflict(
                    "supplier.code_immutable",
                    "Supplier codes cannot change after the supplier is created."));
            }

            var externalErpIdentifier = NormalizeOptionalUpper(input.ExternalErpIdentifier);
            if (externalErpIdentifier is not null &&
                await context.Suppliers.AnyAsync(
                    other => other.Id != id &&
                             other.ExternalErpIdentifier == externalErpIdentifier,
                    cancellationToken))
            {
                return Result.Failure<SupplierDto>(WmsErrors.Conflict(
                    "supplier.external_erp_conflict",
                    $"External ERP identifier '{externalErpIdentifier}' is already assigned."));
            }

            var defaults = input.ReceivingDefaults ?? new SupplierReceivingDefaultsInput(
                supplier.PreferredWarehouseId,
                supplier.PreferredDockLocationId,
                supplier.DefaultLeadTimeDays,
                supplier.OverDeliveryTolerancePercent,
                supplier.UnderDeliveryTolerancePercent,
                supplier.RequiresLot,
                supplier.RequiresExpiry,
                supplier.QualityProfile,
                supplier.LabelRule,
                supplier.DefaultCurrencyCode);
            var defaultsValidation = await ValidateReceivingDefaultsAsync(defaults, cancellationToken);
            if (defaultsValidation.IsFailure)
            {
                return defaultsValidation.ToFailure<SupplierDto>();
            }

            if (input.ItemReferences is not null)
            {
                var referenceValidation = await ValidateItemReferencesAsync(
                    input.ItemReferences,
                    cancellationToken);
                if (referenceValidation.IsFailure)
                {
                    return referenceValidation.ToFailure<SupplierDto>();
                }
            }

            var before = AuditSnapshot(supplier);
            supplier.UpdateProfile(
                input.LegalName,
                input.LocalizedName,
                input.TaxRegistrationNumber,
                externalErpIdentifier,
                input.AddressLine1,
                input.AddressLine2,
                input.City,
                input.StateOrProvince,
                input.PostalCode,
                input.CountryCode,
                input.ContactName,
                input.ContactEmail,
                input.ContactPhone,
                defaults.PreferredWarehouseId,
                defaults.PreferredDockLocationId,
                defaults.DefaultLeadTimeDays,
                defaults.OverDeliveryTolerancePercent,
                defaults.UnderDeliveryTolerancePercent,
                defaults.RequiresLot,
                defaults.RequiresExpiry,
                defaults.QualityProfile,
                defaults.LabelRule,
                defaults.DefaultCurrencyCode,
                input.Notes);
            if (input.IsActive)
            {
                supplier.Activate();
            }
            else
            {
                supplier.Deactivate();
            }

            var referencesChanged = input.ItemReferences is not null;
            if (referencesChanged)
            {
                ApplyItemReferences(supplier, input.ItemReferences!);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SupplierUpdated,
                    WmsAuditEntityTypes.Supplier,
                    supplier.Code,
                    WarehouseId: supplier.PreferredWarehouseId,
                    Before: before,
                    After: AuditSnapshot(supplier),
                    ActorUserId: userId),
                cancellationToken);
            if (referencesChanged)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.SupplierItemReferencesChanged,
                        WmsAuditEntityTypes.Supplier,
                        supplier.Code,
                        WarehouseId: supplier.PreferredWarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["referenceCount"] = supplier.ItemReferences.Count,
                            ["activeReferenceCount"] = supplier.ItemReferences.Count(reference => reference.IsActive)
                        },
                        ActorUserId: userId),
                    cancellationToken);
            }

            await context.SaveChangesAsync(cancellationToken);
            var saved = await LoadSupplierAsync(id, asNoTracking: true, cancellationToken);
            return Result.Success((await MapToDtosAsync(new List<Supplier> { saved! }, cancellationToken)).Single());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SupplierDto>(WmsErrors.Validation(
                "supplier.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Supplier update failed for {SupplierId}", id);
            return Result.Failure<SupplierDto>(WmsErrors.FromException(
                exception,
                "supplier.update_failed",
                "The supplier could not be updated."));
        }
    }

    public async Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var supplier = await LoadSupplierAsync(id, asNoTracking: false, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "supplier.not_found",
                "The requested supplier was not found."));
        }

        var warehouseAuthorization = await AuthorizeManageAsync(
            supplier.PreferredWarehouseId,
            cancellationToken);
        if (warehouseAuthorization.IsFailure)
        {
            return warehouseAuthorization;
        }

        var before = AuditSnapshot(supplier);
        if (active)
        {
            supplier.Activate();
        }
        else
        {
            supplier.Deactivate();
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                active ? WmsAuditActions.SupplierActivated : WmsAuditActions.SupplierDeactivated,
                WmsAuditEntityTypes.Supplier,
                supplier.Code,
                WarehouseId: supplier.PreferredWarehouseId,
                Before: before,
                After: AuditSnapshot(supplier),
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
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var supplier = await LoadSupplierAsync(id, asNoTracking: false, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "supplier.not_found",
                "The requested supplier was not found."));
        }

        var warehouseAuthorization = await AuthorizeManageAsync(
            supplier.PreferredWarehouseId,
            cancellationToken);
        if (warehouseAuthorization.IsFailure)
        {
            return warehouseAuthorization;
        }

        if (supplier.ItemReferences.Count > 0 || await context.Items.AnyAsync(
                item => item.DefaultSupplierCode == supplier.Code,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.Conflict(
                "supplier.referenced",
                "The supplier is referenced by item master data or supplier-item mappings. Deactivate it instead."));
        }

        context.Suppliers.Remove(supplier);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.SupplierDeleted,
                WmsAuditEntityTypes.Supplier,
                supplier.Code,
                WarehouseId: supplier.PreferredWarehouseId,
                Before: AuditSnapshot(supplier),
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<SupplierImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierImportResult>();
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000)
        {
            return Result.Failure<SupplierImportResult>(WmsErrors.Validation(
                "supplier.import_empty",
                "Provide a non-empty CSV import no larger than 2 MB."));
        }

        var parsed = ParseImport(csv);
        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<SupplierImportResult>(WmsErrors.Validation(
                "supplier.import_invalid",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        if (parsed.Rows.Count is < 1 or > MaximumImportRows)
        {
            return Result.Failure<SupplierImportResult>(WmsErrors.Validation(
                "supplier.import_limit",
                $"Import must contain between 1 and {MaximumImportRows} data rows."));
        }

        var groups = parsed.Rows
            .GroupBy(row => NormalizeRequired(row.Code, "code"), StringComparer.Ordinal)
            .ToArray();
        var supplierCodes = groups.Select(group => group.Key).ToArray();
        if (await context.Suppliers.AnyAsync(
                supplier => supplierCodes.Contains(supplier.Code),
                cancellationToken))
        {
            return Result.Failure<SupplierImportResult>(WmsErrors.Conflict(
                "supplier.code_conflict",
                "One or more supplier codes already exist."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var importedSupplierCount = 0;
        var importedReferenceCount = 0;
        try
        {
            foreach (var group in groups)
            {
                var inputResult = await BuildImportInputAsync(group.ToArray(), cancellationToken);
                if (inputResult.IsFailure)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return inputResult.ToFailure<SupplierImportResult>();
                }

                var created = await CreateAsync(inputResult.Value, userId, cancellationToken);
                if (created.IsFailure)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return created.ToFailure<SupplierImportResult>();
                }

                importedSupplierCount++;
                importedReferenceCount += inputResult.Value.ItemReferences?.Count ?? 0;
            }

            await transaction.CommitAsync(cancellationToken);
            return Result.Success(new SupplierImportResult(
                importedSupplierCount,
                importedReferenceCount,
                []));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogError(exception, "Supplier import failed");
            return Result.Failure<SupplierImportResult>(WmsErrors.FromException(
                exception,
                "supplier.import_failed",
                "The supplier import could not be completed."));
        }
    }

    public async Task<Result<string>> ExportAsync(
        SupplierListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(request.PreferredWarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var query = await ApplyVisibilityAsync(
            QuerySuppliers(),
            request.PreferredWarehouseId,
            cancellationToken);
        query = ApplyFilters(query, request);
        var suppliers = await ApplyOrdering(query, request)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);
        var warehouseCodes = await LoadWarehouseCodesAsync(suppliers, cancellationToken);
        var dockCodes = await LoadDockCodesAsync(suppliers, cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine(
            "CODE,LEGAL_NAME,LOCALIZED_NAME,TAX_REGISTRATION_NUMBER,EXTERNAL_ERP_IDENTIFIER,ADDRESS_LINE_1,ADDRESS_LINE_2,CITY,STATE_OR_PROVINCE,POSTAL_CODE,COUNTRY_CODE,CONTACT_NAME,CONTACT_EMAIL,CONTACT_PHONE,IS_ACTIVE,PREFERRED_WAREHOUSE_CODE,PREFERRED_DOCK_CODE,DEFAULT_LEAD_TIME_DAYS,OVER_DELIVERY_TOLERANCE_PERCENT,UNDER_DELIVERY_TOLERANCE_PERCENT,REQUIRES_LOT,REQUIRES_EXPIRY,QUALITY_PROFILE,LABEL_RULE,DEFAULT_CURRENCY_CODE,NOTES,ITEM_SKU,VENDOR_SKU,VENDOR_BARCODE,ITEM_PACKAGING_CODE,VENDOR_PACKAGING,UNITS_PER_PURCHASE_PACKAGE,MINIMUM_ORDER_QUANTITY,REFERENCE_LEAD_TIME_DAYS,REFERENCE_ACTIVE");
        foreach (var supplier in suppliers)
        {
            var references = supplier.ItemReferences.Count == 0
                ? new SupplierItemReference?[] { null }
                : supplier.ItemReferences.Cast<SupplierItemReference?>().ToArray();
            foreach (var reference in references)
            {
                builder.AppendLine(string.Join(",", [
                    EscapeCsv(supplier.Code),
                    EscapeCsv(supplier.LegalName),
                    EscapeCsv(supplier.LocalizedName),
                    EscapeCsv(supplier.TaxRegistrationNumber),
                    EscapeCsv(supplier.ExternalErpIdentifier),
                    EscapeCsv(supplier.AddressLine1),
                    EscapeCsv(supplier.AddressLine2),
                    EscapeCsv(supplier.City),
                    EscapeCsv(supplier.StateOrProvince),
                    EscapeCsv(supplier.PostalCode),
                    EscapeCsv(supplier.CountryCode),
                    EscapeCsv(supplier.ContactName),
                    EscapeCsv(supplier.ContactEmail),
                    EscapeCsv(supplier.ContactPhone),
                    supplier.IsActive.ToString(),
                    EscapeCsv(supplier.PreferredWarehouseId is null
                        ? null
                        : warehouseCodes.GetValueOrDefault(supplier.PreferredWarehouseId.Value)),
                    EscapeCsv(supplier.PreferredDockLocationId is null
                        ? null
                        : dockCodes.GetValueOrDefault(supplier.PreferredDockLocationId.Value)),
                    supplier.DefaultLeadTimeDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    supplier.OverDeliveryTolerancePercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    supplier.UnderDeliveryTolerancePercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    supplier.RequiresLot.ToString(),
                    supplier.RequiresExpiry.ToString(),
                    EscapeCsv(supplier.QualityProfile),
                    EscapeCsv(supplier.LabelRule),
                    EscapeCsv(supplier.DefaultCurrencyCode),
                    EscapeCsv(supplier.Notes),
                    EscapeCsv(reference?.Item?.Sku),
                    EscapeCsv(reference?.VendorSku),
                    EscapeCsv(reference?.VendorBarcode),
                    EscapeCsv(reference?.ItemPackaging?.Code),
                    EscapeCsv(reference?.VendorPackaging),
                    reference?.UnitsPerPurchasePackage?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    reference?.MinimumOrderQuantity.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    reference?.LeadTimeDays?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    reference?.IsActive.ToString() ?? string.Empty
                ]));
            }
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result<SupplierItemReferenceResolutionDto>> ResolveItemReferenceAsync(
        SupplierItemReferenceResolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SupplierItemReferenceResolutionDto>();
        }

        var supplier = await LoadSupplierAsync(query.SupplierId, asNoTracking: true, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<SupplierItemReferenceResolutionDto>(WmsErrors.NotFound(
                "supplier.not_found",
                "The requested supplier was not found."));
        }

        var warehouseAuthorization = await AuthorizeReadAsync(
            supplier.PreferredWarehouseId,
            cancellationToken);
        if (warehouseAuthorization.IsFailure)
        {
            return warehouseAuthorization.ToFailure<SupplierItemReferenceResolutionDto>();
        }

        if (!query.IncludeInactive && !supplier.IsActive)
        {
            return Result.Failure<SupplierItemReferenceResolutionDto>(WmsErrors.Conflict(
                "supplier.inactive",
                "Inactive suppliers cannot be used for new document imports."));
        }

        var vendorSku = NormalizeOptionalUpper(query.VendorSku);
        var vendorBarcode = NormalizeOptionalUpper(query.VendorBarcode);
        if (vendorSku is null && vendorBarcode is null)
        {
            return Result.Failure<SupplierItemReferenceResolutionDto>(WmsErrors.Validation(
                "supplier.reference_identifier_required",
                "Provide a vendor SKU or vendor barcode."));
        }

        var matches = supplier.ItemReferences
            .Where(reference => query.IncludeInactive || reference.IsActive)
            .Where(reference =>
                (vendorSku is not null && reference.VendorSku == vendorSku) ||
                (vendorBarcode is not null && reference.VendorBarcode == vendorBarcode))
            .ToArray();
        if (matches.Length == 0)
        {
            return Result.Failure<SupplierItemReferenceResolutionDto>(WmsErrors.NotFound(
                "supplier.item_reference_not_found",
                "No active supplier item reference matched the supplied vendor identifier."));
        }

        if (matches.Select(reference => reference.ItemId).Distinct().Count() > 1)
        {
            return Result.Failure<SupplierItemReferenceResolutionDto>(WmsErrors.Conflict(
                "supplier.item_reference_ambiguous",
                "The supplied vendor identifiers resolve to more than one item."));
        }

        var reference = matches[0];
        return Result.Success(new SupplierItemReferenceResolutionDto(
            supplier.Id,
            supplier.Code,
            reference.ItemId,
            reference.Item?.Sku ?? string.Empty,
            reference.Item?.Name ?? string.Empty,
            reference.ItemPackagingId,
            reference.ItemPackaging?.Code,
            reference.VendorSku,
            reference.VendorBarcode,
            reference.VendorPackaging,
            reference.UnitsPerPurchasePackage,
            reference.MinimumOrderQuantity,
            reference.LeadTimeDays,
            supplier.DefaultLeadTimeDays,
            supplier.RequiresLot,
            supplier.RequiresExpiry,
            supplier.DefaultCurrencyCode));
    }

    private IQueryable<Supplier> QuerySuppliers(bool asNoTracking = true)
    {
        var query = context.Suppliers.AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query
            .Include(supplier => supplier.ItemReferences)
            .ThenInclude(reference => reference.Item)
            .Include(supplier => supplier.ItemReferences)
            .ThenInclude(reference => reference.ItemPackaging);
    }

    private async Task<Supplier?> LoadSupplierAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        return await QuerySuppliers(asNoTracking)
            .SingleOrDefaultAsync(supplier => supplier.Id == id, cancellationToken);
    }

    private IQueryable<Supplier> ApplyFilters(
        IQueryable<Supplier> query,
        SupplierListQuery request)
    {
        if (!request.IncludeInactive)
        {
            query = query.Where(supplier => supplier.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = string.Equals(
                    context.Database.ProviderName,
                    PostgreSqlProviderName,
                    StringComparison.Ordinal)
                ? query.Where(supplier =>
                    EF.Functions.ILike(supplier.Code, pattern) ||
                    EF.Functions.ILike(supplier.LegalName, pattern) ||
                    (supplier.LocalizedName != null && EF.Functions.ILike(supplier.LocalizedName, pattern)) ||
                    (supplier.ExternalErpIdentifier != null && EF.Functions.ILike(supplier.ExternalErpIdentifier, pattern)) ||
                    (supplier.TaxRegistrationNumber != null && EF.Functions.ILike(supplier.TaxRegistrationNumber, pattern)) ||
                    supplier.ItemReferences.Any(reference =>
                        EF.Functions.ILike(reference.VendorSku, pattern) ||
                        (reference.VendorBarcode != null && EF.Functions.ILike(reference.VendorBarcode, pattern))))
                : query.Where(supplier =>
                    EF.Functions.Like(supplier.Code, pattern) ||
                    EF.Functions.Like(supplier.LegalName, pattern) ||
                    (supplier.LocalizedName != null && EF.Functions.Like(supplier.LocalizedName, pattern)) ||
                    (supplier.ExternalErpIdentifier != null && EF.Functions.Like(supplier.ExternalErpIdentifier, pattern)) ||
                    (supplier.TaxRegistrationNumber != null && EF.Functions.Like(supplier.TaxRegistrationNumber, pattern)) ||
                    supplier.ItemReferences.Any(reference =>
                        EF.Functions.Like(reference.VendorSku, pattern) ||
                        (reference.VendorBarcode != null && EF.Functions.Like(reference.VendorBarcode, pattern))));
        }

        return query;
    }

    private static IOrderedQueryable<Supplier> ApplyOrdering(
        IQueryable<Supplier> query,
        SupplierListQuery request)
    {
        var ordered = request.SortBy switch
        {
            SupplierSortField.LegalName => request.Descending
                ? query.OrderByDescending(supplier => supplier.LegalName)
                : query.OrderBy(supplier => supplier.LegalName),
            SupplierSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(supplier => supplier.UpdatedAt)
                : query.OrderBy(supplier => supplier.UpdatedAt),
            SupplierSortField.CreatedAt => request.Descending
                ? query.OrderByDescending(supplier => supplier.CreatedAt)
                : query.OrderBy(supplier => supplier.CreatedAt),
            _ => request.Descending
                ? query.OrderByDescending(supplier => supplier.Code)
                : query.OrderBy(supplier => supplier.Code)
        };

        return ordered.ThenBy(supplier => supplier.Id);
    }

    private async Task<IQueryable<Supplier>> ApplyVisibilityAsync(
        IQueryable<Supplier> query,
        int? preferredWarehouseId,
        CancellationToken cancellationToken)
    {
        if (preferredWarehouseId.HasValue)
        {
            return query.Where(supplier => supplier.PreferredWarehouseId == preferredWarehouseId.Value);
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        return scope.HasGlobalAccess
            ? query
            : query.Where(supplier =>
                supplier.PreferredWarehouseId == null ||
                scope.WarehouseIds.Contains(supplier.PreferredWarehouseId.Value));
    }

    private async Task<Result> AuthorizeReadAsync(
        int? warehouseId,
        CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SuppliersRead,
            warehouseId,
            cancellationToken);

    private async Task<Result> AuthorizeManageAsync(
        int? warehouseId,
        CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SuppliersManage,
            warehouseId,
            cancellationToken);

    private async Task<Result> ValidateReceivingDefaultsAsync(
        SupplierReceivingDefaultsInput defaults,
        CancellationToken cancellationToken)
    {
        if (defaults.PreferredWarehouseId.HasValue)
        {
            var authorization = await AuthorizeManageAsync(
                defaults.PreferredWarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }

            var warehouse = await context.Warehouses
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == defaults.PreferredWarehouseId.Value,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "supplier.preferred_warehouse_not_found",
                    "The preferred warehouse was not found."));
            }

            if (!warehouse.IsActive)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "supplier.preferred_warehouse_inactive",
                    "The preferred warehouse must be active."));
            }
        }

        if (defaults.PreferredDockLocationId.HasValue)
        {
            var location = await context.Locations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == defaults.PreferredDockLocationId.Value,
                    cancellationToken);
            if (location is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "supplier.preferred_dock_not_found",
                    "The preferred receiving dock was not found."));
            }

            if (location.WarehouseId != defaults.PreferredWarehouseId ||
                !location.IsActive ||
                !location.IsReceivable)
            {
                return Result.Failure(WmsErrors.Validation(
                    "supplier.preferred_dock_invalid",
                    "The preferred dock must be an active receivable location in the preferred warehouse."));
            }
        }

        return Result.Success();
    }

    private async Task<Result> ValidateItemReferencesAsync(
        IReadOnlyList<SupplierItemReferenceInput>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs is null)
        {
            return Result.Success();
        }

        if (inputs.Count > MaximumReferencesPerSupplier)
        {
            return Result.Failure(WmsErrors.Validation(
                "supplier.reference_limit",
                $"A supplier cannot contain more than {MaximumReferencesPerSupplier} item references."));
        }

        var duplicateSku = inputs
            .GroupBy(input => NormalizeOptionalUpper(input.VendorSku) ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicateSku is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "supplier.vendor_sku_conflict",
                $"Vendor SKU '{duplicateSku.Key}' appears more than once for the supplier."));
        }

        var duplicateBarcode = inputs
            .Where(input => !string.IsNullOrWhiteSpace(input.VendorBarcode))
            .GroupBy(input => NormalizeOptionalUpper(input.VendorBarcode)!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBarcode is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "supplier.vendor_barcode_conflict",
                $"Vendor barcode '{duplicateBarcode.Key}' appears more than once for the supplier."));
        }

        var itemIds = inputs.Select(input => input.ItemId).Distinct().ToArray();
        if (itemIds.Any(itemId => itemId <= 0))
        {
            return Result.Failure(WmsErrors.Validation(
                "supplier.item_reference_invalid",
                "Every supplier item reference must specify a valid item."));
        }

        var items = await context.Items
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .Select(item => new { item.Id, item.IsActive })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var missingItem = itemIds.FirstOrDefault(itemId => !items.ContainsKey(itemId));
        if (missingItem != 0)
        {
            return Result.Failure(WmsErrors.NotFound(
                "supplier.item_reference_item_not_found",
                $"Item {missingItem} was not found."));
        }

        var packagingIds = inputs
            .Where(input => input.ItemPackagingId.HasValue)
            .Select(input => input.ItemPackagingId!.Value)
            .Distinct()
            .ToArray();
        var packagings = await context.ItemPackagings
            .AsNoTracking()
            .Where(packaging => packagingIds.Contains(packaging.Id))
            .Select(packaging => new { packaging.Id, packaging.ItemId, packaging.IsActive })
            .ToDictionaryAsync(packaging => packaging.Id, cancellationToken);

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.VendorSku))
            {
                return Result.Failure(WmsErrors.Validation(
                    "supplier.vendor_sku_required",
                    "Every supplier item reference must specify a vendor SKU."));
            }

            if (input.IsActive && !items[input.ItemId].IsActive)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "supplier.item_reference_item_inactive",
                    $"Item {input.ItemId} is inactive and cannot have an active supplier mapping."));
            }

            if (input.ItemPackagingId.HasValue &&
                (!packagings.TryGetValue(input.ItemPackagingId.Value, out var packaging) ||
                 packaging.ItemId != input.ItemId ||
                 (input.IsActive && !packaging.IsActive)))
            {
                return Result.Failure(WmsErrors.Validation(
                    "supplier.item_reference_packaging_invalid",
                    $"Packaging {input.ItemPackagingId.Value} does not belong to the referenced active item."));
            }

            if (input.MinimumOrderQuantity <= 0 ||
                input.UnitsPerPurchasePackage is <= 0 ||
                input.LeadTimeDays is < 0 or > 3_650)
            {
                return Result.Failure(WmsErrors.Validation(
                    "supplier.item_reference_values_invalid",
                    "Minimum order quantity must be positive, package units must be positive when supplied, and lead time must be between 0 and 3650 days."));
            }
        }

        return Result.Success();
    }

    private static SupplierItemReference BuildReference(SupplierItemReferenceInput input) =>
        new(
            input.ItemId,
            input.VendorSku,
            input.VendorBarcode,
            input.ItemPackagingId,
            input.VendorPackaging,
            input.UnitsPerPurchasePackage,
            input.MinimumOrderQuantity,
            input.LeadTimeDays);

    private static void ApplyItemReferences(
        Supplier supplier,
        IReadOnlyList<SupplierItemReferenceInput> inputs)
    {
        var existingById = supplier.ItemReferences.ToDictionary(reference => reference.Id);
        var existingBySku = supplier.ItemReferences.ToDictionary(
            reference => reference.VendorSku,
            StringComparer.Ordinal);
        var existingByBarcode = supplier.ItemReferences
            .Where(reference => reference.VendorBarcode is not null)
            .ToDictionary(reference => reference.VendorBarcode!, StringComparer.Ordinal);
        var retained = new HashSet<SupplierItemReference>();

        foreach (var input in inputs)
        {
            var normalizedSku = NormalizeRequired(input.VendorSku, "vendorSku");
            var normalizedBarcode = NormalizeOptionalUpper(input.VendorBarcode);
            SupplierItemReference reference;
            if (input.Id.HasValue)
            {
                if (!existingById.TryGetValue(input.Id.Value, out reference!))
                {
                    throw new ArgumentException(
                        $"Supplier item reference {input.Id.Value} does not belong to the supplier.");
                }
            }
            else if (!existingBySku.TryGetValue(normalizedSku, out reference!))
            {
                if (normalizedBarcode is null ||
                    !existingByBarcode.TryGetValue(normalizedBarcode, out reference!))
                {
                    reference = BuildReference(input);
                    supplier.AddItemReference(reference);
                }
            }

            reference.Update(
                input.ItemId,
                input.VendorSku,
                input.VendorBarcode,
                input.ItemPackagingId,
                input.VendorPackaging,
                input.UnitsPerPurchasePackage,
                input.MinimumOrderQuantity,
                input.LeadTimeDays);
            reference.SetActive(input.IsActive);
            retained.Add(reference);
        }

        foreach (var reference in supplier.ItemReferences.Where(reference => !retained.Contains(reference)))
        {
            reference.SetActive(false);
        }
    }

    private async Task<IReadOnlyList<SupplierDto>> MapToDtosAsync(
        List<Supplier> suppliers,
        CancellationToken cancellationToken)
    {
        if (suppliers.Count == 0)
        {
            return [];
        }

        var warehouseCodes = await LoadWarehouseCodesAsync(suppliers, cancellationToken);
        var dockCodes = await LoadDockCodesAsync(suppliers, cancellationToken);
        var supplierCodes = suppliers.Select(supplier => supplier.Code).ToArray();
        var defaultSupplierCodes = await context.Items
            .AsNoTracking()
            .Where(item => item.DefaultSupplierCode != null &&
                           supplierCodes.Contains(item.DefaultSupplierCode))
            .Select(item => item.DefaultSupplierCode!)
            .ToHashSetAsync(cancellationToken);

        return suppliers
            .Select(supplier => new SupplierDto(
                supplier.Id,
                supplier.Code,
                supplier.LegalName,
                supplier.LocalizedName,
                supplier.TaxRegistrationNumber,
                supplier.ExternalErpIdentifier,
                supplier.AddressLine1,
                supplier.AddressLine2,
                supplier.City,
                supplier.StateOrProvince,
                supplier.PostalCode,
                supplier.CountryCode,
                supplier.ContactName,
                supplier.ContactEmail,
                supplier.ContactPhone,
                supplier.PreferredWarehouseId,
                supplier.PreferredWarehouseId is null
                    ? null
                    : warehouseCodes.GetValueOrDefault(supplier.PreferredWarehouseId.Value),
                supplier.PreferredDockLocationId,
                supplier.PreferredDockLocationId is null
                    ? null
                    : dockCodes.GetValueOrDefault(supplier.PreferredDockLocationId.Value),
                supplier.DefaultLeadTimeDays,
                supplier.OverDeliveryTolerancePercent,
                supplier.UnderDeliveryTolerancePercent,
                supplier.RequiresLot,
                supplier.RequiresExpiry,
                supplier.QualityProfile,
                supplier.LabelRule,
                supplier.DefaultCurrencyCode,
                supplier.Notes,
                supplier.IsActive,
                supplier.CreatedAt,
                supplier.UpdatedAt,
                supplier.ItemReferences
                    .OrderBy(reference => reference.Item?.Sku)
                    .ThenBy(reference => reference.VendorSku)
                    .Select(MapReference)
                    .ToArray(),
                supplier.ItemReferences.Count == 0 &&
                !defaultSupplierCodes.Contains(supplier.Code)))
            .ToArray();
    }

    private static SupplierItemReferenceDto MapReference(SupplierItemReference reference) =>
        new(
            reference.Id,
            reference.ItemId,
            reference.Item?.Sku ?? string.Empty,
            reference.Item?.Name ?? string.Empty,
            reference.ItemPackagingId,
            reference.ItemPackaging?.Code,
            reference.VendorSku,
            reference.VendorBarcode,
            reference.VendorPackaging,
            reference.UnitsPerPurchasePackage,
            reference.MinimumOrderQuantity,
            reference.LeadTimeDays,
            reference.IsActive);

    private async Task<Dictionary<int, string>> LoadWarehouseCodesAsync(
        IReadOnlyList<Supplier> suppliers,
        CancellationToken cancellationToken)
    {
        var ids = suppliers
            .Where(supplier => supplier.PreferredWarehouseId.HasValue)
            .Select(supplier => supplier.PreferredWarehouseId!.Value)
            .Distinct()
            .ToArray();
        return await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => ids.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.Code, cancellationToken);
    }

    private async Task<Dictionary<int, string>> LoadDockCodesAsync(
        IReadOnlyList<Supplier> suppliers,
        CancellationToken cancellationToken)
    {
        var ids = suppliers
            .Where(supplier => supplier.PreferredDockLocationId.HasValue)
            .Select(supplier => supplier.PreferredDockLocationId!.Value)
            .Distinct()
            .ToArray();
        return await context.Locations
            .AsNoTracking()
            .Where(location => ids.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, location => location.Code, cancellationToken);
    }

    private async Task<Result<SupplierInput>> BuildImportInputAsync(
        IReadOnlyList<ImportedRow> rows,
        CancellationToken cancellationToken)
    {
        var first = rows[0];
        if (rows.Skip(1).Any(row => !SameProfile(first, row)))
        {
            return Result.Failure<SupplierInput>(WmsErrors.Validation(
                "supplier.import_profile_conflict",
                $"Supplier '{first.Code}' has conflicting master/default values across import rows."));
        }

        var preferredWarehouseId = await ResolveWarehouseIdAsync(
            first.PreferredWarehouseCode,
            cancellationToken);
        if (first.PreferredWarehouseCode is not null && preferredWarehouseId is null)
        {
            return Result.Failure<SupplierInput>(WmsErrors.NotFound(
                "supplier.import_warehouse_not_found",
                $"Preferred warehouse '{first.PreferredWarehouseCode}' was not found."));
        }

        var preferredDockLocationId = await ResolveDockLocationIdAsync(
            preferredWarehouseId,
            first.PreferredDockCode,
            cancellationToken);
        if (first.PreferredDockCode is not null && preferredDockLocationId is null)
        {
            return Result.Failure<SupplierInput>(WmsErrors.NotFound(
                "supplier.import_dock_not_found",
                $"Preferred dock '{first.PreferredDockCode}' was not found in the preferred warehouse."));
        }

        var references = new List<SupplierItemReferenceInput>();
        foreach (var row in rows.Where(row =>
                     !string.IsNullOrWhiteSpace(row.ItemSku) ||
                     !string.IsNullOrWhiteSpace(row.VendorSku)))
        {
            if (string.IsNullOrWhiteSpace(row.ItemSku) || string.IsNullOrWhiteSpace(row.VendorSku))
            {
                return Result.Failure<SupplierInput>(WmsErrors.Validation(
                    "supplier.import_reference_invalid",
                    $"Supplier '{first.Code}' has an item-reference row without both ITEM_SKU and VENDOR_SKU."));
            }

            var itemSku = NormalizeRequired(row.ItemSku, "itemSku");
            var item = await context.Items
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Sku == itemSku, cancellationToken);
            if (item is null)
            {
                return Result.Failure<SupplierInput>(WmsErrors.NotFound(
                    "supplier.import_item_not_found",
                    $"Item '{itemSku}' was not found."));
            }

            int? packagingId = null;
            if (row.ItemPackagingCode is not null)
            {
                var packagingCode = NormalizeRequired(row.ItemPackagingCode, "itemPackagingCode");
                packagingId = await context.ItemPackagings
                    .AsNoTracking()
                    .Where(packaging => packaging.ItemId == item.Id && packaging.Code == packagingCode)
                    .Select(packaging => (int?)packaging.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                if (!packagingId.HasValue)
                {
                    return Result.Failure<SupplierInput>(WmsErrors.NotFound(
                        "supplier.import_packaging_not_found",
                        $"Packaging '{packagingCode}' was not found for item '{itemSku}'."));
                }
            }

            references.Add(new SupplierItemReferenceInput(
                null,
                item.Id,
                row.VendorSku!,
                row.VendorBarcode,
                packagingId,
                row.VendorPackaging,
                row.UnitsPerPurchasePackage,
                row.MinimumOrderQuantity,
                row.ReferenceLeadTimeDays,
                row.ReferenceActive));
        }

        return Result.Success(new SupplierInput(
            first.Code,
            first.LegalName,
            first.LocalizedName,
            first.TaxRegistrationNumber,
            first.ExternalErpIdentifier,
            first.AddressLine1,
            first.AddressLine2,
            first.City,
            first.StateOrProvince,
            first.PostalCode,
            first.CountryCode,
            first.ContactName,
            first.ContactEmail,
            first.ContactPhone,
            new SupplierReceivingDefaultsInput(
                preferredWarehouseId,
                preferredDockLocationId,
                first.DefaultLeadTimeDays,
                first.OverDeliveryTolerancePercent,
                first.UnderDeliveryTolerancePercent,
                first.RequiresLot,
                first.RequiresExpiry,
                first.QualityProfile,
                first.LabelRule,
                first.DefaultCurrencyCode),
            first.Notes,
            first.IsActive,
            references));
    }

    private async Task<int?> ResolveWarehouseIdAsync(
        string? warehouseCode,
        CancellationToken cancellationToken)
    {
        if (warehouseCode is null)
        {
            return null;
        }

        var normalizedCode = NormalizeRequired(warehouseCode, "preferredWarehouseCode");
        return await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.Code == normalizedCode)
            .Select(warehouse => (int?)warehouse.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<int?> ResolveDockLocationIdAsync(
        int? warehouseId,
        string? dockCode,
        CancellationToken cancellationToken)
    {
        if (dockCode is null || !warehouseId.HasValue)
        {
            return null;
        }

        var normalizedCode = NormalizeRequired(dockCode, "preferredDockCode");
        return await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == warehouseId.Value &&
                               location.Code == normalizedCode &&
                               location.IsReceivable)
            .Select(location => (int?)location.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static bool SameProfile(ImportedRow first, ImportedRow other) =>
        string.Equals(first.LegalName, other.LegalName, StringComparison.Ordinal) &&
        string.Equals(first.LocalizedName, other.LocalizedName, StringComparison.Ordinal) &&
        string.Equals(first.TaxRegistrationNumber, other.TaxRegistrationNumber, StringComparison.Ordinal) &&
        string.Equals(first.ExternalErpIdentifier, other.ExternalErpIdentifier, StringComparison.Ordinal) &&
        string.Equals(first.AddressLine1, other.AddressLine1, StringComparison.Ordinal) &&
        string.Equals(first.AddressLine2, other.AddressLine2, StringComparison.Ordinal) &&
        string.Equals(first.City, other.City, StringComparison.Ordinal) &&
        string.Equals(first.StateOrProvince, other.StateOrProvince, StringComparison.Ordinal) &&
        string.Equals(first.PostalCode, other.PostalCode, StringComparison.Ordinal) &&
        string.Equals(first.CountryCode, other.CountryCode, StringComparison.Ordinal) &&
        string.Equals(first.ContactName, other.ContactName, StringComparison.Ordinal) &&
        string.Equals(first.ContactEmail, other.ContactEmail, StringComparison.Ordinal) &&
        string.Equals(first.ContactPhone, other.ContactPhone, StringComparison.Ordinal) &&
        string.Equals(first.PreferredWarehouseCode, other.PreferredWarehouseCode, StringComparison.Ordinal) &&
        string.Equals(first.PreferredDockCode, other.PreferredDockCode, StringComparison.Ordinal) &&
        first.DefaultLeadTimeDays == other.DefaultLeadTimeDays &&
        first.OverDeliveryTolerancePercent == other.OverDeliveryTolerancePercent &&
        first.UnderDeliveryTolerancePercent == other.UnderDeliveryTolerancePercent &&
        first.RequiresLot == other.RequiresLot &&
        first.RequiresExpiry == other.RequiresExpiry &&
        string.Equals(first.QualityProfile, other.QualityProfile, StringComparison.Ordinal) &&
        string.Equals(first.LabelRule, other.LabelRule, StringComparison.Ordinal) &&
        string.Equals(first.DefaultCurrencyCode, other.DefaultCurrencyCode, StringComparison.Ordinal) &&
        string.Equals(first.Notes, other.Notes, StringComparison.Ordinal) &&
        first.IsActive == other.IsActive;

    private static ParsedImport ParseImport(string csv)
    {
        var rows = new List<ImportedRow>();
        var errors = new List<SupplierImportError>();
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
            if (fields.Count < 26)
            {
                errors.Add(new SupplierImportError(
                    rowNumber,
                    "Expected at least the 26 supplier master columns; item-reference columns are optional."));
                continue;
            }

            var code = NullIfEmpty(fields.ElementAtOrDefault(0));
            var legalName = NullIfEmpty(fields.ElementAtOrDefault(1));
            if (code is null || legalName is null)
            {
                errors.Add(new SupplierImportError(rowNumber, "CODE and LEGAL_NAME are required."));
                continue;
            }

            rows.Add(new ImportedRow(
                rowNumber,
                code,
                legalName,
                NullIfEmpty(fields.ElementAtOrDefault(2)),
                NullIfEmpty(fields.ElementAtOrDefault(3)),
                NullIfEmpty(fields.ElementAtOrDefault(4)),
                NullIfEmpty(fields.ElementAtOrDefault(5)),
                NullIfEmpty(fields.ElementAtOrDefault(6)),
                NullIfEmpty(fields.ElementAtOrDefault(7)),
                NullIfEmpty(fields.ElementAtOrDefault(8)),
                NullIfEmpty(fields.ElementAtOrDefault(9)),
                NullIfEmpty(fields.ElementAtOrDefault(10)),
                NullIfEmpty(fields.ElementAtOrDefault(11)),
                NullIfEmpty(fields.ElementAtOrDefault(12)),
                NullIfEmpty(fields.ElementAtOrDefault(13)),
                ParseBool(fields.ElementAtOrDefault(14), true, rowNumber, "IS_ACTIVE", errors),
                NullIfEmpty(fields.ElementAtOrDefault(15))?.ToUpperInvariant(),
                NullIfEmpty(fields.ElementAtOrDefault(16))?.ToUpperInvariant(),
                ParseNullableInt(fields.ElementAtOrDefault(17), rowNumber, "DEFAULT_LEAD_TIME_DAYS", errors),
                ParseNullableDecimal(fields.ElementAtOrDefault(18), rowNumber, "OVER_DELIVERY_TOLERANCE_PERCENT", errors),
                ParseNullableDecimal(fields.ElementAtOrDefault(19), rowNumber, "UNDER_DELIVERY_TOLERANCE_PERCENT", errors),
                ParseBool(fields.ElementAtOrDefault(20), false, rowNumber, "REQUIRES_LOT", errors),
                ParseBool(fields.ElementAtOrDefault(21), false, rowNumber, "REQUIRES_EXPIRY", errors),
                NullIfEmpty(fields.ElementAtOrDefault(22)),
                NullIfEmpty(fields.ElementAtOrDefault(23)),
                NullIfEmpty(fields.ElementAtOrDefault(24))?.ToUpperInvariant(),
                NullIfEmpty(fields.ElementAtOrDefault(25)),
                NullIfEmpty(fields.ElementAtOrDefault(26))?.ToUpperInvariant(),
                NullIfEmpty(fields.ElementAtOrDefault(27)),
                NullIfEmpty(fields.ElementAtOrDefault(28))?.ToUpperInvariant(),
                NullIfEmpty(fields.ElementAtOrDefault(29))?.ToUpperInvariant(),
                NullIfEmpty(fields.ElementAtOrDefault(30)),
                ParseNullableDecimal(fields.ElementAtOrDefault(31), rowNumber, "UNITS_PER_PURCHASE_PACKAGE", errors),
                ParseDecimal(fields.ElementAtOrDefault(32), 1, rowNumber, "MINIMUM_ORDER_QUANTITY", errors),
                ParseNullableInt(fields.ElementAtOrDefault(33), rowNumber, "REFERENCE_LEAD_TIME_DAYS", errors),
                ParseBool(fields.ElementAtOrDefault(34), true, rowNumber, "REFERENCE_ACTIVE", errors)));
        }

        return new ParsedImport(rows, errors);
    }

    private static bool ParseBool(
        string? value,
        bool defaultValue,
        int row,
        string field,
        List<SupplierImportError> errors) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.TryParse(value, out var parsed)
                ? parsed
                : AddParseError(field, row, errors, defaultValue);

    private static int? ParseNullableInt(
        string? value,
        int row,
        string field,
        List<SupplierImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError(field, row, errors, (int?)null);
    }

    private static decimal? ParseNullableDecimal(
        string? value,
        int row,
        string field,
        List<SupplierImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError(field, row, errors, (decimal?)null);
    }

    private static decimal ParseDecimal(
        string? value,
        decimal defaultValue,
        int row,
        string field,
        List<SupplierImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError(field, row, errors, defaultValue);
    }

    private static T AddParseError<T>(
        string field,
        int row,
        List<SupplierImportError> errors,
        T fallback)
    {
        errors.Add(new SupplierImportError(row, $"{field} has an invalid value."));
        return fallback;
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

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalUpper(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',', StringComparison.Ordinal) ||
               value.Contains('"', StringComparison.Ordinal) ||
               value.Contains('\n', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static Dictionary<string, object?> AuditSnapshot(Supplier supplier) =>
        new(StringComparer.Ordinal)
        {
            ["code"] = supplier.Code,
            ["legalName"] = supplier.LegalName,
            ["localizedName"] = supplier.LocalizedName,
            ["externalErpIdentifier"] = supplier.ExternalErpIdentifier,
            ["preferredWarehouseId"] = supplier.PreferredWarehouseId,
            ["preferredDockLocationId"] = supplier.PreferredDockLocationId,
            ["defaultLeadTimeDays"] = supplier.DefaultLeadTimeDays,
            ["overDeliveryTolerancePercent"] = supplier.OverDeliveryTolerancePercent,
            ["underDeliveryTolerancePercent"] = supplier.UnderDeliveryTolerancePercent,
            ["requiresLot"] = supplier.RequiresLot,
            ["requiresExpiry"] = supplier.RequiresExpiry,
            ["qualityProfile"] = supplier.QualityProfile,
            ["labelRule"] = supplier.LabelRule,
            ["defaultCurrencyCode"] = supplier.DefaultCurrencyCode,
            ["isActive"] = supplier.IsActive,
            ["itemReferenceCount"] = supplier.ItemReferences.Count
        };

    private sealed record ImportedRow(
        int RowNumber,
        string Code,
        string LegalName,
        string? LocalizedName,
        string? TaxRegistrationNumber,
        string? ExternalErpIdentifier,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? StateOrProvince,
        string? PostalCode,
        string? CountryCode,
        string? ContactName,
        string? ContactEmail,
        string? ContactPhone,
        bool IsActive,
        string? PreferredWarehouseCode,
        string? PreferredDockCode,
        int? DefaultLeadTimeDays,
        decimal? OverDeliveryTolerancePercent,
        decimal? UnderDeliveryTolerancePercent,
        bool RequiresLot,
        bool RequiresExpiry,
        string? QualityProfile,
        string? LabelRule,
        string? DefaultCurrencyCode,
        string? Notes,
        string? ItemSku,
        string? VendorSku,
        string? VendorBarcode,
        string? ItemPackagingCode,
        string? VendorPackaging,
        decimal? UnitsPerPurchasePackage,
        decimal MinimumOrderQuantity,
        int? ReferenceLeadTimeDays,
        bool ReferenceActive);

    private sealed class ParsedImport(
        List<ImportedRow> rows,
        List<SupplierImportError> errors)
    {
        public List<ImportedRow> Rows { get; } = rows;
        public List<SupplierImportError> Errors { get; } = errors;
    }
}
