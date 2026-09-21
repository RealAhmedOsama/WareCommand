using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Customers;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.SalesOrders;

public sealed class SalesOrderService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    ICustomerManagementService customerManagementService,
    IItemQuantityConversionService quantityConversionService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<SalesOrderService> logger) : ISalesOrderService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;

    public async Task<Result<SalesOrderPageDto>> ListAsync(
        SalesOrderListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SalesOrderPageDto>();
        }

        var query = await ApplyVisibilityAsync(
            context.SalesOrders.AsNoTracking().AsSplitQuery().Include(order => order.Lines),
            request.WarehouseId,
            cancellationToken);
        query = ApplyFilters(query, request);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await ApplyOrdering(query, request)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new SalesOrderPageDto(
            orders.Select(MapOrder).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<SalesOrderDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(id, asNoTracking: true, cancellationToken);
        if (order is null)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.NotFound(
                "sales_order.not_found",
                "The requested sales order was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersRead,
            order.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<SalesOrderDto>()
            : Result.Success(MapOrder(order));
    }

    public async Task<Result<SalesOrderDto>> CreateAsync(
        SalesOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(input.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SalesOrderDto>();
        }

        try
        {
            var prepared = await PrepareAsync(input, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<SalesOrderDto>();
            }

            if (await HasExternalReferenceAsync(
                    input.WarehouseId,
                    prepared.Value.SourceType,
                    prepared.Value.ExternalReference,
                    excludeId: null,
                    cancellationToken))
            {
                return Result.Failure<SalesOrderDto>(WmsErrors.Conflict(
                    "sales_order.external_reference_conflict",
                    "The external reference is already used for this warehouse and source."));
            }

            var sequence = prepared.Value.Warehouse.NumberSequence;
            if (sequence is null)
            {
                sequence = new WarehouseNumberSequence(prepared.Value.Warehouse.Id);
                context.WarehouseNumberSequences.Add(sequence);
            }

            var documentNumber = $"SO-{prepared.Value.Warehouse.Code}-{sequence.AllocateOrderNumber():D6}";
            var order = new SalesOrder(
                documentNumber,
                prepared.Value.Warehouse.Id,
                prepared.Value.Warehouse.Code,
                prepared.Value.Snapshot.CustomerId,
                prepared.Value.Snapshot.CustomerCode,
                prepared.Value.Snapshot.CustomerLegalName,
                prepared.Value.Snapshot.CustomerLocalizedName,
                prepared.Value.Snapshot.CustomerContactName,
                prepared.Value.Snapshot.CustomerContactEmail,
                prepared.Value.Snapshot.CustomerContactPhone,
                prepared.Value.Snapshot.ShipToAddressId,
                prepared.Value.Snapshot.ShipToCode,
                prepared.Value.Snapshot.ShipToRecipientName,
                prepared.Value.Snapshot.ShipToPhone,
                prepared.Value.Snapshot.ShipToCountryCode,
                prepared.Value.Snapshot.ShipToRegion,
                prepared.Value.Snapshot.ShipToCity,
                prepared.Value.Snapshot.ShipToPostalCode,
                prepared.Value.Snapshot.ShipToAddressLine1,
                prepared.Value.Snapshot.ShipToAddressLine2,
                prepared.Value.Snapshot.ShipToDeliveryInstructions,
                prepared.Value.OrderDate,
                input.RequestedShipDate,
                prepared.Value.ExternalReference,
                prepared.Value.SourceType,
                input.SourceReference,
                prepared.Value.Priority,
                prepared.Value.DefaultCarrierCode,
                prepared.Value.DefaultCarrierServiceCode,
                prepared.Value.PackagingProfile,
                prepared.Value.LabelProfile,
                prepared.Value.AllowPartialShipment,
                input.Notes,
                userId);
            order.ReplaceDraftLines(prepared.Value.Lines);
            context.SalesOrders.Add(order);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SalesOrderCreated,
                    WmsAuditEntityTypes.SalesOrder,
                    documentNumber,
                    prepared.Value.Warehouse.Id,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Sales order {DocumentNumber} created for warehouse {WarehouseId} by {UserId}",
                documentNumber,
                prepared.Value.Warehouse.Id,
                userId);
            return Result.Success(MapOrder(order));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.Validation(
                "sales_order.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.BusinessRule(
                "sales_order.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sales order creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<SalesOrderDto>(WmsErrors.FromException(
                exception,
                "sales_order.create_failed",
                "The sales order could not be created."));
        }
    }

    public async Task<Result<SalesOrderDto>> UpdateAsync(
        int id,
        SalesOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(id, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.NotFound(
                "sales_order.not_found",
                "The requested sales order was not found."));
        }

        var authorization = await AuthorizeManageAsync(order.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SalesOrderDto>();
        }

        if (input.WarehouseId != order.WarehouseId)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.Conflict(
                "sales_order.warehouse_locked",
                "The warehouse cannot change after the sales order number is assigned."));
        }

        if (!order.CanEdit)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.Conflict(
                "sales_order.not_editable",
                "Only draft sales orders can be edited. Use controlled replan commands after confirmation."));
        }

        try
        {
            var prepared = await PrepareAsync(input, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<SalesOrderDto>();
            }

            if (await HasExternalReferenceAsync(
                    order.WarehouseId,
                    prepared.Value.SourceType,
                    prepared.Value.ExternalReference,
                    order.Id,
                    cancellationToken))
            {
                return Result.Failure<SalesOrderDto>(WmsErrors.Conflict(
                    "sales_order.external_reference_conflict",
                    "The external reference is already used for this warehouse and source."));
            }

            var before = OrderSnapshot(order);
            if (order.CustomerId != prepared.Value.Snapshot.CustomerId)
            {
                order.ChangeDraftCustomer(
                    prepared.Value.Snapshot.CustomerId,
                    ToDomainSnapshot(prepared.Value.Snapshot));
            }
            else
            {
                order.RefreshCustomerSnapshot(ToDomainSnapshot(prepared.Value.Snapshot));
            }

            context.SalesOrderLines.RemoveRange(order.Lines);
            order.UpdateDraft(
                prepared.Value.OrderDate,
                input.RequestedShipDate,
                prepared.Value.ExternalReference,
                prepared.Value.SourceType,
                input.SourceReference,
                prepared.Value.Priority,
                prepared.Value.DefaultCarrierCode,
                prepared.Value.DefaultCarrierServiceCode,
                prepared.Value.PackagingProfile,
                prepared.Value.LabelProfile,
                prepared.Value.AllowPartialShipment,
                input.Notes);
            order.ReplaceDraftLines(prepared.Value.Lines);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SalesOrderUpdated,
                    WmsAuditEntityTypes.SalesOrder,
                    order.DocumentNumber,
                    order.WarehouseId,
                    Before: before,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapOrder(order));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.Validation(
                "sales_order.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.BusinessRule(
                "sales_order.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sales order update failed for {SalesOrderId}", id);
            return Result.Failure<SalesOrderDto>(WmsErrors.FromException(
                exception,
                "sales_order.update_failed",
                "The sales order could not be updated."));
        }
    }

    public Task<Result<SalesOrderDto>> ConfirmAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            id,
            userId,
            async order =>
            {
                var snapshot = await customerManagementService.GetDocumentSnapshotAsync(
                    new CustomerDocumentSnapshotQuery(
                        order.CustomerId,
                        order.ShipToAddressId,
                        order.ShipToCodeSnapshot),
                    cancellationToken);
                if (snapshot.IsFailure)
                {
                    return snapshot.ToFailure<SalesOrderDto>();
                }

                order.RefreshCustomerSnapshot(ToDomainSnapshot(snapshot.Value));
                order.Confirm(userId, UtcNow());
                return null;
            },
            WmsAuditActions.SalesOrderConfirmed,
            cancellationToken);

    public Task<Result<SalesOrderDto>> HoldAsync(
        int id,
        string reason,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            id,
            userId,
            order =>
            {
                order.Hold(userId, reason, UtcNow());
                return Task.FromResult<Result<SalesOrderDto>?>(null);
            },
            WmsAuditActions.SalesOrderHeld,
            cancellationToken);

    public Task<Result<SalesOrderDto>> ReleaseHoldAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            id,
            userId,
            order =>
            {
                order.ReleaseHold();
                return Task.FromResult<Result<SalesOrderDto>?>(null);
            },
            WmsAuditActions.SalesOrderHoldReleased,
            cancellationToken);

    public Task<Result<SalesOrderDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            id,
            userId,
            order =>
            {
                order.Cancel(userId, UtcNow());
                return Task.FromResult<Result<SalesOrderDto>?>(null);
            },
            WmsAuditActions.SalesOrderCancelled,
            cancellationToken);

    public Task<Result<SalesOrderDto>> CloseAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            id,
            userId,
            order =>
            {
                order.Close(userId, UtcNow());
                return Task.FromResult<Result<SalesOrderDto>?>(null);
            },
            WmsAuditActions.SalesOrderClosed,
            cancellationToken);

    public async Task<Result<SalesOrderImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(null, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SalesOrderImportResult>();
        }

        if (string.IsNullOrWhiteSpace(csv) || csv.Length > 2_000_000)
        {
            return Result.Failure<SalesOrderImportResult>(WmsErrors.Validation(
                "sales_order.import_empty",
                "Provide a non-empty sales-order CSV no larger than 2 MB."));
        }

        var parsed = ParseImport(csv);
        if (parsed.Errors.Count > 0)
        {
            return Result.Failure<SalesOrderImportResult>(WmsErrors.Validation(
                "sales_order.import_invalid",
                string.Join(" ", parsed.Errors.Select(error => $"Row {error.Row}: {error.Message}"))));
        }

        var groups = parsed.Rows
            .GroupBy(row => new
            {
                WarehouseCode = row.WarehouseCode,
                CustomerCode = row.CustomerCode,
                row.ExternalReference,
                SourceType = row.SourceType
            })
            .ToArray();
        var preparedGroups = new List<(SalesOrderInput Input, int Row)>();
        foreach (var group in groups)
        {
            var first = group.First();
            var warehouse = await context.Warehouses
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Code == first.WarehouseCode && candidate.IsActive,
                    cancellationToken);
            if (warehouse is null)
            {
                return Result.Failure<SalesOrderImportResult>(WmsErrors.NotFound(
                    "sales_order.import_warehouse_not_found",
                    $"Row {first.RowNumber}: warehouse '{first.WarehouseCode}' was not found or is inactive."));
            }

            var customer = await context.Customers
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Code == first.CustomerCode && candidate.IsActive,
                    cancellationToken);
            if (customer is null)
            {
                return Result.Failure<SalesOrderImportResult>(WmsErrors.NotFound(
                    "sales_order.import_customer_not_found",
                    $"Row {first.RowNumber}: customer '{first.CustomerCode}' was not found or is inactive."));
            }

            preparedGroups.Add((
                new SalesOrderInput(
                    warehouse.Id,
                    customer.Id,
                    ShipToCode: first.ShipToCode,
                    OrderDate: first.OrderDate,
                    RequestedShipDate: first.RequestedShipDate,
                    ExternalReference: first.ExternalReference,
                    SourceType: first.SourceType,
                    SourceReference: first.SourceReference,
                    Priority: first.Priority,
                    Notes: first.Notes,
                    Lines: group.Select(row => new SalesOrderLineInput(
                        row.ItemSku,
                        row.Quantity,
                        row.UnitOfMeasure,
                        row.PackagingCode,
                        row.CustomerItemSku,
                        row.LineNotes)).ToArray()),
                first.RowNumber));
        }

        var imported = 0;
        foreach (var prepared in preparedGroups)
        {
            var created = await CreateAsync(prepared.Input, userId, cancellationToken);
            if (created.IsFailure)
            {
                return created.ToFailure<SalesOrderImportResult>();
            }

            imported++;
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.SalesOrderBulkImported,
                WmsAuditEntityTypes.SalesOrder,
                After: new Dictionary<string, object?> { ["orderCount"] = imported },
                ActorUserId: userId),
            cancellationToken);
        return Result.Success(new SalesOrderImportResult(imported, []));
    }

    public async Task<Result<string>> ExportAsync(
        SalesOrderListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var query = await ApplyVisibilityAsync(
            context.SalesOrders.AsNoTracking().AsSplitQuery().Include(order => order.Lines),
            request.WarehouseId,
            cancellationToken);
        var orders = await ApplyOrdering(ApplyFilters(query, request), request)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine(
            "DOCUMENT_NUMBER,WAREHOUSE_ID,CUSTOMER_ID,SHIP_TO_CODE,ORDER_DATE,REQUESTED_SHIP_DATE,EXTERNAL_REFERENCE,SOURCE_TYPE,SOURCE_REFERENCE,PRIORITY,STATUS,ALLOW_PARTIAL_SHIPMENT,ITEM_SKU,QUANTITY,UNIT_OF_MEASURE,PACKAGING_CODE,CUSTOMER_ITEM_SKU,LINE_NOTES");
        foreach (var order in orders)
        {
            foreach (var line in order.Lines)
            {
                builder.AppendLine(string.Join(",", [
                    EscapeCsv(order.DocumentNumber),
                    order.WarehouseId.ToString(CultureInfo.InvariantCulture),
                    order.CustomerId.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(order.ShipToCodeSnapshot),
                    order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    order.RequestedShipDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                    EscapeCsv(order.ExternalReference),
                    EscapeCsv(order.SourceType),
                    EscapeCsv(order.SourceReference),
                    order.Priority.ToString(CultureInfo.InvariantCulture),
                    order.Status.ToString(),
                    order.AllowPartialShipmentSnapshot.ToString(),
                    EscapeCsv(line.ItemSkuSnapshot),
                    line.OrderedQuantity.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(line.OrderedUnitOfMeasure),
                    EscapeCsv(line.PackagingCodeSnapshot),
                    EscapeCsv(line.CustomerItemSkuSnapshot),
                    EscapeCsv(line.Notes)
                ]));
            }
        }

        return Result.Success(builder.ToString());
    }

    private async Task<Result<SalesOrderDto>> MutateAsync(
        int id,
        string userId,
        Func<SalesOrder, Task<Result<SalesOrderDto>?>> mutation,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(id, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.NotFound(
                "sales_order.not_found",
                "The requested sales order was not found."));
        }

        var authorization = await AuthorizeManageAsync(order.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<SalesOrderDto>();
        }

        try
        {
            var before = OrderSnapshot(order);
            var mutationResult = await mutation(order);
            if (mutationResult is not null && mutationResult.IsFailure)
            {
                return mutationResult;
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.SalesOrder,
                    order.DocumentNumber,
                    order.WarehouseId,
                    Before: before,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapOrder(order));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.Validation(
                "sales_order.command_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<SalesOrderDto>(WmsErrors.BusinessRule(
                "sales_order.command_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sales order command {Action} failed for {SalesOrderId}", auditAction, id);
            return Result.Failure<SalesOrderDto>(WmsErrors.FromException(
                exception,
                "sales_order.command_failed",
                "The sales-order command could not be completed."));
        }
    }

    private async Task<Result<PreparedOrder>> PrepareAsync(
        SalesOrderInput input,
        CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .Include(candidate => candidate.NumberSequence)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
                cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<PreparedOrder>(WmsErrors.NotFound(
                "sales_order.warehouse_not_found",
                "The requested warehouse was not found or is inactive."));
        }

        var snapshot = await customerManagementService.GetDocumentSnapshotAsync(
            new CustomerDocumentSnapshotQuery(input.CustomerId, input.ShipToAddressId, input.ShipToCode),
            cancellationToken);
        if (snapshot.IsFailure)
        {
            return snapshot.ToFailure<PreparedOrder>();
        }

        var lines = await PrepareLinesAsync(
            input.CustomerId,
            input.Lines,
            cancellationToken);
        if (lines.IsFailure)
        {
            return lines.ToFailure<PreparedOrder>();
        }

        var sourceType = NormalizeRequired(input.SourceType, 30, nameof(input.SourceType)).ToUpperInvariant();
        var externalReference = NormalizeOptionalUpper(input.ExternalReference, 100);
        var priority = input.Priority ?? snapshot.Value.Priority;
        var carrier = input.DefaultCarrierCode ?? snapshot.Value.DefaultCarrierCode;
        var service = input.DefaultCarrierServiceCode ?? snapshot.Value.DefaultCarrierServiceCode;
        var packaging = input.PackagingProfile ?? snapshot.Value.PackagingProfile;
        var label = input.LabelProfile ?? snapshot.Value.LabelProfile;
        var partial = input.AllowPartialShipment ?? snapshot.Value.AllowPartialShipment;
        var orderDate = input.OrderDate ?? DateOnly.FromDateTime(UtcNow());

        if (input.RequestedShipDate.HasValue && input.RequestedShipDate < orderDate)
        {
            return Result.Failure<PreparedOrder>(WmsErrors.Validation(
                "sales_order.ship_date_before_order_date",
                "The requested ship date cannot be before the order date."));
        }

        return Result.Success(new PreparedOrder(
            warehouse,
            snapshot.Value,
            lines.Value,
            orderDate,
            externalReference,
            sourceType,
            priority,
            carrier,
            service,
            packaging,
            label,
            partial));
    }

    private async Task<Result<IReadOnlyList<SalesOrderLine>>> PrepareLinesAsync(
        int customerId,
        IReadOnlyList<SalesOrderLineInput>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs is null || inputs.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                "sales_order.lines_required",
                "A sales order must contain at least one line."));
        }

        if (inputs.Count > 10_000)
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                "sales_order.line_limit",
                "A sales order cannot contain more than 10,000 lines."));
        }

        var normalizedSkus = inputs.Select(input => NormalizeOptionalUpper(input.ItemSku, 50) ?? string.Empty).ToArray();
        if (normalizedSkus.Any(string.IsNullOrWhiteSpace))
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                "sales_order.item_required",
                "Every sales-order line must contain an item SKU."));
        }

        if (normalizedSkus.Distinct(StringComparer.Ordinal).Count() != normalizedSkus.Length)
        {
            return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Conflict(
                "sales_order.duplicate_item",
                "An item may appear only once in a sales order; split quantities before allocation."));
        }

        var lines = new List<SalesOrderLine>(inputs.Count);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var item = await context.Items
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Sku == normalizedSkus[index], cancellationToken);
            if (item is null)
            {
                return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.NotFound(
                    "sales_order.item_not_found",
                    $"Item '{normalizedSkus[index]}' was not found."));
            }

            if (!item.IsActive)
            {
                return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Conflict(
                    "sales_order.item_inactive",
                    $"Item '{item.Sku}' is inactive and cannot be ordered."));
            }

            string? customerItemSku = NormalizeOptionalUpper(input.CustomerItemSku, 100);
            if (customerItemSku is not null)
            {
                var reference = await context.CustomerItemReferences
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.CustomerId == customerId &&
                                    candidate.CustomerSku == customerItemSku,
                        cancellationToken);
                if (reference is null || reference.ItemId != item.Id)
                {
                    return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Validation(
                        "sales_order.customer_item_reference_invalid",
                        $"Customer SKU '{customerItemSku}' is not mapped to item '{item.Sku}'."));
                }

                if (!reference.IsActive)
                {
                    return Result.Failure<IReadOnlyList<SalesOrderLine>>(WmsErrors.Conflict(
                        "sales_order.customer_item_reference_inactive",
                        $"Customer SKU '{customerItemSku}' is inactive."));
                }
            }

            var conversion = string.IsNullOrWhiteSpace(input.PackagingCode)
                ? await quantityConversionService.ConvertToBaseAsync(
                    item.Id,
                    input.OrderedQuantity,
                    input.UnitOfMeasure,
                    cancellationToken: cancellationToken)
                : await quantityConversionService.ConvertPackagingToBaseAsync(
                    item.Id,
                    input.OrderedQuantity,
                    input.PackagingCode!,
                    cancellationToken: cancellationToken);
            if (conversion.IsFailure)
            {
                return conversion.ToFailure<IReadOnlyList<SalesOrderLine>>();
            }

            var value = conversion.Value;
            lines.Add(new SalesOrderLine(
                index + 1,
                item.Id,
                item.Sku,
                item.Name,
                item.LocalizedName,
                customerItemSku,
                value.EnteredUnitOfMeasure,
                input.OrderedQuantity,
                value.BaseUnitOfMeasure,
                value.BaseQuantity,
                value.ConversionFactorToBase,
                value.ResultPrecision,
                value.ConversionPath,
                value.ConversionRuleIds,
                value.PackagingSnapshot?.Code,
                value.PackagingSnapshot?.Version,
                value.PackagingSnapshot?.UnitsPerPackage,
                input.Notes));
        }

        return Result.Success<IReadOnlyList<SalesOrderLine>>(lines);
    }

    private async Task<SalesOrder?> LoadOrderAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<SalesOrder> query = context.SalesOrders.AsQueryable().AsSplitQuery().Include(order => order.Lines);
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    private async Task<IQueryable<SalesOrder>> ApplyVisibilityAsync(
        IQueryable<SalesOrder> query,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId.HasValue)
        {
            return query.Where(order => order.WarehouseId == warehouseId.Value);
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        return scope.HasGlobalAccess
            ? query
            : query.Where(order => scope.WarehouseIds.Contains(order.WarehouseId));
    }

    private IQueryable<SalesOrder> ApplyFilters(IQueryable<SalesOrder> query, SalesOrderListQuery request)
    {
        if (request.WarehouseId.HasValue)
        {
            query = query.Where(order => order.WarehouseId == request.WarehouseId.Value);
        }

        if (request.CustomerId.HasValue)
        {
            query = query.Where(order => order.CustomerId == request.CustomerId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(order => order.Status == request.Status.Value);
        }

        if (!request.IncludeCancelled)
        {
            query = query.Where(order => order.Status != SalesOrderStatus.Cancelled);
        }

        if (request.OrderDateFrom.HasValue)
        {
            query = query.Where(order => order.OrderDate >= request.OrderDateFrom.Value);
        }

        if (request.OrderDateTo.HasValue)
        {
            query = query.Where(order => order.OrderDate <= request.OrderDateTo.Value);
        }

        if (string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            return query;
        }

        var pattern = $"%{request.SearchTerm.Trim()}%";
        return string.Equals(context.Database.ProviderName, PostgreSqlProviderName, StringComparison.Ordinal)
            ? query.Where(order =>
                EF.Functions.ILike(order.DocumentNumber, pattern) ||
                EF.Functions.ILike(order.CustomerCodeSnapshot, pattern) ||
                EF.Functions.ILike(order.CustomerLegalNameSnapshot, pattern) ||
                (order.ExternalReference != null && EF.Functions.ILike(order.ExternalReference, pattern)) ||
                order.Lines.Any(line => EF.Functions.ILike(line.ItemSkuSnapshot, pattern)))
            : query.Where(order =>
                EF.Functions.Like(order.DocumentNumber, pattern) ||
                EF.Functions.Like(order.CustomerCodeSnapshot, pattern) ||
                EF.Functions.Like(order.CustomerLegalNameSnapshot, pattern) ||
                (order.ExternalReference != null && EF.Functions.Like(order.ExternalReference, pattern)) ||
                order.Lines.Any(line => EF.Functions.Like(line.ItemSkuSnapshot, pattern)));
    }

    private static IOrderedQueryable<SalesOrder> ApplyOrdering(
        IQueryable<SalesOrder> query,
        SalesOrderListQuery request)
    {
        var ordered = request.SortBy switch
        {
            SalesOrderSortField.CustomerCode => request.Descending
                ? query.OrderByDescending(order => order.CustomerCodeSnapshot)
                : query.OrderBy(order => order.CustomerCodeSnapshot),
            SalesOrderSortField.Status => request.Descending
                ? query.OrderByDescending(order => order.Status)
                : query.OrderBy(order => order.Status),
            SalesOrderSortField.OrderDate => request.Descending
                ? query.OrderByDescending(order => order.OrderDate)
                : query.OrderBy(order => order.OrderDate),
            SalesOrderSortField.RequestedShipDate => request.Descending
                ? query.OrderByDescending(order => order.RequestedShipDate)
                : query.OrderBy(order => order.RequestedShipDate),
            SalesOrderSortField.Priority => request.Descending
                ? query.OrderByDescending(order => order.Priority)
                : query.OrderBy(order => order.Priority),
            SalesOrderSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(order => order.UpdatedAt)
                : query.OrderBy(order => order.UpdatedAt),
            _ => request.Descending
                ? query.OrderByDescending(order => order.DocumentNumber)
                : query.OrderBy(order => order.DocumentNumber)
        };

        return ordered.ThenBy(order => order.Id);
    }

    private async Task<Result> AuthorizeManageAsync(int? warehouseId, CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersManage,
            warehouseId,
            cancellationToken);

    private async Task<bool> HasExternalReferenceAsync(
        int warehouseId,
        string sourceType,
        string? externalReference,
        int? excludeId,
        CancellationToken cancellationToken) =>
        externalReference is not null && await context.SalesOrders.AnyAsync(
            order => order.WarehouseId == warehouseId &&
                     order.SourceType == sourceType &&
                     order.ExternalReference == externalReference &&
                     order.Id != excludeId,
            cancellationToken);

    private DateTime UtcNow() => clock.UtcNow.UtcDateTime;

    private static CustomerOrderSnapshot ToDomainSnapshot(CustomerDocumentSnapshot snapshot) =>
        new(
            snapshot.CustomerId,
            snapshot.CustomerCode,
            snapshot.CustomerLegalName,
            snapshot.CustomerLocalizedName,
            snapshot.CustomerContactName,
            snapshot.CustomerContactEmail,
            snapshot.CustomerContactPhone,
            snapshot.ShipToAddressId,
            snapshot.ShipToCode,
            snapshot.ShipToRecipientName,
            snapshot.ShipToPhone,
            snapshot.ShipToCountryCode,
            snapshot.ShipToRegion,
            snapshot.ShipToCity,
            snapshot.ShipToPostalCode,
            snapshot.ShipToAddressLine1,
            snapshot.ShipToAddressLine2,
            snapshot.ShipToDeliveryInstructions,
            snapshot.DefaultCarrierCode,
            snapshot.DefaultCarrierServiceCode,
            snapshot.Priority,
            snapshot.PackagingProfile,
            snapshot.LabelProfile,
            snapshot.AllowPartialShipment);

    private static SalesOrderDto MapOrder(SalesOrder order) =>
        new(
            order.Id,
            order.DocumentNumber,
            order.WarehouseId,
            order.WarehouseCodeSnapshot,
            order.CustomerId,
            order.CustomerCodeSnapshot,
            order.CustomerLegalNameSnapshot,
            order.CustomerLocalizedNameSnapshot,
            order.CustomerContactNameSnapshot,
            order.CustomerContactEmailSnapshot,
            order.CustomerContactPhoneSnapshot,
            order.ShipToAddressId,
            order.ShipToCodeSnapshot,
            order.ShipToRecipientNameSnapshot,
            order.ShipToPhoneSnapshot,
            order.ShipToCountryCodeSnapshot,
            order.ShipToRegionSnapshot,
            order.ShipToCitySnapshot,
            order.ShipToPostalCodeSnapshot,
            order.ShipToAddressLine1Snapshot,
            order.ShipToAddressLine2Snapshot,
            order.ShipToDeliveryInstructionsSnapshot,
            order.OrderDate,
            order.RequestedShipDate,
            order.ExternalReference,
            order.SourceType,
            order.SourceReference,
            order.Priority,
            order.DefaultCarrierCodeSnapshot,
            order.DefaultCarrierServiceCodeSnapshot,
            order.PackagingProfileSnapshot,
            order.LabelProfileSnapshot,
            order.AllowPartialShipmentSnapshot,
            order.Notes,
            order.Status,
            order.HoldReason,
            order.CreatedAt,
            order.UpdatedAt,
            order.ConfirmedAtUtc,
            order.HeldAtUtc,
            order.CancelledAtUtc,
            order.ClosedAtUtc,
            order.Revision,
            order.Lines.OrderBy(line => line.LineNumber).Select(MapLine).ToArray(),
            order.CanEdit,
            order.Status == SalesOrderStatus.Draft,
            order.Status is SalesOrderStatus.Draft or SalesOrderStatus.Confirmed or SalesOrderStatus.Held,
            order.Status is not (SalesOrderStatus.Cancelled or SalesOrderStatus.Closed or SalesOrderStatus.Shipped),
            order.Status == SalesOrderStatus.Held,
            order.Status == SalesOrderStatus.Shipped);

    private static SalesOrderLineDto MapLine(SalesOrderLine line) =>
        new(
            line.Id,
            line.LineNumber,
            line.ItemId,
            line.ItemSkuSnapshot,
            line.ItemNameSnapshot,
            line.ItemLocalizedNameSnapshot,
            line.CustomerItemSkuSnapshot,
            line.OrderedUnitOfMeasure,
            line.OrderedQuantity,
            line.BaseUnitOfMeasure,
            line.OrderedBaseQuantity,
            line.AllocatedBaseQuantity,
            line.PickedBaseQuantity,
            line.PackedBaseQuantity,
            line.ShippedBaseQuantity,
            line.CancelledBaseQuantity,
            line.BackorderBaseQuantity,
            line.ConversionPath,
            line.ConversionRuleIds,
            line.ConversionFactorToBase,
            line.ConversionPrecision,
            line.PackagingCodeSnapshot,
            line.PackagingVersionSnapshot,
            line.PackagingUnitsPerPackageSnapshot,
            line.Notes,
            line.Revision);

    private static Dictionary<string, object?> OrderSnapshot(SalesOrder order) =>
        new(StringComparer.Ordinal)
        {
            ["documentNumber"] = order.DocumentNumber,
            ["warehouseId"] = order.WarehouseId,
            ["customerId"] = order.CustomerId,
            ["customerCode"] = order.CustomerCodeSnapshot,
            ["shipToCode"] = order.ShipToCodeSnapshot,
            ["externalReference"] = order.ExternalReference,
            ["sourceType"] = order.SourceType,
            ["status"] = order.Status.ToString(),
            ["priority"] = order.Priority,
            ["allowPartialShipment"] = order.AllowPartialShipmentSnapshot,
            ["lineCount"] = order.Lines.Count,
            ["revision"] = order.Revision
        };

    private static ParsedImport ParseImport(string csv)
    {
        var rows = new List<ImportedRow>();
        var errors = new List<SalesOrderImportError>();
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var start = lines.Length > 0 &&
                    string.Equals(ParseCsvLine(lines[0]).FirstOrDefault(), "WAREHOUSE_CODE", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        if (lines.Length - start > MaximumImportRows)
        {
            errors.Add(new SalesOrderImportError(start + MaximumImportRows + 1, $"A sales-order import cannot exceed {MaximumImportRows} rows."));
            return new ParsedImport(rows, errors);
        }

        for (var index = start; index < lines.Length; index++)
        {
            var rowNumber = index + 1;
            var fields = ParseCsvLine(lines[index]);
            if (fields.Count < 12)
            {
                errors.Add(new SalesOrderImportError(rowNumber, "Expected at least 12 columns: warehouse, customer, dates, source, and item quantity."));
                continue;
            }

            var warehouse = NormalizeOptionalUpper(fields.ElementAtOrDefault(0), 20);
            var customer = NormalizeOptionalUpper(fields.ElementAtOrDefault(1), 50);
            var itemSku = NormalizeOptionalUpper(fields.ElementAtOrDefault(10), 50);
            if (warehouse is null || customer is null || itemSku is null)
            {
                errors.Add(new SalesOrderImportError(rowNumber, "WAREHOUSE_CODE, CUSTOMER_CODE, and ITEM_SKU are required."));
                continue;
            }

            var orderDate = ParseDate(fields.ElementAtOrDefault(3), rowNumber, "ORDER_DATE", errors);
            var requestedShipDate = ParseNullableDate(fields.ElementAtOrDefault(4), rowNumber, "REQUESTED_SHIP_DATE", errors);
            var quantity = ParseDecimal(fields.ElementAtOrDefault(11), rowNumber, "QUANTITY", errors);
            var priority = ParseInt(fields.ElementAtOrDefault(8), 100, rowNumber, "PRIORITY", errors);
            if (!orderDate.HasValue || quantity <= 0m)
            {
                errors.Add(new SalesOrderImportError(rowNumber, "ORDER_DATE and a positive QUANTITY are required."));
                continue;
            }

            rows.Add(new ImportedRow(
                rowNumber,
                warehouse,
                customer,
                NormalizeOptionalUpper(fields.ElementAtOrDefault(2), 50),
                orderDate.Value,
                requestedShipDate,
                NormalizeOptionalUpper(fields.ElementAtOrDefault(5), 100),
                NormalizeOptionalUpper(fields.ElementAtOrDefault(6), 30) ?? "INTEGRATION",
                NullIfEmpty(fields.ElementAtOrDefault(7)),
                priority,
                NullIfEmpty(fields.ElementAtOrDefault(9)),
                itemSku,
                quantity,
                NormalizeOptionalUpper(fields.ElementAtOrDefault(12), 20),
                NormalizeOptionalUpper(fields.ElementAtOrDefault(13), 100),
                NormalizeOptionalUpper(fields.ElementAtOrDefault(14), 100),
                NullIfEmpty(fields.ElementAtOrDefault(15))));
        }

        return new ParsedImport(rows, errors);
    }

    private static DateOnly? ParseDate(string? value, int row, string field, List<SalesOrderImportError> errors) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : AddParseError<DateOnly?>(field, row, errors, null);

    private static DateOnly? ParseNullableDate(string? value, int row, string field, List<SalesOrderImportError> errors) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseDate(value, row, field, errors);

    private static decimal ParseDecimal(string? value, int row, string field, List<SalesOrderImportError> errors) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : AddParseError(field, row, errors, 0m);

    private static int ParseInt(string? value, int defaultValue, int row, string field, List<SalesOrderImportError> errors) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : AddParseError(field, row, errors, defaultValue);

    private static T AddParseError<T>(string field, int row, List<SalesOrderImportError> errors, T fallback)
    {
        errors.Add(new SalesOrderImportError(row, $"{field} has an invalid value."));
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

    private static string NormalizeRequired(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, nameof(value));

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

    private sealed record PreparedOrder(
        Warehouse Warehouse,
        CustomerDocumentSnapshot Snapshot,
        IReadOnlyList<SalesOrderLine> Lines,
        DateOnly OrderDate,
        string? ExternalReference,
        string SourceType,
        int Priority,
        string? DefaultCarrierCode,
        string? DefaultCarrierServiceCode,
        string? PackagingProfile,
        string? LabelProfile,
        bool AllowPartialShipment);

    private sealed record ParsedImport(
        IReadOnlyList<ImportedRow> Rows,
        IReadOnlyList<SalesOrderImportError> Errors);

    private sealed record ImportedRow(
        int RowNumber,
        string WarehouseCode,
        string CustomerCode,
        string? ShipToCode,
        DateOnly OrderDate,
        DateOnly? RequestedShipDate,
        string? ExternalReference,
        string SourceType,
        string? SourceReference,
        int Priority,
        string? Notes,
        string ItemSku,
        decimal Quantity,
        string? UnitOfMeasure,
        string? PackagingCode,
        string? CustomerItemSku,
        string? LineNotes);
}
