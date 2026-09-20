using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Purchasing;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Purchasing;

public sealed class PurchaseOrderService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IItemQuantityConversionService quantityConversionService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<PurchaseOrderService> logger) : IPurchaseOrderService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;

    public async Task<Result<PurchaseOrderPageDto>> ListAsync(
        PurchaseOrderListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PurchaseOrdersRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PurchaseOrderPageDto>();
        }

        var query = await ApplyVisibilityAsync(
            context.PurchaseOrders
                .AsNoTracking()
                .Include(order => order.Lines),
            request.WarehouseId,
            cancellationToken);
        query = ApplyFilters(query, request);

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await ApplyOrdering(query, request)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new PurchaseOrderPageDto(
            rows.Select(MapToDto).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<PurchaseOrderDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(id, asNoTracking: true, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.NotFound(
                "purchase_order.not_found",
                "The requested purchase order was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PurchaseOrdersRead,
            order.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<PurchaseOrderDto>()
            : Result.Success(MapToDto(order));
    }

    public async Task<Result<PurchaseOrderDto>> CreateAsync(
        PurchaseOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(input.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PurchaseOrderDto>();
        }

        try
        {
            var prepared = await PrepareInputAsync(input, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<PurchaseOrderDto>();
            }

            var externalReference = NormalizeOptionalUpper(input.ExternalReference, 100);
            var sourceType = NormalizeSourceType(input.SourceType);
            var duplicate = await HasExternalReferenceAsync(
                input.SupplierId,
                sourceType,
                externalReference,
                excludeId: null,
                cancellationToken);
            if (duplicate)
            {
                return Result.Failure<PurchaseOrderDto>(WmsErrors.Conflict(
                    "purchase_order.external_reference_conflict",
                    "The external reference is already used for this supplier and source."));
            }

            var warehouse = prepared.Value.Warehouse;
            var sequence = warehouse.NumberSequence;
            if (sequence is null)
            {
                sequence = new WarehouseNumberSequence(warehouse.Id);
                context.WarehouseNumberSequences.Add(sequence);
            }

            var documentNumber = $"PO-{warehouse.Code}-{sequence.AllocateOrderNumber():D6}";
            var order = new PurchaseOrder(
                documentNumber,
                warehouse.Id,
                warehouse.Code,
                prepared.Value.Supplier.Id,
                prepared.Value.Supplier.Code,
                prepared.Value.Supplier.LegalName,
                input.OrderDate,
                input.ExpectedReceiptDate,
                externalReference,
                sourceType,
                input.SourceReference,
                input.CurrencyCode ?? prepared.Value.Supplier.DefaultCurrencyCode,
                input.Notes,
                userId);
            order.ReplaceDraftLines(prepared.Value.Lines);
            context.PurchaseOrders.Add(order);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PurchaseOrderCreated,
                    WmsAuditEntityTypes.PurchaseOrder,
                    documentNumber,
                    warehouse.Id,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Purchase order {DocumentNumber} created for warehouse {WarehouseId} by {UserId}",
                documentNumber,
                warehouse.Id,
                userId);
            return Result.Success(MapToDto(order));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.Validation(
                "purchase_order.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.BusinessRule(
                "purchase_order.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Purchase order creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<PurchaseOrderDto>(WmsErrors.FromException(
                exception,
                "purchase_order.create_failed",
                "The purchase order could not be created."));
        }
    }

    public async Task<Result<PurchaseOrderDto>> UpdateAsync(
        int id,
        PurchaseOrderInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(id, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.NotFound(
                "purchase_order.not_found",
                "The requested purchase order was not found."));
        }

        var authorization = await AuthorizeManageAsync(order.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PurchaseOrderDto>();
        }

        if (input.WarehouseId != order.WarehouseId || input.SupplierId != order.SupplierId)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.Conflict(
                "purchase_order.identity_locked",
                "Warehouse and supplier cannot change after the purchase order number is assigned."));
        }

        try
        {
            var prepared = await PrepareInputAsync(input, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<PurchaseOrderDto>();
            }

            var externalReference = NormalizeOptionalUpper(input.ExternalReference, 100);
            var sourceType = NormalizeSourceType(input.SourceType);
            if (await HasExternalReferenceAsync(
                    order.SupplierId,
                    sourceType,
                    externalReference,
                    order.Id,
                    cancellationToken))
            {
                return Result.Failure<PurchaseOrderDto>(WmsErrors.Conflict(
                    "purchase_order.external_reference_conflict",
                    "The external reference is already used for this supplier and source."));
            }

            var before = OrderSnapshot(order);
            context.PurchaseOrderLines.RemoveRange(order.Lines);
            order.UpdateDraft(
                input.OrderDate,
                input.ExpectedReceiptDate,
                externalReference,
                sourceType,
                input.SourceReference,
                input.CurrencyCode ?? prepared.Value.Supplier.DefaultCurrencyCode,
                input.Notes);
            order.ReplaceDraftLines(prepared.Value.Lines);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PurchaseOrderUpdated,
                    WmsAuditEntityTypes.PurchaseOrder,
                    order.DocumentNumber,
                    order.WarehouseId,
                    Before: before,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(order));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.Validation(
                "purchase_order.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.BusinessRule(
                "purchase_order.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Purchase order update failed for {PurchaseOrderId}", id);
            return Result.Failure<PurchaseOrderDto>(WmsErrors.FromException(
                exception,
                "purchase_order.update_failed",
                "The purchase order could not be updated."));
        }
    }

    public Task<Result<PurchaseOrderDto>> ConfirmAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.PurchaseOrderConfirmed,
            (order, now) => order.Confirm(userId, now),
            validateMasters: true,
            cancellationToken);

    public Task<Result<PurchaseOrderDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.PurchaseOrderCancelled,
            (order, now) => order.Cancel(userId, now),
            validateMasters: false,
            cancellationToken);

    public Task<Result<PurchaseOrderDto>> CloseAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.PurchaseOrderClosed,
            (order, now) => order.Close(userId, now),
            validateMasters: false,
            cancellationToken);

    public Task<Result<PurchaseOrderDto>> ReopenAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.PurchaseOrderReopened,
            (order, _) => order.Reopen(),
            validateMasters: false,
            cancellationToken);

    public async Task<Result<PurchaseOrderImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                "purchase_order.import_empty",
                "Purchase-order import content is required."));
        }

        try
        {
            var rows = ParseCsv(csv);
            if (rows.Count < 2)
            {
                return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                    "purchase_order.import_header",
                    "Purchase-order import must contain a header and at least one row."));
            }

            var header = rows[0];
            var expected = new[]
            {
                "WAREHOUSE_ID", "SUPPLIER_ID", "ORDER_DATE", "EXPECTED_RECEIPT_DATE",
                "EXTERNAL_REFERENCE", "SOURCE_TYPE", "SOURCE_REFERENCE", "CURRENCY_CODE", "NOTES",
                "ITEM_SKU", "ORDERED_QUANTITY", "UNIT_OF_MEASURE", "OVER_DELIVERY_TOLERANCE_PERCENT",
                "UNDER_DELIVERY_TOLERANCE_PERCENT", "SUPPLIER_ITEM_REFERENCE", "LINE_NOTES"
            };
            if (header.Count < expected.Length || !expected.SequenceEqual(
                    header.Take(expected.Length).Select(NormalizeHeader),
                    StringComparer.Ordinal))
            {
                return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                    "purchase_order.import_header",
                    $"Purchase-order import header must be: {string.Join(',', expected)}."));
            }

            if (rows.Count - 1 > MaximumImportRows)
            {
                return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                    "purchase_order.import_too_large",
                    $"Purchase-order import cannot exceed {MaximumImportRows} rows."));
            }

            var imported = 0;
            var errors = new List<PurchaseOrderImportError>();
            for (var index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                try
                {
                    var input = ParseImportRow(row, index + 1);
                    var result = await CreateAsync(input, userId, cancellationToken);
                    if (result.IsFailure)
                    {
                        errors.Add(new PurchaseOrderImportError(index + 1, result.Error));
                        continue;
                    }

                    imported++;
                }
                catch (ArgumentException exception)
                {
                    errors.Add(new PurchaseOrderImportError(index + 1, exception.Message));
                }
            }

            if (imported > 0)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.PurchaseOrderBulkImported,
                        WmsAuditEntityTypes.PurchaseOrder,
                        WarehouseId: null,
                        After: new Dictionary<string, object?>
                        {
                            ["importedCount"] = imported,
                            ["errorCount"] = errors.Count
                        },
                        ActorUserId: userId),
                    cancellationToken);
            }

            if (imported == 0 && errors.Count > 0)
            {
                return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                    "purchase_order.import_invalid",
                    errors[0].Message));
            }

            return Result.Success(new PurchaseOrderImportResult(imported, errors));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PurchaseOrderImportResult>(WmsErrors.Validation(
                "purchase_order.import_invalid",
                exception.Message));
        }
    }

    public async Task<Result<string>> ExportAsync(
        PurchaseOrderListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PurchaseOrdersRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var query = await ApplyVisibilityAsync(
            context.PurchaseOrders
                .AsNoTracking()
                .Include(order => order.Lines),
            request.WarehouseId,
            cancellationToken);
        query = ApplyFilters(query, request);
        var rows = await ApplyOrdering(query, request)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine(
            "DOCUMENT_NUMBER,WAREHOUSE_CODE,SUPPLIER_CODE,ORDER_DATE,EXPECTED_RECEIPT_DATE,EXTERNAL_REFERENCE,SOURCE_TYPE,SOURCE_REFERENCE,CURRENCY_CODE,STATUS,ITEM_SKU,ORDERED_QUANTITY,ORDERED_UOM,ORDERED_BASE_QUANTITY,RECEIVED_BASE_QUANTITY,LINE_NUMBER");
        foreach (var order in rows)
        {
            foreach (var line in order.Lines.OrderBy(line => line.LineNumber))
            {
                AppendCsvRow(builder, [
                    order.DocumentNumber,
                    order.WarehouseCodeSnapshot,
                    order.SupplierCodeSnapshot,
                    order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    order.ExpectedReceiptDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    order.ExternalReference,
                    order.SourceType,
                    order.SourceReference,
                    order.CurrencyCodeSnapshot,
                    order.Status.ToString(),
                    line.ItemSkuSnapshot,
                    line.OrderedQuantity.ToString(CultureInfo.InvariantCulture),
                    line.OrderedUnitOfMeasure,
                    line.OrderedBaseQuantity.ToString(CultureInfo.InvariantCulture),
                    line.ReceivedBaseQuantity.ToString(CultureInfo.InvariantCulture),
                    line.LineNumber.ToString(CultureInfo.InvariantCulture)
                ]);
            }
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result<PurchaseOrderReceiptPlan>> ValidateReceiptAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        int itemId,
        int warehouseId,
        decimal baseQuantity,
        CancellationToken cancellationToken = default)
    {
        if (baseQuantity <= 0m)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.Validation(
                "purchase_order.receipt_quantity_invalid",
                "Purchase-order receipt quantity must be positive."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PurchaseOrderReceiptPlan>();
        }

        var order = await LoadOrderAsync(purchaseOrderId, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.NotFound(
                "purchase_order.not_found",
                "The requested purchase order was not found."));
        }

        if (order.WarehouseId != warehouseId)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.Forbidden(
                "purchase_order.warehouse_scope_denied",
                "The purchase order belongs to another warehouse."));
        }

        var line = order.Lines.SingleOrDefault(candidate => candidate.Id == purchaseOrderLineId);
        if (line is null)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.NotFound(
                "purchase_order.line_not_found",
                "The requested purchase-order line was not found."));
        }

        if (line.ItemId != itemId)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.Validation(
                "purchase_order.item_mismatch",
                "The receipt item does not match the purchase-order line."));
        }

        if (!order.CanReceive)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.BusinessRule(
                "purchase_order.receiving_not_allowed",
                $"Purchase order '{order.DocumentNumber}' is in {order.Status} and cannot receive quantity."));
        }

        if (line.IsClosed)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.BusinessRule(
                "purchase_order.line_closed",
                "The purchase-order line is closed."));
        }

        if (line.ReceivedBaseQuantity + baseQuantity > line.MaximumReceivableBaseQuantity)
        {
            return Result.Failure<PurchaseOrderReceiptPlan>(WmsErrors.BusinessRule(
                "purchase_order.over_receipt",
                "The receipt exceeds the purchase-order line over-delivery tolerance."));
        }

        return Result.Success(new PurchaseOrderReceiptPlan(
            order.Id,
            line.Id,
            order.DocumentNumber,
            itemId,
            warehouseId,
            baseQuantity));
    }

    public async Task<Result> RecordReceiptAsync(
        PurchaseOrderReceiptPlan plan,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(movement);

        if (movement.Type != MovementType.Receipt ||
            movement.ItemId != plan.ItemId ||
            movement.Quantity.Value != plan.BaseQuantity)
        {
            return Result.Failure(WmsErrors.Validation(
                "purchase_order.receipt_movement_mismatch",
                "The stock movement does not match the validated purchase-order receipt."));
        }

        var order = await LoadOrderAsync(plan.PurchaseOrderId, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "purchase_order.not_found",
                "The requested purchase order was not found."));
        }

        var line = order.Lines.SingleOrDefault(candidate => candidate.Id == plan.PurchaseOrderLineId);
        if (line is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "purchase_order.line_not_found",
                "The requested purchase-order line was not found."));
        }

        try
        {
            order.RecordReceipt(line.Id, plan.BaseQuantity);
            movement.LinkPurchaseOrder(order.Id, line.Id);
            var allocation = new PurchaseOrderReceiptAllocation(
                order.Id,
                line.Id,
                movement,
                plan.BaseQuantity,
                userId,
                movement.Timestamp);
            context.PurchaseOrderReceiptAllocations.Add(allocation);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PurchaseOrderReceiptAllocated,
                    WmsAuditEntityTypes.PurchaseOrder,
                    order.DocumentNumber,
                    order.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["lineId"] = line.Id,
                        ["movementId"] = movement.Id,
                        ["baseQuantity"] = plan.BaseQuantity,
                        ["status"] = order.Status.ToString()
                    },
                    ActorUserId: userId),
                cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation(
                "purchase_order.receipt_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "purchase_order.receipt_invalid",
                exception.Message));
        }
    }

    private async Task<Result<PurchaseOrderDto>> ChangeLifecycleAsync(
        int id,
        string userId,
        string auditAction,
        Action<PurchaseOrder, DateTime> mutation,
        bool validateMasters,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(id, asNoTracking: false, cancellationToken);
        if (order is null)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.NotFound(
                "purchase_order.not_found",
                "The requested purchase order was not found."));
        }

        var authorization = await AuthorizeManageAsync(order.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PurchaseOrderDto>();
        }

        try
        {
            if (validateMasters)
            {
                var validation = await ValidateCurrentMastersAsync(order, cancellationToken);
                if (validation.IsFailure)
                {
                    return validation.ToFailure<PurchaseOrderDto>();
                }
            }

            var before = OrderSnapshot(order);
            mutation(order, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.PurchaseOrder,
                    order.DocumentNumber,
                    order.WarehouseId,
                    Before: before,
                    After: OrderSnapshot(order),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(MapToDto(order));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<PurchaseOrderDto>(WmsErrors.BusinessRule(
                "purchase_order.lifecycle_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Purchase order lifecycle change failed for {PurchaseOrderId}", id);
            return Result.Failure<PurchaseOrderDto>(WmsErrors.FromException(
                exception,
                "purchase_order.lifecycle_failed",
                "The purchase-order lifecycle change could not be completed."));
        }
    }

    private async Task<Result<PreparedInput>> PrepareInputAsync(
        PurchaseOrderInput input,
        CancellationToken cancellationToken)
    {
        if (input.OrderDate == default)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "purchase_order.order_date_required",
                "Order date is required."));
        }

        if (input.ExpectedReceiptDate.HasValue && input.ExpectedReceiptDate.Value < input.OrderDate)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "purchase_order.expected_date_invalid",
                "Expected receipt date cannot be before the order date."));
        }

        var lineInputs = input.Lines?.ToArray() ?? [];
        if (lineInputs.Length == 0)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "purchase_order.lines_required",
                "At least one purchase-order line is required."));
        }

        if (lineInputs.Length > 500)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "purchase_order.lines_too_many",
                "A purchase order cannot contain more than 500 lines."));
        }

        var warehouse = await context.Warehouses
            .Include(candidate => candidate.NumberSequence)
            .SingleOrDefaultAsync(candidate => candidate.Id == input.WarehouseId, cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The selected warehouse was not found."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                "warehouse.inactive",
                "An inactive warehouse cannot receive new purchase orders."));
        }

        var supplier = await context.Suppliers
            .Include(candidate => candidate.ItemReferences)
            .SingleOrDefaultAsync(candidate => candidate.Id == input.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                "supplier.not_found",
                "The selected supplier was not found."));
        }

        if (!supplier.IsActive)
        {
            return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                "supplier.inactive",
                "An inactive supplier cannot be used for a new purchase order."));
        }

        var normalizedSkus = lineInputs
            .Select(line => NormalizeRequired(line.ItemSku, 50, nameof(line.ItemSku)).ToUpperInvariant())
            .ToArray();
        if (normalizedSkus.Distinct(StringComparer.Ordinal).Count() != normalizedSkus.Length)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "purchase_order.item_duplicate",
                "Each item may appear only once on a purchase order."));
        }

        var items = await context.Items
            .Where(item => normalizedSkus.Contains(item.Sku))
            .ToDictionaryAsync(item => item.Sku, StringComparer.Ordinal, cancellationToken);
        var lines = new List<PurchaseOrderLine>(lineInputs.Length);
        for (var index = 0; index < lineInputs.Length; index++)
        {
            var lineInput = lineInputs[index];
            var sku = normalizedSkus[index];
            if (!items.TryGetValue(sku, out var item))
            {
                return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                    "item.not_found",
                    $"Item '{sku}' was not found."));
            }

            if (!item.IsActive)
            {
                return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                    "item.inactive",
                    $"Item '{sku}' is inactive."));
            }

            var enteredUnit = string.IsNullOrWhiteSpace(lineInput.UnitOfMeasure)
                ? item.PurchaseUnit
                : lineInput.UnitOfMeasure;
            var conversion = await quantityConversionService.ConvertToBaseAsync(
                item.Id,
                lineInput.OrderedQuantity,
                enteredUnit,
                cancellationToken: cancellationToken);
            if (conversion.IsFailure)
            {
                return conversion.ToFailure<PreparedInput>();
            }

            var supplierReference = NormalizeOptionalUpper(lineInput.SupplierItemReference, 100);
            if (supplierReference is not null && !supplier.ItemReferences.Any(reference =>
                    reference.ItemId == item.Id &&
                    reference.IsActive &&
                    reference.VendorSku == supplierReference))
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "purchase_order.supplier_item_reference_not_found",
                    $"Supplier item reference '{supplierReference}' was not found for item '{sku}'."));
            }

            var overTolerance = lineInput.OverDeliveryTolerancePercent ??
                                supplier.OverDeliveryTolerancePercent ?? 0m;
            var underTolerance = lineInput.UnderDeliveryTolerancePercent ??
                                 supplier.UnderDeliveryTolerancePercent ?? 0m;
            try
            {
                lines.Add(new PurchaseOrderLine(
                    index + 1,
                    item.Id,
                    item.Sku,
                    item.Name,
                    conversion.Value.EnteredUnitOfMeasure,
                    conversion.Value.EnteredQuantity,
                    conversion.Value.BaseUnitOfMeasure,
                    conversion.Value.BaseQuantity,
                    conversion.Value.ConversionFactorToBase,
                    conversion.Value.ResultPrecision,
                    conversion.Value.RoundingMode,
                    conversion.Value.RoundingDelta,
                    conversion.Value.ConversionPath,
                    conversion.Value.ConversionRuleIds,
                    overTolerance,
                    underTolerance,
                    supplierReference,
                    lineInput.Notes));
            }
            catch (ArgumentException exception)
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "purchase_order.line_invalid",
                    exception.Message));
            }
        }

        return Result.Success(new PreparedInput(warehouse, supplier, lines));
    }

    private async Task<Result> ValidateCurrentMastersAsync(
        PurchaseOrder order,
        CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == order.WarehouseId, cancellationToken);
        if (warehouse is null || !warehouse.IsActive)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "warehouse.inactive",
                "The purchase order warehouse is not active."));
        }

        var supplier = await context.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == order.SupplierId, cancellationToken);
        if (supplier is null || !supplier.IsActive)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "supplier.inactive",
                "The purchase order supplier is not active."));
        }

        var itemIds = order.Lines.Select(line => line.ItemId).ToArray();
        var items = await context.Items
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (items.Count != itemIds.Length || items.Any(item => !item.IsActive))
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "purchase_order.item_inactive",
                "Every purchase-order item must remain active before confirmation."));
        }

        if (items.Any(item => order.Lines.Any(line =>
                line.ItemId == item.Id &&
                !string.Equals(line.BaseUnitOfMeasure, item.UnitOfMeasure, StringComparison.Ordinal))))
        {
            return Result.Failure(WmsErrors.Conflict(
                "purchase_order.uom_snapshot_changed",
                "An item base unit changed after the draft was entered; recreate the affected line."));
        }

        var units = order.Lines
            .SelectMany(line => new[] { line.OrderedUnitOfMeasure, line.BaseUnitOfMeasure })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var activeUnitCount = await context.UnitOfMeasures
            .CountAsync(unit => units.Contains(unit.Code) && unit.IsActive, cancellationToken);
        if (activeUnitCount != units.Length)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "purchase_order.uom_inactive",
                "Every purchase-order unit of measure must remain active before confirmation."));
        }

        return Result.Success();
    }

    private async Task<bool> HasExternalReferenceAsync(
        int supplierId,
        string sourceType,
        string? externalReference,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        return externalReference is not null && await context.PurchaseOrders.AnyAsync(
            order => order.SupplierId == supplierId &&
                     order.SourceType == sourceType &&
                     order.ExternalReference == externalReference &&
                     (!excludeId.HasValue || order.Id != excludeId.Value),
            cancellationToken);
    }

    private async Task<IQueryable<PurchaseOrder>> ApplyVisibilityAsync(
        IQueryable<PurchaseOrder> query,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        if (scope.HasGlobalAccess)
        {
            return query;
        }

        if (warehouseId.HasValue)
        {
            return query.Where(order => order.WarehouseId == warehouseId.Value &&
                                        scope.WarehouseIds.Contains(order.WarehouseId));
        }

        return query.Where(order => scope.WarehouseIds.Contains(order.WarehouseId));
    }

    private IQueryable<PurchaseOrder> ApplyFilters(
        IQueryable<PurchaseOrder> query,
        PurchaseOrderListQuery request)
    {
        if (request.WarehouseId.HasValue)
        {
            query = query.Where(order => order.WarehouseId == request.WarehouseId.Value);
        }

        if (request.SupplierId.HasValue)
        {
            query = query.Where(order => order.SupplierId == request.SupplierId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(order => order.Status == request.Status.Value);
        }
        else if (!request.IncludeCancelled)
        {
            query = query.Where(order => order.Status != PurchaseOrderStatus.Cancelled);
        }

        if (request.OrderDateFrom.HasValue)
        {
            query = query.Where(order => order.OrderDate >= request.OrderDateFrom.Value);
        }

        if (request.OrderDateTo.HasValue)
        {
            query = query.Where(order => order.OrderDate <= request.OrderDateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var pattern = $"%{request.SearchTerm.Trim()}%";
            query = string.Equals(
                context.Database.ProviderName,
                PostgreSqlProviderName,
                StringComparison.Ordinal)
                ? query.Where(order =>
                    EF.Functions.ILike(order.DocumentNumber, pattern) ||
                    EF.Functions.ILike(order.SupplierCodeSnapshot, pattern) ||
                    EF.Functions.ILike(order.SupplierNameSnapshot, pattern) ||
                    EF.Functions.ILike(order.ExternalReference ?? string.Empty, pattern))
                : query.Where(order =>
                    EF.Functions.Like(order.DocumentNumber, pattern) ||
                    EF.Functions.Like(order.SupplierCodeSnapshot, pattern) ||
                    EF.Functions.Like(order.SupplierNameSnapshot, pattern) ||
                    EF.Functions.Like(order.ExternalReference ?? string.Empty, pattern));
        }

        return query;
    }

    private static IQueryable<PurchaseOrder> ApplyOrdering(
        IQueryable<PurchaseOrder> query,
        PurchaseOrderListQuery request)
    {
        var ordered = request.SortBy switch
        {
            PurchaseOrderSortField.SupplierCode => request.Descending
                ? query.OrderByDescending(order => order.SupplierCodeSnapshot)
                : query.OrderBy(order => order.SupplierCodeSnapshot),
            PurchaseOrderSortField.Status => request.Descending
                ? query.OrderByDescending(order => order.Status)
                : query.OrderBy(order => order.Status),
            PurchaseOrderSortField.OrderDate => request.Descending
                ? query.OrderByDescending(order => order.OrderDate)
                : query.OrderBy(order => order.OrderDate),
            PurchaseOrderSortField.ExpectedReceiptDate => request.Descending
                ? query.OrderByDescending(order => order.ExpectedReceiptDate)
                : query.OrderBy(order => order.ExpectedReceiptDate),
            PurchaseOrderSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(order => order.UpdatedAt)
                : query.OrderBy(order => order.UpdatedAt),
            _ => request.Descending
                ? query.OrderByDescending(order => order.DocumentNumber)
                : query.OrderBy(order => order.DocumentNumber)
        };

        return ordered.ThenBy(order => order.Id);
    }

    private Task<PurchaseOrder?> LoadOrderAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = context.PurchaseOrders
            .Include(order => order.Lines)
            .AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return query.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    private static PurchaseOrderDto MapToDto(PurchaseOrder order) =>
        new(
            order.Id,
            order.DocumentNumber,
            order.WarehouseId,
            order.WarehouseCodeSnapshot,
            order.SupplierId,
            order.SupplierCodeSnapshot,
            order.SupplierNameSnapshot,
            order.OrderDate,
            order.ExpectedReceiptDate,
            order.ExternalReference,
            order.SourceType,
            order.SourceReference,
            order.CurrencyCodeSnapshot,
            order.Notes,
            order.Status,
            order.CreatedAt,
            order.UpdatedAt,
            order.ConfirmedAtUtc,
            order.CancelledAtUtc,
            order.ClosedAtUtc,
            order.Revision,
            order.Lines
                .OrderBy(line => line.LineNumber)
                .Select(line => new PurchaseOrderLineDto(
                    line.Id,
                    line.LineNumber,
                    line.ItemId,
                    line.ItemSkuSnapshot,
                    line.ItemNameSnapshot,
                    line.OrderedUnitOfMeasure,
                    line.OrderedQuantity,
                    line.BaseUnitOfMeasure,
                    line.OrderedBaseQuantity,
                    line.ReceivedQuantity,
                    line.ReceivedBaseQuantity,
                    line.RemainingBaseQuantity,
                    line.OverDeliveryTolerancePercent,
                    line.UnderDeliveryTolerancePercent,
                    line.SupplierItemReferenceSnapshot,
                    line.Notes,
                    line.IsClosed,
                    line.IsFullyReceived,
                    line.ConversionPath,
                    line.ConversionRuleIds,
                    line.ConversionFactorToBase,
                    line.ConversionPrecision))
                .ToArray(),
            order.CanEdit,
            order.Status == PurchaseOrderStatus.Draft,
            order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Confirmed,
            order.Status is PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received,
            order.Status is PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Closed);

    private static Dictionary<string, object?> OrderSnapshot(PurchaseOrder order) => new()
    {
        ["documentNumber"] = order.DocumentNumber,
        ["warehouseId"] = order.WarehouseId,
        ["supplierId"] = order.SupplierId,
        ["externalReference"] = order.ExternalReference,
        ["sourceType"] = order.SourceType,
        ["status"] = order.Status.ToString(),
        ["lineCount"] = order.Lines.Count,
        ["orderedBaseQuantity"] = order.Lines.Sum(line => line.OrderedBaseQuantity),
        ["receivedBaseQuantity"] = order.Lines.Sum(line => line.ReceivedBaseQuantity)
    };

    private async Task<Result> AuthorizeManageAsync(
        int warehouseId,
        CancellationToken cancellationToken) =>
        await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.PurchaseOrdersManage,
            warehouseId,
            cancellationToken);

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, nameof(value)).ToUpperInvariant();

    private static string NormalizeSourceType(string? value) =>
        NormalizeRequired(value ?? "MANUAL", 30, nameof(value)).ToUpperInvariant();

    private static string NormalizeHeader(string value) => value.Trim().ToUpperInvariant();

    private static PurchaseOrderInput ParseImportRow(List<string> row, int rowNumber)
    {
        string Field(int index) => row.Count > index ? row[index].Trim() : string.Empty;
        var warehouseId = ParseInt(Field(0), "warehouse id", rowNumber);
        var supplierId = ParseInt(Field(1), "supplier id", rowNumber);
        var orderDate = ParseDate(Field(2), "order date", rowNumber);
        var expectedDate = ParseOptionalDate(Field(3), "expected receipt date", rowNumber);
        var quantity = ParseDecimal(Field(10), "ordered quantity", rowNumber);
        var overTolerance = ParseOptionalDecimal(Field(12), "over-delivery tolerance", rowNumber);
        var underTolerance = ParseOptionalDecimal(Field(13), "under-delivery tolerance", rowNumber);
        return new PurchaseOrderInput(
            warehouseId,
            supplierId,
            orderDate,
            expectedDate,
            Optional(Field(4)),
            string.IsNullOrWhiteSpace(Field(5)) ? "IMPORT" : Field(5),
            Optional(Field(6)),
            Optional(Field(7)),
            Optional(Field(8)),
            [new PurchaseOrderLineInput(
                Field(9),
                quantity,
                Optional(Field(11)),
                overTolerance,
                underTolerance,
                Optional(Field(14)),
                Optional(Field(15)))]);
    }

    private static int ParseInt(string value, string label, int row) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Import row {row}: {label} must be an integer.");

    private static decimal ParseDecimal(string value, string label, int row) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Import row {row}: {label} must be a decimal number.");

    private static decimal? ParseOptionalDecimal(string value, string label, int row) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseDecimal(value, label, row);

    private static DateOnly ParseDate(string value, string label, int row) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new ArgumentException($"Import row {row}: {label} must be an ISO date.");

    private static DateOnly? ParseOptionalDate(string value, string label, int row) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseDate(value, label, row);

    private static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        foreach (var line in csv.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n')
                     .Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = new List<string>();
            var value = new StringBuilder();
            var quoted = false;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        value.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    fields.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(character);
                }
            }

            fields.Add(value.ToString());
            rows.Add(fields);
        }

        return rows;
    }

    private static void AppendCsvRow(StringBuilder builder, IEnumerable<string?> values)
    {
        builder.AppendLine(string.Join(',', values.Select(value =>
        {
            var normalized = value ?? string.Empty;
            return normalized.Contains(',', StringComparison.Ordinal) ||
                   normalized.Contains('"', StringComparison.Ordinal) ||
                   normalized.Contains('\n', StringComparison.Ordinal)
                ? $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
                : normalized;
        })));
    }

    private sealed record PreparedInput(
        Warehouse Warehouse,
        Supplier Supplier,
        IReadOnlyList<PurchaseOrderLine> Lines);
}
