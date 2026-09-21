using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Customers;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Customers;

public sealed class CustomerManagementService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<CustomerManagementService> logger) : ICustomerManagementService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;
    private const int MaximumShipToAddresses = 1_000;
    private const int MaximumItemReferences = 1_000;

    public async Task<Result<CustomerPageDto>> ListAsync(
        CustomerListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerPageDto>();
        }

        var query = ApplyFilters(QueryCustomers(), request);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var customers = await ApplyOrdering(query, request)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new CustomerPageDto(
            customers.Select(MapCustomer).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<CustomerDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerDto>();
        }

        var customer = await LoadCustomerAsync(id, asNoTracking: true, cancellationToken);
        return customer is null
            ? Result.Failure<CustomerDto>(WmsErrors.NotFound(
                "customer.not_found",
                "The requested customer was not found."))
            : Result.Success(MapCustomer(customer));
    }

    public async Task<Result<CustomerDto>> CreateAsync(
        CustomerInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerDto>();
        }

        try
        {
            var code = NormalizeRequired(input.Code, "code");
            var duplicate = await FindIdentifierConflictAsync(
                code,
                input.ExternalErpIdentifier,
                input.ExternalChannelIdentifier,
                customerId: null,
                cancellationToken);
            if (duplicate is not null)
            {
                return Result.Failure<CustomerDto>(duplicate);
            }

            var validation = await ValidateReferencesAsync(
                input.ShipToAddresses,
                input.ItemReferences,
                cancellationToken);
            if (validation.IsFailure)
            {
                return validation.ToFailure<CustomerDto>();
            }

            var customer = new Customer(
                code,
                input.LegalName,
                input.LocalizedName,
                input.TaxRegistrationNumber,
                NormalizeOptionalUpper(input.ExternalErpIdentifier),
                NormalizeOptionalUpper(input.ExternalChannelIdentifier),
                input.ContactName,
                input.ContactEmail,
                input.ContactPhone,
                input.BillingAddressLine1,
                input.BillingAddressLine2,
                input.BillingCity,
                input.BillingRegion,
                input.BillingPostalCode,
                input.BillingCountryCode,
                input.DefaultCarrierCode,
                input.DefaultCarrierServiceCode,
                input.Priority,
                input.PackagingProfile,
                input.LabelProfile,
                input.AllowPartialShipment,
                input.Notes);
            if (!input.IsActive)
            {
                customer.Deactivate();
            }

            AddShipToAddresses(customer, input.ShipToAddresses ?? []);
            AddItemReferences(customer, input.ItemReferences ?? []);
            context.Customers.Add(customer);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CustomerCreated,
                    WmsAuditEntityTypes.Customer,
                    code,
                    After: AuditSnapshot(customer),
                    ActorUserId: userId),
                cancellationToken);
            await RecordChildAuditAsync(customer, userId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var saved = await LoadCustomerAsync(customer.Id, asNoTracking: true, cancellationToken);
            logger.LogInformation("Customer master created for {CustomerCode} by {UserId}", code, userId);
            return Result.Success(MapCustomer(saved!));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CustomerDto>(WmsErrors.Validation(
                "customer.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Customer creation failed for {CustomerCode}", input.Code);
            return Result.Failure<CustomerDto>(WmsErrors.FromException(
                exception,
                "customer.create_failed",
                "The customer could not be created."));
        }
    }

    public async Task<Result<CustomerDto>> UpdateAsync(
        int id,
        CustomerInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerDto>();
        }

        try
        {
            var customer = await LoadCustomerAsync(id, asNoTracking: false, cancellationToken);
            if (customer is null)
            {
                return Result.Failure<CustomerDto>(WmsErrors.NotFound(
                    "customer.not_found",
                    "The requested customer was not found."));
            }

            var code = NormalizeRequired(input.Code, "code");
            if (!string.Equals(code, customer.Code, StringComparison.Ordinal))
            {
                return Result.Failure<CustomerDto>(WmsErrors.Conflict(
                    "customer.code_immutable",
                    "Customer codes cannot change after the customer is created."));
            }

            var duplicate = await FindIdentifierConflictAsync(
                code,
                input.ExternalErpIdentifier,
                input.ExternalChannelIdentifier,
                id,
                cancellationToken);
            if (duplicate is not null)
            {
                return Result.Failure<CustomerDto>(duplicate);
            }

            var validation = await ValidateReferencesAsync(
                input.ShipToAddresses,
                input.ItemReferences,
                cancellationToken);
            if (validation.IsFailure)
            {
                return validation.ToFailure<CustomerDto>();
            }

            var before = AuditSnapshot(customer);
            customer.UpdateProfile(
                input.LegalName,
                input.LocalizedName,
                input.TaxRegistrationNumber,
                NormalizeOptionalUpper(input.ExternalErpIdentifier),
                NormalizeOptionalUpper(input.ExternalChannelIdentifier),
                input.ContactName,
                input.ContactEmail,
                input.ContactPhone,
                input.BillingAddressLine1,
                input.BillingAddressLine2,
                input.BillingCity,
                input.BillingRegion,
                input.BillingPostalCode,
                input.BillingCountryCode,
                input.DefaultCarrierCode,
                input.DefaultCarrierServiceCode,
                input.Priority,
                input.PackagingProfile,
                input.LabelProfile,
                input.AllowPartialShipment,
                input.Notes);
            if (input.IsActive)
            {
                customer.Activate();
            }
            else
            {
                customer.Deactivate();
            }

            if (input.ShipToAddresses is not null)
            {
                ApplyShipToAddresses(customer, input.ShipToAddresses);
            }

            if (input.ItemReferences is not null)
            {
                ApplyItemReferences(customer, input.ItemReferences);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CustomerUpdated,
                    WmsAuditEntityTypes.Customer,
                    customer.Code,
                    Before: before,
                    After: AuditSnapshot(customer),
                    ActorUserId: userId),
                cancellationToken);
            await RecordChildAuditAsync(customer, userId, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            var saved = await LoadCustomerAsync(id, asNoTracking: true, cancellationToken);
            return Result.Success(MapCustomer(saved!));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<CustomerDto>(WmsErrors.Validation(
                "customer.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Customer update failed for {CustomerId}", id);
            return Result.Failure<CustomerDto>(WmsErrors.FromException(
                exception,
                "customer.update_failed",
                "The customer could not be updated."));
        }
    }

    public async Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var customer = await LoadCustomerAsync(id, asNoTracking: false, cancellationToken);
        if (customer is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "customer.not_found",
                "The requested customer was not found."));
        }

        var before = AuditSnapshot(customer);
        if (active)
        {
            customer.Activate();
        }
        else
        {
            customer.Deactivate();
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                active ? WmsAuditActions.CustomerActivated : WmsAuditActions.CustomerDeactivated,
                WmsAuditEntityTypes.Customer,
                customer.Code,
                Before: before,
                After: AuditSnapshot(customer),
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
        var authorization = await AuthorizeManageAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        var customer = await LoadCustomerAsync(id, asNoTracking: false, cancellationToken);
        if (customer is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "customer.not_found",
                "The requested customer was not found."));
        }

        if (customer.ShipToAddresses.Count > 0 || customer.ItemReferences.Count > 0)
        {
            return Result.Failure(WmsErrors.Conflict(
                "customer.referenced",
                "The customer cannot be deleted because ship-to or item-reference history is attached. Deactivate it instead."));
        }

        context.Customers.Remove(customer);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.CustomerDeleted,
                WmsAuditEntityTypes.Customer,
                customer.Code,
                Before: AuditSnapshot(customer),
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CustomerImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerImportResult>();
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000)
        {
            return Result.Failure<CustomerImportResult>(WmsErrors.Validation(
                "customer.import_empty",
                "Provide a non-empty customer CSV no larger than 2 MB."));
        }

        var parsed = ParseImport(csv);
        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<CustomerImportResult>(WmsErrors.Validation(
                "customer.import_invalid",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        if (parsed.Rows.Count == 0)
        {
            return Result.Failure<CustomerImportResult>(WmsErrors.Validation(
                "customer.import_empty",
                "The customer CSV did not contain any data rows."));
        }

        var importedCustomers = 0;
        var importedShipTo = 0;
        foreach (var group in parsed.Rows.GroupBy(row => row.Code, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Skip(1).Any(row => !SameMasterProfile(first, row)))
            {
                return Result.Failure<CustomerImportResult>(WmsErrors.Validation(
                    "customer.import_profile_conflict",
                    $"Customer '{first.Code}' has conflicting master values across import rows."));
            }

            var input = new CustomerInput(
                first.Code,
                first.LegalName,
                first.LocalizedName,
                first.TaxRegistrationNumber,
                first.ExternalErpIdentifier,
                first.ExternalChannelIdentifier,
                first.ContactName,
                first.ContactEmail,
                first.ContactPhone,
                first.BillingAddressLine1,
                first.BillingAddressLine2,
                first.BillingCity,
                first.BillingRegion,
                first.BillingPostalCode,
                first.BillingCountryCode,
                first.DefaultCarrierCode,
                first.DefaultCarrierServiceCode,
                first.Priority,
                first.PackagingProfile,
                first.LabelProfile,
                first.AllowPartialShipment,
                first.Notes,
                first.IsActive,
                group.Where(row => row.ShipToCode is not null)
                    .Select(row => new CustomerShipToAddressInput(
                        null,
                        row.ShipToCode!,
                        row.ShipToRecipientName!,
                        row.ShipToPhone,
                        row.ShipToCountryCode,
                        row.ShipToRegion,
                        row.ShipToCity,
                        row.ShipToPostalCode,
                        row.ShipToAddressLine1!,
                        row.ShipToAddressLine2,
                        row.ShipToDeliveryInstructions,
                        row.ShipToWindowStart,
                        row.ShipToWindowEnd,
                        row.ShipToIsDefault,
                        row.ShipToIsActive))
                    .ToArray());

            var result = await CreateAsync(input, userId, cancellationToken);
            if (result.IsFailure)
            {
                return result.ToFailure<CustomerImportResult>();
            }

            importedCustomers++;
            importedShipTo += result.Value.ShipToAddresses.Count;
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.CustomerBulkImported,
                WmsAuditEntityTypes.Customer,
                After: new Dictionary<string, object?>
                {
                    ["customerCount"] = importedCustomers,
                    ["shipToCount"] = importedShipTo
                },
                ActorUserId: userId),
            cancellationToken);

        return Result.Success(new CustomerImportResult(importedCustomers, importedShipTo, []));
    }

    public async Task<Result<string>> ExportAsync(
        CustomerListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var customers = await ApplyOrdering(
                ApplyFilters(QueryCustomers(), request),
                request)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine(
            "CODE,LEGAL_NAME,LOCALIZED_NAME,TAX_REGISTRATION_NUMBER,EXTERNAL_ERP_IDENTIFIER,EXTERNAL_CHANNEL_IDENTIFIER,CONTACT_NAME,CONTACT_EMAIL,CONTACT_PHONE,BILLING_ADDRESS_LINE_1,BILLING_ADDRESS_LINE_2,BILLING_CITY,BILLING_REGION,BILLING_POSTAL_CODE,BILLING_COUNTRY_CODE,DEFAULT_CARRIER_CODE,DEFAULT_CARRIER_SERVICE_CODE,PRIORITY,PACKAGING_PROFILE,LABEL_PROFILE,ALLOW_PARTIAL_SHIPMENT,NOTES,IS_ACTIVE,SHIP_TO_CODE,SHIP_TO_RECIPIENT_NAME,SHIP_TO_PHONE,SHIP_TO_COUNTRY_CODE,SHIP_TO_REGION,SHIP_TO_CITY,SHIP_TO_POSTAL_CODE,SHIP_TO_ADDRESS_LINE_1,SHIP_TO_ADDRESS_LINE_2,SHIP_TO_DELIVERY_INSTRUCTIONS,SHIP_TO_WINDOW_START,SHIP_TO_WINDOW_END,SHIP_TO_IS_DEFAULT,SHIP_TO_IS_ACTIVE");
        foreach (var customer in customers)
        {
            var addresses = customer.ShipToAddresses.Count == 0
                ? new CustomerShipToAddress?[] { null }
                : customer.ShipToAddresses.OrderBy(address => address.Code).Cast<CustomerShipToAddress?>().ToArray();
            foreach (var address in addresses)
            {
                builder.AppendLine(string.Join(",", [
                    EscapeCsv(customer.Code),
                    EscapeCsv(customer.LegalName),
                    EscapeCsv(customer.LocalizedName),
                    EscapeCsv(customer.TaxRegistrationNumber),
                    EscapeCsv(customer.ExternalErpIdentifier),
                    EscapeCsv(customer.ExternalChannelIdentifier),
                    EscapeCsv(customer.ContactName),
                    EscapeCsv(customer.ContactEmail),
                    EscapeCsv(customer.ContactPhone),
                    EscapeCsv(customer.BillingAddressLine1),
                    EscapeCsv(customer.BillingAddressLine2),
                    EscapeCsv(customer.BillingCity),
                    EscapeCsv(customer.BillingRegion),
                    EscapeCsv(customer.BillingPostalCode),
                    EscapeCsv(customer.BillingCountryCode),
                    EscapeCsv(customer.DefaultCarrierCode),
                    EscapeCsv(customer.DefaultCarrierServiceCode),
                    customer.Priority.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(customer.PackagingProfile),
                    EscapeCsv(customer.LabelProfile),
                    customer.AllowPartialShipment.ToString(),
                    EscapeCsv(customer.Notes),
                    customer.IsActive.ToString(),
                    EscapeCsv(address?.Code),
                    EscapeCsv(address?.RecipientName),
                    EscapeCsv(address?.Phone),
                    EscapeCsv(address?.CountryCode),
                    EscapeCsv(address?.Region),
                    EscapeCsv(address?.City),
                    EscapeCsv(address?.PostalCode),
                    EscapeCsv(address?.AddressLine1),
                    EscapeCsv(address?.AddressLine2),
                    EscapeCsv(address?.DeliveryInstructions),
                    address?.DeliveryWindowStart?.ToString(),
                    address?.DeliveryWindowEnd?.ToString(),
                    address?.IsDefault.ToString() ?? string.Empty,
                    address?.IsActive.ToString() ?? string.Empty
                ]));
            }
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result<CustomerDocumentSnapshot>> GetDocumentSnapshotAsync(
        CustomerDocumentSnapshotQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerDocumentSnapshot>();
        }

        var customer = await LoadCustomerAsync(query.CustomerId, asNoTracking: true, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<CustomerDocumentSnapshot>(WmsErrors.NotFound(
                "customer.not_found",
                "The requested customer was not found."));
        }

        if (!customer.IsActive)
        {
            return Result.Failure<CustomerDocumentSnapshot>(WmsErrors.Conflict(
                "customer.inactive",
                "Inactive customers cannot be selected for a new outbound document."));
        }

        var activeAddresses = customer.ShipToAddresses
            .Where(address => address.IsActive)
            .OrderBy(address => address.Code, StringComparer.Ordinal)
            .ToArray();
        CustomerShipToAddress? selected = null;
        if (query.ShipToAddressId.HasValue)
        {
            selected = activeAddresses.SingleOrDefault(address => address.Id == query.ShipToAddressId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(query.ShipToCode))
        {
            var code = NormalizeRequired(query.ShipToCode, "shipToCode");
            selected = activeAddresses.SingleOrDefault(address => address.Code == code);
        }
        else
        {
            selected = activeAddresses.SingleOrDefault(address => address.IsDefault);
            if (selected is null && activeAddresses.Length == 1)
            {
                selected = activeAddresses[0];
            }
        }

        if ((query.ShipToAddressId.HasValue || !string.IsNullOrWhiteSpace(query.ShipToCode)) && selected is null)
        {
            return Result.Failure<CustomerDocumentSnapshot>(WmsErrors.NotFound(
                "customer.ship_to_not_found",
                "The requested active ship-to address was not found."));
        }

        if (selected is null && activeAddresses.Length > 1)
        {
            return Result.Failure<CustomerDocumentSnapshot>(WmsErrors.Conflict(
                "customer.ship_to_required",
                "Select a ship-to address because this customer has more than one active destination and no default."));
        }

        return Result.Success(new CustomerDocumentSnapshot(
            customer.Id,
            customer.Code,
            customer.LegalName,
            customer.LocalizedName,
            customer.ContactName,
            customer.ContactEmail,
            customer.ContactPhone,
            selected?.Id,
            selected?.Code,
            selected?.RecipientName,
            selected?.Phone,
            selected?.CountryCode,
            selected?.Region,
            selected?.City,
            selected?.PostalCode,
            selected?.AddressLine1,
            selected?.AddressLine2,
            selected?.DeliveryInstructions,
            selected?.DeliveryWindowStart,
            selected?.DeliveryWindowEnd,
            customer.DefaultCarrierCode,
            customer.DefaultCarrierServiceCode,
            customer.Priority,
            customer.PackagingProfile,
            customer.LabelProfile,
            customer.AllowPartialShipment));
    }

    public async Task<Result<CustomerItemReferenceResolutionDto>> ResolveItemReferenceAsync(
        CustomerItemReferenceResolutionQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<CustomerItemReferenceResolutionDto>();
        }

        var customer = await LoadCustomerAsync(query.CustomerId, asNoTracking: true, cancellationToken);
        if (customer is null)
        {
            return Result.Failure<CustomerItemReferenceResolutionDto>(WmsErrors.NotFound(
                "customer.not_found",
                "The requested customer was not found."));
        }

        if (!customer.IsActive && !query.IncludeInactive)
        {
            return Result.Failure<CustomerItemReferenceResolutionDto>(WmsErrors.Conflict(
                "customer.inactive",
                "Inactive customers cannot be selected for a new outbound document."));
        }

        var sku = NormalizeOptionalUpper(query.CustomerSku);
        var barcode = NormalizeOptionalUpper(query.CustomerBarcode);
        if (sku is null && barcode is null)
        {
            return Result.Failure<CustomerItemReferenceResolutionDto>(WmsErrors.Validation(
                "customer.item_reference_query_required",
                "Provide a customer SKU or customer barcode."));
        }

        var reference = customer.ItemReferences.SingleOrDefault(candidate =>
            (sku is not null && candidate.CustomerSku == sku) ||
            (barcode is not null && candidate.CustomerBarcode == barcode));
        if (reference is null)
        {
            return Result.Failure<CustomerItemReferenceResolutionDto>(WmsErrors.NotFound(
                "customer.item_reference_not_found",
                "The customer item reference was not found."));
        }

        if (!reference.IsActive && !query.IncludeInactive)
        {
            return Result.Failure<CustomerItemReferenceResolutionDto>(WmsErrors.Conflict(
                "customer.item_reference_inactive",
                "The customer item reference is inactive."));
        }

        return Result.Success(new CustomerItemReferenceResolutionDto(
            customer.Id,
            customer.Code,
            reference.ItemId,
            reference.Item?.Sku ?? string.Empty,
            reference.Item?.Name ?? string.Empty,
            reference.CustomerSku,
            reference.CustomerBarcode,
            reference.CustomerDescription,
            reference.IsActive));
    }

    private IQueryable<Customer> QueryCustomers(bool asNoTracking = true)
    {
        var query = context.Customers.AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query
            .AsSplitQuery()
            .Include(customer => customer.ShipToAddresses)
            .Include(customer => customer.ItemReferences)
            .ThenInclude(reference => reference.Item);
    }

    private Task<Customer?> LoadCustomerAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken) =>
        QueryCustomers(asNoTracking).SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken);

    private IQueryable<Customer> ApplyFilters(IQueryable<Customer> query, CustomerListQuery request)
    {
        if (!request.IncludeInactive)
        {
            query = query.Where(customer => customer.IsActive);
        }

        if (string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            return query;
        }

        var pattern = $"%{request.SearchTerm.Trim()}%";
        return string.Equals(context.Database.ProviderName, PostgreSqlProviderName, StringComparison.Ordinal)
            ? query.Where(customer =>
                EF.Functions.ILike(customer.Code, pattern) ||
                EF.Functions.ILike(customer.LegalName, pattern) ||
                (customer.LocalizedName != null && EF.Functions.ILike(customer.LocalizedName, pattern)) ||
                (customer.ExternalErpIdentifier != null && EF.Functions.ILike(customer.ExternalErpIdentifier, pattern)) ||
                (customer.ExternalChannelIdentifier != null && EF.Functions.ILike(customer.ExternalChannelIdentifier, pattern)) ||
                customer.ShipToAddresses.Any(address =>
                    EF.Functions.ILike(address.Code, pattern) ||
                    EF.Functions.ILike(address.RecipientName, pattern) ||
                    (address.City != null && EF.Functions.ILike(address.City, pattern))) ||
                customer.ItemReferences.Any(reference =>
                    EF.Functions.ILike(reference.CustomerSku, pattern) ||
                    (reference.CustomerBarcode != null && EF.Functions.ILike(reference.CustomerBarcode, pattern))))
            : query.Where(customer =>
                EF.Functions.Like(customer.Code, pattern) ||
                EF.Functions.Like(customer.LegalName, pattern) ||
                (customer.LocalizedName != null && EF.Functions.Like(customer.LocalizedName, pattern)) ||
                (customer.ExternalErpIdentifier != null && EF.Functions.Like(customer.ExternalErpIdentifier, pattern)) ||
                (customer.ExternalChannelIdentifier != null && EF.Functions.Like(customer.ExternalChannelIdentifier, pattern)) ||
                customer.ShipToAddresses.Any(address =>
                    EF.Functions.Like(address.Code, pattern) ||
                    EF.Functions.Like(address.RecipientName, pattern) ||
                    (address.City != null && EF.Functions.Like(address.City, pattern))) ||
                customer.ItemReferences.Any(reference =>
                    EF.Functions.Like(reference.CustomerSku, pattern) ||
                    (reference.CustomerBarcode != null && EF.Functions.Like(reference.CustomerBarcode, pattern))));
    }

    private static IOrderedQueryable<Customer> ApplyOrdering(IQueryable<Customer> query, CustomerListQuery request)
    {
        var ordered = request.SortBy switch
        {
            CustomerSortField.LegalName => request.Descending
                ? query.OrderByDescending(customer => customer.LegalName)
                : query.OrderBy(customer => customer.LegalName),
            CustomerSortField.Priority => request.Descending
                ? query.OrderByDescending(customer => customer.Priority)
                : query.OrderBy(customer => customer.Priority),
            CustomerSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(customer => customer.UpdatedAt)
                : query.OrderBy(customer => customer.UpdatedAt),
            CustomerSortField.CreatedAt => request.Descending
                ? query.OrderByDescending(customer => customer.CreatedAt)
                : query.OrderBy(customer => customer.CreatedAt),
            _ => request.Descending
                ? query.OrderByDescending(customer => customer.Code)
                : query.OrderBy(customer => customer.Code)
        };

        return ordered.ThenBy(customer => customer.Id);
    }

    private async Task<Result> AuthorizeReadAsync(CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(WmsPermissions.CustomersRead, cancellationToken: cancellationToken);

    private async Task<Result> AuthorizeManageAsync(CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(WmsPermissions.CustomersManage, cancellationToken: cancellationToken);

    private async Task<ResultError?> FindIdentifierConflictAsync(
        string code,
        string? externalErpIdentifier,
        string? externalChannelIdentifier,
        int? customerId,
        CancellationToken cancellationToken)
    {
        var normalizedErp = NormalizeOptionalUpper(externalErpIdentifier);
        var normalizedChannel = NormalizeOptionalUpper(externalChannelIdentifier);
        if (await context.Customers.AnyAsync(
                customer => customer.Id != customerId && customer.Code == code,
                cancellationToken))
        {
            return WmsErrors.Conflict("customer.code_conflict", $"Customer code '{code}' already exists.");
        }

        if (normalizedErp is not null && await context.Customers.AnyAsync(
                customer => customer.Id != customerId && customer.ExternalErpIdentifier == normalizedErp,
                cancellationToken))
        {
            return WmsErrors.Conflict(
                "customer.external_erp_conflict",
                $"External ERP identifier '{normalizedErp}' is already assigned.");
        }

        if (normalizedChannel is not null && await context.Customers.AnyAsync(
                customer => customer.Id != customerId && customer.ExternalChannelIdentifier == normalizedChannel,
                cancellationToken))
        {
            return WmsErrors.Conflict(
                "customer.external_channel_conflict",
                $"External channel identifier '{normalizedChannel}' is already assigned.");
        }

        return null;
    }

    private async Task<Result> ValidateReferencesAsync(
        IReadOnlyList<CustomerShipToAddressInput>? addresses,
        IReadOnlyList<CustomerItemReferenceInput>? references,
        CancellationToken cancellationToken)
    {
        if (addresses is not null)
        {
            if (addresses.Count > MaximumShipToAddresses)
            {
                return Result.Failure(WmsErrors.Validation(
                    "customer.ship_to_limit",
                    $"A customer cannot contain more than {MaximumShipToAddresses} ship-to addresses."));
            }

            var duplicateCode = addresses
                .GroupBy(address => NormalizeOptionalUpper(address.Code) ?? string.Empty, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
            if (duplicateCode is not null)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "customer.ship_to_code_conflict",
                    $"Ship-to code '{duplicateCode.Key}' appears more than once for the customer."));
            }

            if (addresses.Count(address => address.IsDefault) > 1)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "customer.ship_to_default_conflict",
                    "A customer can have only one default ship-to address."));
            }

            if (addresses.Any(address => address.IsDefault && !address.IsActive))
            {
                return Result.Failure(WmsErrors.Validation(
                    "customer.ship_to_default_inactive",
                    "The default ship-to address must be active."));
            }
        }

        if (references is null)
        {
            return Result.Success();
        }

        if (references.Count > MaximumItemReferences)
        {
            return Result.Failure(WmsErrors.Validation(
                "customer.item_reference_limit",
                $"A customer cannot contain more than {MaximumItemReferences} item references."));
        }

        var duplicateSku = references
            .GroupBy(reference => NormalizeOptionalUpper(reference.CustomerSku) ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicateSku is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "customer.item_sku_conflict",
                $"Customer SKU '{duplicateSku.Key}' appears more than once for the customer."));
        }

        var duplicateBarcode = references
            .Where(reference => !string.IsNullOrWhiteSpace(reference.CustomerBarcode))
            .GroupBy(reference => NormalizeOptionalUpper(reference.CustomerBarcode)!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateBarcode is not null)
        {
            return Result.Failure(WmsErrors.Conflict(
                "customer.item_barcode_conflict",
                $"Customer barcode '{duplicateBarcode.Key}' appears more than once for the customer."));
        }

        var itemIds = references.Select(reference => reference.ItemId).Distinct().ToArray();
        if (itemIds.Any(itemId => itemId <= 0))
        {
            return Result.Failure(WmsErrors.Validation(
                "customer.item_reference_item_invalid",
                "Every customer item reference must point to a positive item identifier."));
        }

        var existingItemIds = await context.Items
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToHashSetAsync(cancellationToken);
        var missingItem = itemIds.FirstOrDefault(itemId => !existingItemIds.Contains(itemId));
        return missingItem == 0
            ? Result.Success()
            : Result.Failure(WmsErrors.NotFound(
                "customer.item_not_found",
                $"Item '{missingItem}' was not found."));
    }

    private static void AddShipToAddresses(
        Customer customer,
        IReadOnlyList<CustomerShipToAddressInput> inputs)
    {
        foreach (var input in inputs)
        {
            customer.AddShipToAddress(new CustomerShipToAddress(
                input.Code,
                input.RecipientName,
                input.Phone,
                input.CountryCode,
                input.Region,
                input.City,
                input.PostalCode,
                input.AddressLine1,
                input.AddressLine2,
                input.DeliveryInstructions,
                input.DeliveryWindowStart,
                input.DeliveryWindowEnd,
                input.IsDefault));
        }
    }

    private static void AddItemReferences(
        Customer customer,
        IReadOnlyList<CustomerItemReferenceInput> inputs)
    {
        foreach (var input in inputs)
        {
            var reference = new CustomerItemReference(
                input.ItemId,
                input.CustomerSku,
                input.CustomerBarcode,
                input.CustomerDescription);
            if (!input.IsActive)
            {
                reference.SetActive(false);
            }

            customer.AddItemReference(reference);
        }
    }

    private static void ApplyShipToAddresses(
        Customer customer,
        IReadOnlyList<CustomerShipToAddressInput> inputs)
    {
        var retained = new HashSet<int>();
        foreach (var input in inputs)
        {
            CustomerShipToAddress address;
            if (input.Id.HasValue)
            {
                address = customer.ShipToAddresses.Single(candidate => candidate.Id == input.Id.Value);
                if (!string.Equals(
                        address.Code,
                        NormalizeRequired(input.Code, "code"),
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Ship-to codes cannot change after the address is created.");
                }

                address.Update(
                    input.RecipientName,
                    input.Phone,
                    input.CountryCode,
                    input.Region,
                    input.City,
                    input.PostalCode,
                    input.AddressLine1,
                    input.AddressLine2,
                    input.DeliveryInstructions,
                    input.DeliveryWindowStart,
                    input.DeliveryWindowEnd,
                    input.IsDefault,
                    input.IsActive);
                retained.Add(address.Id);
            }
            else
            {
                address = new CustomerShipToAddress(
                    input.Code,
                    input.RecipientName,
                    input.Phone,
                    input.CountryCode,
                    input.Region,
                    input.City,
                    input.PostalCode,
                    input.AddressLine1,
                    input.AddressLine2,
                    input.DeliveryInstructions,
                    input.DeliveryWindowStart,
                    input.DeliveryWindowEnd,
                    input.IsDefault);
                customer.AddShipToAddress(address);
            }
        }

        foreach (var address in customer.ShipToAddresses.Where(address => address.Id != 0 && !retained.Contains(address.Id)))
        {
            address.SetActive(false);
            address.SetDefault(false);
        }

        ApplyDefaultState(customer, inputs.FirstOrDefault(input => input.IsDefault)?.Code);
    }

    private static void ApplyDefaultState(Customer customer, string? requestedCode)
    {
        var normalizedRequested = NormalizeOptionalUpper(requestedCode);
        var active = customer.ShipToAddresses
            .Where(address => address.IsActive)
            .OrderBy(address => address.Code, StringComparer.Ordinal)
            .ToArray();
        var defaultCode = normalizedRequested ??
                          active.FirstOrDefault(address => address.IsDefault)?.Code ??
                          active.FirstOrDefault()?.Code;
        foreach (var address in customer.ShipToAddresses)
        {
            address.SetDefault(defaultCode is not null && address.IsActive && address.Code == defaultCode);
        }
    }

    private static void ApplyItemReferences(
        Customer customer,
        IReadOnlyList<CustomerItemReferenceInput> inputs)
    {
        var retained = new HashSet<int>();
        foreach (var input in inputs)
        {
            if (input.Id.HasValue)
            {
                var reference = customer.ItemReferences.Single(candidate => candidate.Id == input.Id.Value);
                reference.Update(
                    input.ItemId,
                    input.CustomerSku,
                    input.CustomerBarcode,
                    input.CustomerDescription,
                    input.IsActive);
                retained.Add(reference.Id);
            }
            else
            {
                customer.AddItemReference(new CustomerItemReference(
                    input.ItemId,
                    input.CustomerSku,
                    input.CustomerBarcode,
                    input.CustomerDescription));
                if (!input.IsActive)
                {
                    customer.ItemReferences[^1].SetActive(false);
                }
            }
        }

        foreach (var reference in customer.ItemReferences.Where(reference => reference.Id != 0 && !retained.Contains(reference.Id)))
        {
            reference.SetActive(false);
        }
    }

    private async Task RecordChildAuditAsync(
        Customer customer,
        string userId,
        CancellationToken cancellationToken)
    {
        if (customer.ShipToAddresses.Count > 0)
        {
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CustomerShipToChanged,
                    WmsAuditEntityTypes.Customer,
                    customer.Code,
                    After: new Dictionary<string, object?>
                    {
                        ["shipToCount"] = customer.ShipToAddresses.Count,
                        ["activeShipToCount"] = customer.ShipToAddresses.Count(address => address.IsActive)
                    },
                    ActorUserId: userId),
                cancellationToken);
        }

        if (customer.ItemReferences.Count > 0)
        {
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.CustomerItemReferencesChanged,
                    WmsAuditEntityTypes.Customer,
                    customer.Code,
                    After: new Dictionary<string, object?>
                    {
                        ["referenceCount"] = customer.ItemReferences.Count,
                        ["activeReferenceCount"] = customer.ItemReferences.Count(reference => reference.IsActive)
                    },
                    ActorUserId: userId),
                cancellationToken);
        }
    }

    private static CustomerDto MapCustomer(Customer customer) =>
        new(
            customer.Id,
            customer.Code,
            customer.LegalName,
            customer.LocalizedName,
            customer.TaxRegistrationNumber,
            customer.ExternalErpIdentifier,
            customer.ExternalChannelIdentifier,
            customer.ContactName,
            customer.ContactEmail,
            customer.ContactPhone,
            customer.BillingAddressLine1,
            customer.BillingAddressLine2,
            customer.BillingCity,
            customer.BillingRegion,
            customer.BillingPostalCode,
            customer.BillingCountryCode,
            customer.DefaultCarrierCode,
            customer.DefaultCarrierServiceCode,
            customer.Priority,
            customer.PackagingProfile,
            customer.LabelProfile,
            customer.AllowPartialShipment,
            customer.Notes,
            customer.IsActive,
            customer.CreatedAt,
            customer.UpdatedAt,
            customer.ShipToAddresses
                .OrderBy(address => address.Code, StringComparer.Ordinal)
                .Select(address => new CustomerShipToAddressDto(
                    address.Id,
                    address.Code,
                    address.RecipientName,
                    address.Phone,
                    address.CountryCode,
                    address.Region,
                    address.City,
                    address.PostalCode,
                    address.AddressLine1,
                    address.AddressLine2,
                    address.DeliveryInstructions,
                    address.DeliveryWindowStart,
                    address.DeliveryWindowEnd,
                    address.IsDefault,
                    address.IsActive,
                    address.CreatedAt,
                    address.UpdatedAt))
                .ToArray(),
            customer.ItemReferences
                .OrderBy(reference => reference.CustomerSku, StringComparer.Ordinal)
                .Select(reference => new CustomerItemReferenceDto(
                    reference.Id,
                    reference.ItemId,
                    reference.Item?.Sku ?? string.Empty,
                    reference.Item?.Name ?? string.Empty,
                    reference.CustomerSku,
                    reference.CustomerBarcode,
                    reference.CustomerDescription,
                    reference.IsActive,
                    reference.CreatedAt,
                    reference.UpdatedAt))
                .ToArray(),
            customer.ShipToAddresses.Count == 0 && customer.ItemReferences.Count == 0);

    private static Dictionary<string, object?> AuditSnapshot(Customer customer) =>
        new(StringComparer.Ordinal)
        {
            ["code"] = customer.Code,
            ["legalName"] = customer.LegalName,
            ["localizedName"] = customer.LocalizedName,
            ["externalErpIdentifier"] = customer.ExternalErpIdentifier,
            ["externalChannelIdentifier"] = customer.ExternalChannelIdentifier,
            ["defaultCarrierCode"] = customer.DefaultCarrierCode,
            ["defaultCarrierServiceCode"] = customer.DefaultCarrierServiceCode,
            ["priority"] = customer.Priority,
            ["allowPartialShipment"] = customer.AllowPartialShipment,
            ["isActive"] = customer.IsActive,
            ["shipToCount"] = customer.ShipToAddresses.Count,
            ["itemReferenceCount"] = customer.ItemReferences.Count
        };

    private static ParsedImport ParseImport(string csv)
    {
        var rows = new List<ImportedRow>();
        var errors = new List<CustomerImportError>();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = lines.Length > 0 &&
                    string.Equals(ParseCsvLine(lines[0]).FirstOrDefault(), "CODE", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        if (lines.Length - start > MaximumImportRows)
        {
            errors.Add(new CustomerImportError(start + MaximumImportRows + 1, $"A customer import cannot exceed {MaximumImportRows} rows."));
            return new ParsedImport(rows, errors);
        }

        for (var index = start; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            var fields = ParseCsvLine(lines[index]);
            if (fields.Count < 23)
            {
                errors.Add(new CustomerImportError(rowNumber, "Expected at least the 23 customer master columns; ship-to columns are optional."));
                continue;
            }

            var code = NullIfEmpty(fields.ElementAtOrDefault(0));
            var legalName = NullIfEmpty(fields.ElementAtOrDefault(1));
            if (code is null || legalName is null)
            {
                errors.Add(new CustomerImportError(rowNumber, "CODE and LEGAL_NAME are required."));
                continue;
            }

            var shipToCode = NullIfEmpty(fields.ElementAtOrDefault(23));
            var shipToRecipient = NullIfEmpty(fields.ElementAtOrDefault(24));
            var shipToAddress = NullIfEmpty(fields.ElementAtOrDefault(31));
            if (shipToCode is not null && (shipToRecipient is null || shipToAddress is null))
            {
                errors.Add(new CustomerImportError(rowNumber, "SHIP_TO_CODE, SHIP_TO_RECIPIENT_NAME, and SHIP_TO_ADDRESS_LINE_1 are required together."));
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
                NullIfEmpty(fields.ElementAtOrDefault(14)),
                NullIfEmpty(fields.ElementAtOrDefault(15)),
                NullIfEmpty(fields.ElementAtOrDefault(16)),
                ParseInt(fields.ElementAtOrDefault(17), 100, rowNumber, "PRIORITY", errors),
                NullIfEmpty(fields.ElementAtOrDefault(18)),
                NullIfEmpty(fields.ElementAtOrDefault(19)),
                ParseBool(fields.ElementAtOrDefault(20), false, rowNumber, "ALLOW_PARTIAL_SHIPMENT", errors),
                NullIfEmpty(fields.ElementAtOrDefault(21)),
                ParseBool(fields.ElementAtOrDefault(22), true, rowNumber, "IS_ACTIVE", errors),
                shipToCode,
                shipToRecipient,
                NullIfEmpty(fields.ElementAtOrDefault(25)),
                NullIfEmpty(fields.ElementAtOrDefault(26)),
                NullIfEmpty(fields.ElementAtOrDefault(27)),
                NullIfEmpty(fields.ElementAtOrDefault(28)),
                NullIfEmpty(fields.ElementAtOrDefault(29)),
                shipToAddress,
                NullIfEmpty(fields.ElementAtOrDefault(32)),
                NullIfEmpty(fields.ElementAtOrDefault(33)),
                ParseTime(fields.ElementAtOrDefault(34), rowNumber, "SHIP_TO_WINDOW_START", errors),
                ParseTime(fields.ElementAtOrDefault(35), rowNumber, "SHIP_TO_WINDOW_END", errors),
                ParseBool(fields.ElementAtOrDefault(36), false, rowNumber, "SHIP_TO_IS_DEFAULT", errors),
                ParseBool(fields.ElementAtOrDefault(37), true, rowNumber, "SHIP_TO_IS_ACTIVE", errors)));
        }

        return new ParsedImport(rows, errors);
    }

    private static bool SameMasterProfile(ImportedRow first, ImportedRow other) =>
        string.Equals(first.Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(first.LegalName, other.LegalName, StringComparison.Ordinal) &&
        string.Equals(first.LocalizedName, other.LocalizedName, StringComparison.Ordinal) &&
        string.Equals(first.TaxRegistrationNumber, other.TaxRegistrationNumber, StringComparison.Ordinal) &&
        string.Equals(first.ExternalErpIdentifier, other.ExternalErpIdentifier, StringComparison.Ordinal) &&
        string.Equals(first.ExternalChannelIdentifier, other.ExternalChannelIdentifier, StringComparison.Ordinal) &&
        string.Equals(first.ContactName, other.ContactName, StringComparison.Ordinal) &&
        string.Equals(first.ContactEmail, other.ContactEmail, StringComparison.Ordinal) &&
        string.Equals(first.ContactPhone, other.ContactPhone, StringComparison.Ordinal) &&
        string.Equals(first.BillingAddressLine1, other.BillingAddressLine1, StringComparison.Ordinal) &&
        string.Equals(first.BillingAddressLine2, other.BillingAddressLine2, StringComparison.Ordinal) &&
        string.Equals(first.BillingCity, other.BillingCity, StringComparison.Ordinal) &&
        string.Equals(first.BillingRegion, other.BillingRegion, StringComparison.Ordinal) &&
        string.Equals(first.BillingPostalCode, other.BillingPostalCode, StringComparison.Ordinal) &&
        string.Equals(first.BillingCountryCode, other.BillingCountryCode, StringComparison.Ordinal) &&
        string.Equals(first.DefaultCarrierCode, other.DefaultCarrierCode, StringComparison.Ordinal) &&
        string.Equals(first.DefaultCarrierServiceCode, other.DefaultCarrierServiceCode, StringComparison.Ordinal) &&
        first.Priority == other.Priority &&
        string.Equals(first.PackagingProfile, other.PackagingProfile, StringComparison.Ordinal) &&
        string.Equals(first.LabelProfile, other.LabelProfile, StringComparison.Ordinal) &&
        first.AllowPartialShipment == other.AllowPartialShipment &&
        string.Equals(first.Notes, other.Notes, StringComparison.Ordinal) &&
        first.IsActive == other.IsActive;

    private static bool ParseBool(string? value, bool defaultValue, int row, string field, List<CustomerImportError> errors) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : bool.TryParse(value, out var parsed)
                ? parsed
                : AddParseError(field, row, errors, defaultValue);

    private static int ParseInt(string? value, int defaultValue, int row, string field, List<CustomerImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError(field, row, errors, defaultValue);
    }

    private static TimeSpan? ParseTime(string? value, int row, string field, List<CustomerImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError<TimeSpan?>(field, row, errors, null);
    }

    private static T AddParseError<T>(string field, int row, List<CustomerImportError> errors, T fallback)
    {
        errors.Add(new CustomerImportError(row, $"{field} has an invalid value."));
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

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed record ParsedImport(
        IReadOnlyList<ImportedRow> Rows,
        IReadOnlyList<CustomerImportError> Errors);

    private sealed record ImportedRow(
        int RowNumber,
        string Code,
        string LegalName,
        string? LocalizedName,
        string? TaxRegistrationNumber,
        string? ExternalErpIdentifier,
        string? ExternalChannelIdentifier,
        string? ContactName,
        string? ContactEmail,
        string? ContactPhone,
        string? BillingAddressLine1,
        string? BillingAddressLine2,
        string? BillingCity,
        string? BillingRegion,
        string? BillingPostalCode,
        string? BillingCountryCode,
        string? DefaultCarrierCode,
        string? DefaultCarrierServiceCode,
        int Priority,
        string? PackagingProfile,
        string? LabelProfile,
        bool AllowPartialShipment,
        string? Notes,
        bool IsActive,
        string? ShipToCode,
        string? ShipToRecipientName,
        string? ShipToPhone,
        string? ShipToCountryCode,
        string? ShipToRegion,
        string? ShipToCity,
        string? ShipToPostalCode,
        string? ShipToAddressLine1,
        string? ShipToAddressLine2,
        string? ShipToDeliveryInstructions,
        TimeSpan? ShipToWindowStart,
        TimeSpan? ShipToWindowEnd,
        bool ShipToIsDefault,
        bool ShipToIsActive);
}
