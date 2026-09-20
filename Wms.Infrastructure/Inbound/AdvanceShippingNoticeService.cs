using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Purchasing;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inbound;

public sealed partial class AdvanceShippingNoticeService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IItemQuantityConversionService quantityConversionService,
    IPurchaseOrderService purchaseOrderService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<AdvanceShippingNoticeService> logger) : IAdvanceShippingNoticeService
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const int MaximumPageSize = 200;
    private const int MaximumImportRows = 1_000;
    private const int MaximumExportRows = 10_000;
    private const string ImportHeader =
        "WAREHOUSE_ID,SUPPLIER_ID,CARRIER_NAME,EXPECTED_FROM_UTC,EXPECTED_TO_UTC,VEHICLE_NUMBER,TRAILER_NUMBER,CONTAINER_NUMBER,TRACKING_REFERENCE,EXTERNAL_REFERENCE,SOURCE_TYPE,SOURCE_REFERENCE,SOURCE_PAYLOAD,DOCK_LOCATION_ID,NOTES,ITEM_SKU,EXPECTED_QUANTITY,UNIT_OF_MEASURE,PACKAGING_CODE,PURCHASE_ORDER_ID,PURCHASE_ORDER_LINE_ID,OVER_DELIVERY_TOLERANCE_PERCENT,UNDER_DELIVERY_TOLERANCE_PERCENT,PRE_ADVISED_LOT_NUMBER,PRE_ADVISED_EXPIRY_DATE,PRE_ADVISED_SERIAL_NUMBER,EXPECTED_LICENSE_PLATE_NUMBER,EXPECTED_LICENSE_PLATE_IS_SSCC,LINE_NOTES";

    public async Task<Result<AdvanceShippingNoticePageDto>> ListAsync(
        AdvanceShippingNoticeListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(request.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticePageDto>();
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaximumPageSize);
        var query = context.AdvanceShippingNotices
            .AsNoTracking()
            .Include(notice => notice.Lines)
            .AsQueryable();
        query = await ApplyScopeAsync(query, request.WarehouseId, cancellationToken);

        if (request.SupplierId.HasValue)
        {
            query = query.Where(notice => notice.SupplierId == request.SupplierId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(notice => notice.Status == request.Status.Value);
        }
        else if (!request.IncludeCancelled)
        {
            query = query.Where(notice => notice.Status != AdvanceShippingNoticeStatus.Cancelled);
        }

        if (request.ExpectedArrivalFromUtc.HasValue)
        {
            var fromUtc = NormalizeUtc(request.ExpectedArrivalFromUtc.Value);
            query = query.Where(notice => notice.ExpectedArrivalToUtc >= fromUtc);
        }

        if (request.ExpectedArrivalToUtc.HasValue)
        {
            var toUtc = NormalizeUtc(request.ExpectedArrivalToUtc.Value);
            query = query.Where(notice => notice.ExpectedArrivalFromUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.Trim();
            var pattern = $"%{searchTerm}%";
            query = context.Database.ProviderName == PostgreSqlProviderName
                ? query.Where(notice =>
                    EF.Functions.ILike(notice.DocumentNumber, pattern) ||
                    EF.Functions.ILike(notice.SupplierCodeSnapshot, pattern) ||
                    EF.Functions.ILike(notice.ExternalReference ?? string.Empty, pattern) ||
                    EF.Functions.ILike(notice.TrackingReference ?? string.Empty, pattern))
                : query.Where(notice =>
                    notice.DocumentNumber.Contains(searchTerm) ||
                    notice.SupplierCodeSnapshot.Contains(searchTerm) ||
                    (notice.ExternalReference ?? string.Empty).Contains(searchTerm) ||
                    (notice.TrackingReference ?? string.Empty).Contains(searchTerm));
        }

        query = request.SortBy switch
        {
            AdvanceShippingNoticeSortField.DocumentNumber => request.Descending
                ? query.OrderByDescending(notice => notice.DocumentNumber)
                : query.OrderBy(notice => notice.DocumentNumber),
            AdvanceShippingNoticeSortField.SupplierCode => request.Descending
                ? query.OrderByDescending(notice => notice.SupplierCodeSnapshot).ThenByDescending(notice => notice.DocumentNumber)
                : query.OrderBy(notice => notice.SupplierCodeSnapshot).ThenBy(notice => notice.DocumentNumber),
            AdvanceShippingNoticeSortField.Status => request.Descending
                ? query.OrderByDescending(notice => notice.Status).ThenByDescending(notice => notice.DocumentNumber)
                : query.OrderBy(notice => notice.Status).ThenBy(notice => notice.DocumentNumber),
            AdvanceShippingNoticeSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(notice => notice.UpdatedAt).ThenByDescending(notice => notice.DocumentNumber)
                : query.OrderBy(notice => notice.UpdatedAt).ThenBy(notice => notice.DocumentNumber),
            _ => request.Descending
                ? query.OrderByDescending(notice => notice.ExpectedArrivalFromUtc).ThenByDescending(notice => notice.DocumentNumber)
                : query.OrderBy(notice => notice.ExpectedArrivalFromUtc).ThenBy(notice => notice.DocumentNumber)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var notices = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new AdvanceShippingNoticePageDto(
            notices.Select(notice => Map(notice, [])).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<AdvanceShippingNoticeDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: true, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeReadAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        var discrepancies = await context.AdvanceShippingNoticeDiscrepancies
            .AsNoTracking()
            .Where(discrepancy => discrepancy.AdvanceShippingNoticeId == id)
            .OrderByDescending(discrepancy => discrepancy.OccurredAtUtc)
            .ToListAsync(cancellationToken);
        return Result.Success(Map(notice, discrepancies));
    }

    public async Task<Result<AdvanceShippingNoticeDto>> CreateAsync(
        AdvanceShippingNoticeInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeManageAsync(input.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        try
        {
            var prepared = await PrepareInputAsync(input, excludingNoticeId: null, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<AdvanceShippingNoticeDto>();
            }

            var externalReference = NormalizeOptionalUpper(input.ExternalReference, 100);
            var sourceType = NormalizeSourceType(input.SourceType);
            if (externalReference is not null && await HasExternalReferenceAsync(
                    input.SupplierId,
                    sourceType,
                    externalReference,
                    excludingNoticeId: null,
                    cancellationToken))
            {
                return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Conflict(
                    "asn.external_reference_conflict",
                    "The external ASN reference is already used for this supplier and source."));
            }

            var sequence = await context.WarehouseNumberSequences
                .SingleOrDefaultAsync(value => value.WarehouseId == input.WarehouseId, cancellationToken);
            if (sequence is null)
            {
                sequence = new WarehouseNumberSequence(input.WarehouseId);
                context.WarehouseNumberSequences.Add(sequence);
            }

            var documentNumber = $"ASN-{prepared.Value.Warehouse.Code}-{sequence.AllocateAdvanceShippingNoticeNumber():D6}";
            var notice = new AdvanceShippingNotice(
                documentNumber,
                prepared.Value.Warehouse.Id,
                prepared.Value.Warehouse.Code,
                prepared.Value.Supplier.Id,
                prepared.Value.Supplier.Code,
                prepared.Value.Supplier.LegalName,
                input.CarrierName,
                input.ExpectedArrivalFromUtc,
                input.ExpectedArrivalToUtc,
                input.VehicleNumber,
                input.TrailerNumber,
                input.ContainerNumber,
                input.TrackingReference,
                externalReference,
                sourceType,
                input.SourceReference,
                SanitizeSourcePayload(input.SourcePayload),
                input.DockLocationId,
                input.Notes,
                userId);
            notice.ReplaceDraftLines(prepared.Value.Lines);
            context.AdvanceShippingNotices.Add(notice);

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeCreated,
                    WmsAuditEntityTypes.AdvanceShippingNotice,
                    documentNumber,
                    notice.WarehouseId,
                    After: NoticeSnapshot(notice),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, []));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Validation(
                "asn.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule(
                "asn.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ASN creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.FromException(
                exception,
                "asn.create_failed",
                "The advance shipping notice could not be created."));
        }
    }

    public async Task<Result<AdvanceShippingNoticeDto>> UpdateAsync(
        int id,
        AdvanceShippingNoticeInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeManageAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        if (input.WarehouseId != notice.WarehouseId || input.SupplierId != notice.SupplierId)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Conflict(
                "asn.identity_locked",
                "Warehouse and supplier cannot change after the ASN number is assigned."));
        }

        try
        {
            var prepared = await PrepareInputAsync(input, id, cancellationToken);
            if (prepared.IsFailure)
            {
                return prepared.ToFailure<AdvanceShippingNoticeDto>();
            }

            var externalReference = NormalizeOptionalUpper(input.ExternalReference, 100);
            var sourceType = NormalizeSourceType(input.SourceType);
            if (externalReference is not null && await HasExternalReferenceAsync(
                    notice.SupplierId,
                    sourceType,
                    externalReference,
                    id,
                    cancellationToken))
            {
                return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Conflict(
                    "asn.external_reference_conflict",
                    "The external ASN reference is already used for this supplier and source."));
            }

            var before = NoticeSnapshot(notice);
            context.AdvanceShippingNoticeLines.RemoveRange(notice.Lines);
            notice.UpdateDraft(
                input.CarrierName,
                input.ExpectedArrivalFromUtc,
                input.ExpectedArrivalToUtc,
                input.VehicleNumber,
                input.TrailerNumber,
                input.ContainerNumber,
                input.TrackingReference,
                externalReference,
                sourceType,
                input.SourceReference,
                SanitizeSourcePayload(input.SourcePayload),
                input.DockLocationId,
                input.Notes);
            notice.ReplaceDraftLines(prepared.Value.Lines);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeUpdated,
                    WmsAuditEntityTypes.AdvanceShippingNotice,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    Before: before,
                    After: NoticeSnapshot(notice),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, []));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Validation(
                "asn.definition_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule(
                "asn.change_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ASN update failed for {AdvanceShippingNoticeId}", id);
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.FromException(
                exception,
                "asn.update_failed",
                "The advance shipping notice could not be updated."));
        }
    }

    public Task<Result<AdvanceShippingNoticeDto>> SubmitAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.AdvanceShippingNoticeSubmitted,
            (notice, timestamp) => notice.Submit(userId, timestamp),
            cancellationToken);

    public Task<Result<AdvanceShippingNoticeDto>> MarkExpectedAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.AdvanceShippingNoticeExpected,
            (notice, timestamp) => notice.MarkExpected(timestamp),
            cancellationToken);

    public async Task<Result<AdvanceShippingNoticeDto>> AssignDockAsync(
        int id,
        int? dockLocationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeManageAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        var dock = await ValidateDockAsync(notice.WarehouseId, dockLocationId, cancellationToken);
        if (dock.IsFailure)
        {
            return dock.ToFailure<AdvanceShippingNoticeDto>();
        }

        try
        {
            notice.AssignDock(dockLocationId);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeDockAssigned,
                    WmsAuditEntityTypes.AdvanceShippingNotice,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    After: new Dictionary<string, object?> { ["dockLocationId"] = dockLocationId },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, []));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule(
                "asn.dock_assignment_invalid",
                exception.Message));
        }
    }

    public async Task<Result<AdvanceShippingNoticeDto>> ArriveAsync(
        int id,
        int? dockLocationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeManageAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        var dock = await ValidateDockAsync(notice.WarehouseId, dockLocationId ?? notice.DockLocationId, cancellationToken);
        if (dock.IsFailure)
        {
            return dock.ToFailure<AdvanceShippingNoticeDto>();
        }

        try
        {
            notice.Arrive(userId, clock.UtcNow.UtcDateTime, dockLocationId ?? notice.DockLocationId);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeArrived,
                    WmsAuditEntityTypes.AdvanceShippingNotice,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    After: NoticeSnapshot(notice),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, []));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule(
                "asn.arrival_invalid",
                exception.Message));
        }
    }

    public Task<Result<AdvanceShippingNoticeDto>> MarkExceptionAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.AdvanceShippingNoticeException,
            (notice, _) => notice.MarkException(),
            cancellationToken);

    public Task<Result<AdvanceShippingNoticeDto>> CompleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.AdvanceShippingNoticeCompleted,
            (notice, timestamp) => notice.Complete(userId, timestamp),
            cancellationToken);

    public Task<Result<AdvanceShippingNoticeDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            id,
            userId,
            WmsAuditActions.AdvanceShippingNoticeCancelled,
            (notice, timestamp) => notice.Cancel(userId, timestamp),
            cancellationToken);

    public async Task<Result<AdvanceShippingNoticeImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Result.Failure<AdvanceShippingNoticeImportResult>(WmsErrors.Validation(
                "asn.import_empty",
                "ASN import content is required."));
        }

        try
        {
            var rows = ParseCsv(csv);
            if (rows.Count < 2 || !HeaderMatches(rows[0]))
            {
                return Result.Failure<AdvanceShippingNoticeImportResult>(WmsErrors.Validation(
                    "asn.import_header_invalid",
                    $"ASN import header must be: {ImportHeader}"));
            }

            if (rows.Count - 1 > MaximumImportRows)
            {
                return Result.Failure<AdvanceShippingNoticeImportResult>(WmsErrors.Validation(
                    "asn.import_too_large",
                    $"ASN import cannot contain more than {MaximumImportRows} data rows."));
            }

            var imported = 0;
            var errors = new List<AdvanceShippingNoticeImportError>();
            for (var index = 1; index < rows.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = rows[index];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                try
                {
                    var result = await CreateAsync(ParseImportRow(row, index + 1), userId, cancellationToken);
                    if (result.IsFailure)
                    {
                        errors.Add(new AdvanceShippingNoticeImportError(index + 1, result.Error));
                        continue;
                    }

                    imported++;
                }
                catch (ArgumentException exception)
                {
                    errors.Add(new AdvanceShippingNoticeImportError(index + 1, exception.Message));
                }
            }

            if (imported > 0)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.AdvanceShippingNoticeBulkImported,
                        WmsAuditEntityTypes.AdvanceShippingNotice,
                        ActorUserId: userId,
                        Details: $"Imported {imported} ASN rows with {errors.Count} row errors."),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(new AdvanceShippingNoticeImportResult(imported, errors));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<AdvanceShippingNoticeImportResult>(WmsErrors.Validation(
                "asn.import_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ASN import failed");
            return Result.Failure<AdvanceShippingNoticeImportResult>(WmsErrors.FromException(
                exception,
                "asn.import_failed",
                "The ASN import could not be completed."));
        }
    }

    public async Task<Result<string>> ExportAsync(
        AdvanceShippingNoticeListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeReadAsync(request.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<string>();
        }

        var query = context.AdvanceShippingNotices
            .AsNoTracking()
            .Include(notice => notice.Lines)
            .AsQueryable();
        query = await ApplyScopeAsync(query, request.WarehouseId, cancellationToken);
        if (request.SupplierId.HasValue)
        {
            query = query.Where(notice => notice.SupplierId == request.SupplierId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(notice => notice.Status == request.Status.Value);
        }
        else if (!request.IncludeCancelled)
        {
            query = query.Where(notice => notice.Status != AdvanceShippingNoticeStatus.Cancelled);
        }

        var notices = await query
            .OrderByDescending(notice => notice.ExpectedArrivalFromUtc)
            .ThenByDescending(notice => notice.DocumentNumber)
            .Take(MaximumExportRows)
            .ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine(ImportHeader);
        foreach (var notice in notices)
        {
            foreach (var line in notice.Lines.OrderBy(value => value.LineNumber))
            {
                builder.AppendLine(string.Join(',', [
                    Csv(notice.WarehouseId),
                    Csv(notice.SupplierId),
                    Csv(notice.CarrierName),
                    Csv(notice.ExpectedArrivalFromUtc?.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(notice.ExpectedArrivalToUtc?.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(notice.VehicleNumber),
                    Csv(notice.TrailerNumber),
                    Csv(notice.ContainerNumber),
                    Csv(notice.TrackingReference),
                    Csv(notice.ExternalReference),
                    Csv(notice.SourceType),
                    Csv(notice.SourceReference),
                    Csv(notice.SourcePayload),
                    Csv(notice.DockLocationId),
                    Csv(notice.Notes),
                    Csv(line.ItemSkuSnapshot),
                    Csv(line.ExpectedQuantity),
                    Csv(line.EnteredUnitOfMeasure),
                    Csv(line.ItemPackagingCodeSnapshot),
                    Csv(line.PurchaseOrderId),
                    Csv(line.PurchaseOrderLineId),
                    Csv(line.OverDeliveryTolerancePercent),
                    Csv(line.UnderDeliveryTolerancePercent),
                    Csv(line.PreAdvisedLotNumber),
                    Csv(line.PreAdvisedExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(line.PreAdvisedSerialNumber),
                    Csv(line.ExpectedLicensePlateNumber),
                    Csv(line.ExpectedLicensePlateIsSscc),
                    Csv(line.Notes)
                ]));
            }
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result<AdvanceShippingNoticeReceiptPlan>> ValidateReceiptAsync(
        int advanceShippingNoticeId,
        int advanceShippingNoticeLineId,
        int itemId,
        int warehouseId,
        decimal baseQuantity,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        int? licensePlateId,
        CancellationToken cancellationToken = default)
    {
        if (baseQuantity <= 0m)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.Validation(
                "asn.receipt_quantity_invalid",
                "ASN receipt quantity must be positive."));
        }

        var notice = await LoadNoticeAsync(advanceShippingNoticeId, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            notice.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeReceiptPlan>();
        }

        if (notice.WarehouseId != warehouseId)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.Forbidden(
                "asn.warehouse_scope",
                "The ASN belongs to a different warehouse."));
        }

        if (!notice.CanReceive)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.BusinessRule(
                "asn.receiving_invalid",
                $"An ASN in {notice.Status} cannot receive stock. Check it in as arrived first."));
        }

        var line = notice.Lines.SingleOrDefault(candidate => candidate.Id == advanceShippingNoticeLineId);
        if (line is null)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.NotFound(
                "asn.line_not_found",
                "The requested ASN line was not found."));
        }

        if (line.ItemId != itemId)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.Conflict(
                "asn.item_mismatch",
                "The receiving item does not match the ASN line."));
        }

        if (line.IsClosed || line.ReceivedBaseQuantity + baseQuantity > line.MaximumReceivableBaseQuantity)
        {
            return Result.Failure<AdvanceShippingNoticeReceiptPlan>(WmsErrors.BusinessRule(
                "asn.over_receipt",
                "The receipt exceeds the ASN line over-delivery tolerance."));
        }

        var preAdvice = await ValidatePreAdviceAsync(
            line,
            notice.WarehouseId,
            lotNumber,
            expiryDate,
            serialNumber,
            licensePlateId,
            cancellationToken);
        if (preAdvice.IsFailure)
        {
            return preAdvice.ToFailure<AdvanceShippingNoticeReceiptPlan>();
        }

        PurchaseOrderReceiptPlan? purchaseOrderPlan = null;
        if (line.PurchaseOrderId.HasValue && line.PurchaseOrderLineId.HasValue)
        {
            var purchaseOrderResult = await purchaseOrderService.ValidateReceiptAsync(
                line.PurchaseOrderId.Value,
                line.PurchaseOrderLineId.Value,
                itemId,
                warehouseId,
                baseQuantity,
                cancellationToken);
            if (purchaseOrderResult.IsFailure)
            {
                return purchaseOrderResult.ToFailure<AdvanceShippingNoticeReceiptPlan>();
            }

            purchaseOrderPlan = purchaseOrderResult.Value;
        }

        return Result.Success(new AdvanceShippingNoticeReceiptPlan(
            notice.Id,
            line.Id,
            notice.DocumentNumber,
            itemId,
            warehouseId,
            baseQuantity,
            line.PreAdvisedLotNumber,
            line.PreAdvisedExpiryDate,
            line.PreAdvisedSerialNumber,
            line.ExpectedLicensePlateNumber,
            purchaseOrderPlan,
            line.RemainingBaseQuantity));
    }

    public async Task<Result> RecordReceiptAsync(
        AdvanceShippingNoticeReceiptPlan plan,
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
                "asn.receipt_movement_mismatch",
                "The receipt movement does not match the ASN receipt plan."));
        }

        var notice = await LoadNoticeAsync(plan.AdvanceShippingNoticeId, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var line = notice.Lines.SingleOrDefault(candidate => candidate.Id == plan.AdvanceShippingNoticeLineId);
        if (line is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "asn.line_not_found",
                "The requested ASN line was not found."));
        }

        try
        {
            notice.RecordReceipt(line.Id, plan.BaseQuantity, movement.Timestamp);
            if (plan.PurchaseOrderPlan is not null)
            {
                var purchaseOrderResult = await purchaseOrderService.RecordReceiptAsync(
                    plan.PurchaseOrderPlan,
                    movement,
                    userId,
                    cancellationToken);
                if (purchaseOrderResult.IsFailure)
                {
                    return purchaseOrderResult;
                }
            }

            movement.LinkAdvanceShippingNotice(notice.Id, line.Id);
            context.AdvanceShippingNoticeReceiptAllocations.Add(
                new AdvanceShippingNoticeReceiptAllocation(
                    notice.Id,
                    line.Id,
                    movement,
                    plan.BaseQuantity,
                    userId,
                    movement.Timestamp));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeReceiptAllocated,
                    WmsAuditEntityTypes.AdvanceShippingNoticeReceiptAllocation,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["lineId"] = line.Id,
                        ["baseQuantity"] = plan.BaseQuantity,
                        ["movementId"] = movement.Id
                    },
                    ActorUserId: userId),
                cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation("asn.receipt_invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure(WmsErrors.BusinessRule("asn.receipt_invalid", exception.Message));
        }
    }

    public async Task<Result<AdvanceShippingNoticeDto>> AddDiscrepancyAsync(
        int id,
        AdvanceShippingNoticeDiscrepancyInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeManageAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        if (input.AdvanceShippingNoticeLineId.HasValue &&
            notice.Lines.All(line => line.Id != input.AdvanceShippingNoticeLineId.Value))
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.line_not_found",
                "The requested ASN line was not found."));
        }

        try
        {
            notice.MarkException();
            context.AdvanceShippingNoticeDiscrepancies.Add(
                new AdvanceShippingNoticeDiscrepancy(
                    notice.Id,
                    input.AdvanceShippingNoticeLineId,
                    input.Kind,
                    input.ExpectedBaseQuantity,
                    input.ReceivedBaseQuantity,
                    input.VarianceBaseQuantity,
                    input.Details,
                    userId,
                    clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AdvanceShippingNoticeDiscrepancyRecorded,
                    WmsAuditEntityTypes.AdvanceShippingNoticeDiscrepancy,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["kind"] = input.Kind.ToString(),
                        ["lineId"] = input.AdvanceShippingNoticeLineId,
                        ["details"] = input.Details
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, await context.AdvanceShippingNoticeDiscrepancies
                .AsNoTracking()
                .Where(discrepancy => discrepancy.AdvanceShippingNoticeId == notice.Id)
                .OrderByDescending(discrepancy => discrepancy.OccurredAtUtc)
                .ToListAsync(cancellationToken)));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Validation(
                "asn.discrepancy_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule(
                "asn.discrepancy_invalid",
                exception.Message));
        }
    }

    private async Task<Result<AdvanceShippingNoticeDto>> ChangeLifecycleAsync(
        int id,
        string userId,
        string auditAction,
        Action<AdvanceShippingNotice, DateTime> mutation,
        CancellationToken cancellationToken)
    {
        var notice = await LoadNoticeAsync(id, asNoTracking: false, cancellationToken);
        if (notice is null)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.NotFound(
                "asn.not_found",
                "The requested advance shipping notice was not found."));
        }

        var authorization = await AuthorizeManageAsync(notice.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AdvanceShippingNoticeDto>();
        }

        try
        {
            mutation(notice, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.AdvanceShippingNotice,
                    notice.DocumentNumber,
                    notice.WarehouseId,
                    After: NoticeSnapshot(notice),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(notice, []));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.Validation("asn.lifecycle_invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<AdvanceShippingNoticeDto>(WmsErrors.BusinessRule("asn.lifecycle_invalid", exception.Message));
        }
    }

    private async Task<Result<PreparedInput>> PrepareInputAsync(
        AdvanceShippingNoticeInput input,
        int? excludingNoticeId,
        CancellationToken cancellationToken)
    {
        if (input.Lines is null || input.Lines.Count == 0)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "asn.lines_required",
                "At least one ASN line is required."));
        }

        var warehouse = await context.Warehouses
            .SingleOrDefaultAsync(candidate => candidate.Id == input.WarehouseId, cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The ASN warehouse was not found."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                "warehouse.inactive",
                "The ASN warehouse is inactive."));
        }

        var supplier = await context.Suppliers
            .SingleOrDefaultAsync(candidate => candidate.Id == input.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                "supplier.not_found",
                "The ASN supplier was not found."));
        }

        if (!supplier.IsActive)
        {
            return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                "supplier.inactive",
                "An inactive supplier cannot be used for an ASN."));
        }

        var dock = await ValidateDockAsync(input.WarehouseId, input.DockLocationId, cancellationToken);
        if (dock.IsFailure)
        {
            return dock.ToFailure<PreparedInput>();
        }

        var purchaseOrderIds = input.Lines
            .Where(line => line.PurchaseOrderId.HasValue)
            .Select(line => line.PurchaseOrderId!.Value)
            .Distinct()
            .ToArray();
        if (!input.AllowMultiplePurchaseOrders && purchaseOrderIds.Length > 1)
        {
            return Result.Failure<PreparedInput>(WmsErrors.Validation(
                "asn.multiple_purchase_orders_disabled",
                "This ASN policy allows lines from only one purchase order."));
        }

        var lines = new List<AdvanceShippingNoticeLine>(input.Lines.Count);
        var linkedPurchaseOrderIds = new HashSet<int>();
        for (var index = 0; index < input.Lines.Count; index++)
        {
            var lineInput = input.Lines[index];
            if (lineInput.PurchaseOrderId.HasValue != lineInput.PurchaseOrderLineId.HasValue)
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "asn.purchase_order_reference_incomplete",
                    $"ASN line {index + 1} must provide both purchase order and line IDs."));
            }

            var itemSku = lineInput.ItemSku.Trim().ToUpperInvariant();
            var item = await context.Items
                .Include(candidate => candidate.Packagings)
                .SingleOrDefaultAsync(
                    candidate => candidate.Sku == itemSku,
                    cancellationToken);
            if (item is null)
            {
                return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                    "item.not_found",
                    $"Item '{lineInput.ItemSku}' was not found."));
            }

            if (!item.IsActive)
            {
                return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                    "item.inactive",
                    $"Item '{item.Sku}' is inactive."));
            }

            if (!item.RequiresLot && !string.IsNullOrWhiteSpace(lineInput.PreAdvisedLotNumber))
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "asn.lot_not_allowed",
                    $"Item '{item.Sku}' is not lot controlled and cannot have a pre-advised lot."));
            }

            if (!item.RequiresSerial && !string.IsNullOrWhiteSpace(lineInput.PreAdvisedSerialNumber))
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "asn.serial_not_allowed",
                    $"Item '{item.Sku}' is not serial controlled and cannot have a pre-advised serial."));
            }

            if (lineInput.ExpectedLicensePlateIsSscc)
            {
                if (string.IsNullOrWhiteSpace(lineInput.ExpectedLicensePlateNumber))
                {
                    return Result.Failure<PreparedInput>(WmsErrors.Validation(
                        "asn.sscc_required",
                        $"ASN line {index + 1} must provide an SSCC when SSCC validation is enabled."));
                }

                try
                {
                    _ = BarcodeParser.ParseSscc(lineInput.ExpectedLicensePlateNumber);
                }
                catch (ArgumentException exception)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.Validation(
                        "asn.sscc_invalid",
                        $"ASN line {index + 1}: {exception.Message}"));
                }
            }

            ItemPackaging? packaging = null;
            Result<QuantityConversionResult> conversion;
            if (string.IsNullOrWhiteSpace(lineInput.PackagingCode))
            {
                conversion = await quantityConversionService.ConvertToBaseAsync(
                    item.Id,
                    lineInput.ExpectedQuantity,
                    lineInput.UnitOfMeasure ?? item.PurchaseUnit,
                    cancellationToken: cancellationToken);
            }
            else
            {
                packaging = item.Packagings.SingleOrDefault(candidate =>
                    candidate.IsActive &&
                    candidate.Code.Equals(lineInput.PackagingCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (packaging is null)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                        "packaging.not_found",
                        $"Packaging '{lineInput.PackagingCode}' was not found for item '{item.Sku}'."));
                }

                conversion = await quantityConversionService.ConvertPackagingToBaseAsync(
                    item.Id,
                    lineInput.ExpectedQuantity,
                    packaging.Code,
                    cancellationToken: cancellationToken);
            }

            if (conversion.IsFailure)
            {
                return conversion.ToFailure<PreparedInput>();
            }

            PurchaseOrderLine? purchaseOrderLine = null;
            if (lineInput.PurchaseOrderId.HasValue)
            {
                var purchaseOrder = await context.PurchaseOrders
                    .Include(candidate => candidate.Lines)
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == lineInput.PurchaseOrderId.Value,
                        cancellationToken);
                purchaseOrderLine = purchaseOrder?.Lines.SingleOrDefault(candidate =>
                    candidate.Id == lineInput.PurchaseOrderLineId!.Value);
                if (purchaseOrder is null || purchaseOrderLine is null)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.NotFound(
                        "asn.purchase_order_line_not_found",
                        $"The purchase-order line for ASN line {index + 1} was not found."));
                }

                if (purchaseOrder.WarehouseId != input.WarehouseId || purchaseOrder.SupplierId != input.SupplierId)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.Conflict(
                        "asn.purchase_order_scope_mismatch",
                        "Linked purchase orders must belong to the ASN warehouse and supplier."));
                }

                if (purchaseOrder.Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived) ||
                    purchaseOrderLine.IsClosed ||
                    purchaseOrderLine.ItemId != item.Id)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.BusinessRule(
                        "asn.purchase_order_line_invalid",
                        "The linked purchase-order line is not open for inbound demand."));
                }

                var reserved = await context.AdvanceShippingNoticeLines
                    .Where(candidate =>
                        candidate.PurchaseOrderLineId == purchaseOrderLine.Id &&
                        candidate.AdvanceShippingNoticeId != excludingNoticeId)
                    .Select(candidate => new
                    {
                        candidate.ExpectedBaseQuantity,
                        candidate.ReceivedBaseQuantity,
                        Status = candidate.AdvanceShippingNotice.Status
                    })
                    .ToListAsync(cancellationToken);
                var openReserved = reserved
                    .Where(candidate => candidate.Status is not (AdvanceShippingNoticeStatus.Cancelled or AdvanceShippingNoticeStatus.Completed))
                    .Sum(candidate => Math.Max(0m, candidate.ExpectedBaseQuantity - candidate.ReceivedBaseQuantity));
                var available = purchaseOrderLine.MaximumReceivableBaseQuantity -
                    purchaseOrderLine.ReceivedBaseQuantity -
                    openReserved;
                if (conversion.Value.BaseQuantity > available)
                {
                    return Result.Failure<PreparedInput>(WmsErrors.Conflict(
                        "asn.purchase_order_quantity_exceeded",
                        $"ASN line {index + 1} exceeds the remaining open quantity on the linked purchase order."));
                }

                linkedPurchaseOrderIds.Add(purchaseOrder.Id);
            }

            var overTolerance = lineInput.OverDeliveryTolerancePercent ?? purchaseOrderLine?.OverDeliveryTolerancePercent ?? 0m;
            var underTolerance = lineInput.UnderDeliveryTolerancePercent ?? purchaseOrderLine?.UnderDeliveryTolerancePercent ?? 0m;
            var packagingSnapshot = conversion.Value.PackagingSnapshot;
            try
            {
                lines.Add(new AdvanceShippingNoticeLine(
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
                    packagingSnapshot?.PackagingId ?? packaging?.Id,
                    packagingSnapshot?.Code ?? packaging?.Code,
                    packagingSnapshot?.Name ?? packaging?.Name,
                    packagingSnapshot?.UnitOfMeasure ?? packaging?.UnitOfMeasure,
                    packagingSnapshot?.UnitsPerPackage ?? packaging?.UnitsPerPackage,
                    lineInput.PurchaseOrderId,
                    lineInput.PurchaseOrderLineId,
                    lineInput.PreAdvisedLotNumber,
                    lineInput.PreAdvisedExpiryDate,
                    lineInput.PreAdvisedSerialNumber,
                    lineInput.ExpectedLicensePlateNumber,
                    lineInput.ExpectedLicensePlateIsSscc,
                    lineInput.Notes));
            }
            catch (ArgumentException exception)
            {
                return Result.Failure<PreparedInput>(WmsErrors.Validation(
                    "asn.line_invalid",
                    $"ASN line {index + 1}: {exception.Message}"));
            }
        }

        _ = linkedPurchaseOrderIds;
        return Result.Success(new PreparedInput(warehouse, supplier, lines));
    }

    private async Task<Result> ValidateDockAsync(
        int warehouseId,
        int? dockLocationId,
        CancellationToken cancellationToken)
    {
        if (!dockLocationId.HasValue)
        {
            return Result.Success();
        }

        var dock = await context.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == dockLocationId.Value, cancellationToken);
        if (dock is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "dock.not_found",
                "The selected dock was not found."));
        }

        if (dock.WarehouseId != warehouseId || dock.Type != LocationType.Dock || !dock.IsActive)
        {
            return Result.Failure(WmsErrors.Validation(
                "dock.invalid",
                "The selected dock must be an active dock in the ASN warehouse."));
        }

        return Result.Success();
    }

    private async Task<Result> ValidatePreAdviceAsync(
        AdvanceShippingNoticeLine line,
        int warehouseId,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        int? licensePlateId,
        CancellationToken cancellationToken)
    {
        if (line.PreAdvisedLotNumber is not null &&
            !string.Equals(line.PreAdvisedLotNumber, NormalizeOptionalUpper(lotNumber, 100), StringComparison.Ordinal))
        {
            return Result.Failure(WmsErrors.Conflict(
                "asn.lot_mismatch",
                "The received lot does not match the ASN pre-advice."));
        }

        if (line.PreAdvisedExpiryDate.HasValue &&
            (!expiryDate.HasValue || line.PreAdvisedExpiryDate.Value.Date != expiryDate.Value.Date))
        {
            return Result.Failure(WmsErrors.Conflict(
                "asn.expiry_mismatch",
                "The received expiry date does not match the ASN pre-advice."));
        }

        if (line.PreAdvisedSerialNumber is not null &&
            !string.Equals(line.PreAdvisedSerialNumber, NormalizeOptionalUpper(serialNumber, 100), StringComparison.Ordinal))
        {
            return Result.Failure(WmsErrors.Conflict(
                "asn.serial_mismatch",
                "The received serial does not match the ASN pre-advice."));
        }

        if (line.ExpectedLicensePlateNumber is not null && !licensePlateId.HasValue)
        {
            return Result.Failure(WmsErrors.Conflict(
                "asn.license_plate_required",
                "The expected license plate must be selected during receipt."));
        }

        if (line.ExpectedLicensePlateNumber is not null)
        {
            var licensePlate = await context.LicensePlates
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == licensePlateId!.Value,
                    cancellationToken);
            if (licensePlate is null ||
                licensePlate.WarehouseId != warehouseId ||
                !licensePlate.IsActive ||
                licensePlate.Status is LicensePlateStatus.Shipped or LicensePlateStatus.Voided ||
                licensePlate.IsSscc != line.ExpectedLicensePlateIsSscc ||
                !string.Equals(licensePlate.Number, line.ExpectedLicensePlateNumber, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(WmsErrors.Conflict(
                    "asn.license_plate_mismatch",
                    "The selected license plate does not match the ASN pre-advice or warehouse."));
            }
        }

        return Result.Success();
    }

    private async Task<AdvanceShippingNotice?> LoadNoticeAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = context.AdvanceShippingNotices
            .Include(notice => notice.Lines)
            .AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(notice => notice.Id == id, cancellationToken);
    }

    private async Task<IQueryable<AdvanceShippingNotice>> ApplyScopeAsync(
        IQueryable<AdvanceShippingNotice> query,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId.HasValue)
        {
            return query.Where(notice => notice.WarehouseId == warehouseId.Value);
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        return scope.HasGlobalAccess
            ? query
            : query.Where(notice => scope.WarehouseIds.Contains(notice.WarehouseId));
    }

    private Task<Result> AuthorizeReadAsync(int? warehouseId, CancellationToken cancellationToken) =>
        warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesRead,
            warehouseId,
            cancellationToken);

    private Task<Result> AuthorizeManageAsync(int warehouseId, CancellationToken cancellationToken) =>
        warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            warehouseId,
            cancellationToken);

    private async Task<bool> HasExternalReferenceAsync(
        int supplierId,
        string sourceType,
        string externalReference,
        int? excludingNoticeId,
        CancellationToken cancellationToken) =>
        await context.AdvanceShippingNotices.AnyAsync(
            notice => notice.SupplierId == supplierId &&
                      notice.SourceType == sourceType &&
                      notice.ExternalReference == externalReference &&
                      notice.Id != excludingNoticeId,
            cancellationToken);

    private static AdvanceShippingNoticeDto Map(
        AdvanceShippingNotice notice,
        IReadOnlyList<AdvanceShippingNoticeDiscrepancy> discrepancies) =>
        new(
            notice.Id,
            notice.DocumentNumber,
            notice.WarehouseId,
            notice.WarehouseCodeSnapshot,
            notice.SupplierId,
            notice.SupplierCodeSnapshot,
            notice.SupplierNameSnapshot,
            notice.CarrierName,
            notice.ExpectedArrivalFromUtc,
            notice.ExpectedArrivalToUtc,
            notice.VehicleNumber,
            notice.TrailerNumber,
            notice.ContainerNumber,
            notice.TrackingReference,
            notice.ExternalReference,
            notice.SourceType,
            notice.SourceReference,
            notice.SourcePayload,
            notice.DockLocationId,
            notice.Notes,
            notice.Status,
            notice.CreatedAt,
            notice.UpdatedAt,
            notice.SubmittedAtUtc,
            notice.ArrivedAtUtc,
            notice.CompletedAtUtc,
            notice.CancelledAtUtc,
            notice.Revision,
            notice.Lines.Select(Map).ToArray(),
            discrepancies.Select(Map).ToArray(),
            notice.CanEdit,
            notice.CanSubmit,
            notice.CanArrive,
            notice.CanReceive,
            notice.CanCancel);

    private static AdvanceShippingNoticeLineDto Map(AdvanceShippingNoticeLine line) =>
        new(
            line.Id,
            line.LineNumber,
            line.ItemId,
            line.ItemSkuSnapshot,
            line.ItemNameSnapshot,
            line.EnteredUnitOfMeasure,
            line.ExpectedQuantity,
            line.BaseUnitOfMeasure,
            line.ExpectedBaseQuantity,
            line.ReceivedQuantity,
            line.ReceivedBaseQuantity,
            line.RemainingBaseQuantity,
            line.OverDeliveryTolerancePercent,
            line.UnderDeliveryTolerancePercent,
            line.ItemPackagingId,
            line.ItemPackagingCodeSnapshot,
            line.PurchaseOrderId,
            line.PurchaseOrderLineId,
            line.PreAdvisedLotNumber,
            line.PreAdvisedExpiryDate,
            line.PreAdvisedSerialNumber,
            line.ExpectedLicensePlateNumber,
            line.ExpectedLicensePlateIsSscc,
            line.Notes,
            line.IsClosed,
            line.IsFullyReceived,
            line.ConversionPath,
            line.ConversionRuleIds,
            line.ConversionFactorToBase,
            line.ConversionPrecision);

    private static AdvanceShippingNoticeDiscrepancyDto Map(AdvanceShippingNoticeDiscrepancy discrepancy) =>
        new(
            discrepancy.Id,
            discrepancy.AdvanceShippingNoticeLineId,
            discrepancy.Kind,
            discrepancy.ExpectedBaseQuantity,
            discrepancy.ReceivedBaseQuantity,
            discrepancy.VarianceBaseQuantity,
            discrepancy.Details,
            discrepancy.RecordedByUserId,
            discrepancy.OccurredAtUtc);

    private static Dictionary<string, object?> NoticeSnapshot(AdvanceShippingNotice notice) =>
        new Dictionary<string, object?>
        {
            ["documentNumber"] = notice.DocumentNumber,
            ["warehouseId"] = notice.WarehouseId,
            ["supplierId"] = notice.SupplierId,
            ["status"] = notice.Status.ToString(),
            ["externalReference"] = notice.ExternalReference,
            ["expectedArrivalFromUtc"] = notice.ExpectedArrivalFromUtc,
            ["expectedArrivalToUtc"] = notice.ExpectedArrivalToUtc,
            ["dockLocationId"] = notice.DockLocationId
        };

    private static string NormalizeSourceType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "MANUAL" : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim().ToUpperInvariant()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? SanitizeSourcePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var normalized = payload.Trim();
        return SecretPayloadRegex().Replace(normalized, "$1[REDACTED]");
    }

    [GeneratedRegex("([\\\"']?(?:password|token|secret|authorization|connectionstring|clientsecret)[\\\"']?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|'[^']*'|[^,\\s;}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPayloadRegex();

    private sealed record PreparedInput(
        Warehouse Warehouse,
        Supplier Supplier,
        IReadOnlyList<AdvanceShippingNoticeLine> Lines);

    private static bool HeaderMatches(IReadOnlyList<string> header) =>
        string.Equals(string.Join(',', header).Trim(), ImportHeader, StringComparison.OrdinalIgnoreCase);

    private static AdvanceShippingNoticeInput ParseImportRow(string[] row, int rowNumber)
    {
        if (row.Length < 29)
        {
            throw new ArgumentException($"ASN import row {rowNumber} does not contain all required columns.");
        }

        return new AdvanceShippingNoticeInput(
            ParseInt(row[0], "warehouse ID", rowNumber),
            ParseInt(row[1], "supplier ID", rowNumber),
            Optional(row, 2),
            ParseDateTimeOptional(row, 3, rowNumber),
            ParseDateTimeOptional(row, 4, rowNumber),
            Optional(row, 5),
            Optional(row, 6),
            Optional(row, 7),
            Optional(row, 8),
            Optional(row, 9),
            Optional(row, 10) ?? "INTEGRATION",
            Optional(row, 11),
            Optional(row, 12),
            ParseIntOptional(row, 13, rowNumber),
            Optional(row, 14),
            Lines: [new AdvanceShippingNoticeLineInput(
                row[15],
                ParseDecimal(row[16], "expected quantity", rowNumber),
                Optional(row, 17),
                Optional(row, 18),
                ParseIntOptional(row, 19, rowNumber),
                ParseIntOptional(row, 20, rowNumber),
                ParseDecimalOptional(row, 21, rowNumber),
                ParseDecimalOptional(row, 22, rowNumber),
                Optional(row, 23),
                ParseDateOptional(row, 24, rowNumber),
                Optional(row, 25),
                Optional(row, 26),
                ParseBool(row, 27, rowNumber),
                Optional(row, 28))]);
    }

    private static int ParseInt(string value, string name, int row) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"ASN import row {row}: {name} must be a positive integer.");

    private static int? ParseIntOptional(string[] row, int index, int rowNumber) =>
        string.IsNullOrWhiteSpace(row[index]) ? null : ParseInt(row[index], "identifier", rowNumber);

    private static decimal ParseDecimal(string value, string name, int row) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m
            ? parsed
            : throw new ArgumentException($"ASN import row {row}: {name} must be positive.");

    private static decimal? ParseDecimalOptional(string[] row, int index, int rowNumber) =>
        string.IsNullOrWhiteSpace(row[index])
            ? null
            : decimal.TryParse(row[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN import row {rowNumber}: tolerance must be a decimal number.");

    private static bool ParseBool(string[] row, int index, int rowNumber) =>
        string.IsNullOrWhiteSpace(row[index]) ||
        bool.TryParse(row[index], out var parsed) && parsed ||
        string.Equals(row[index], "1", StringComparison.Ordinal);

    private static DateTime? ParseDateTimeOptional(string[] row, int index, int rowNumber) =>
        string.IsNullOrWhiteSpace(row[index])
            ? null
            : DateTime.TryParse(row[index], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN import row {rowNumber}: expected arrival must be a valid UTC date/time.");

    private static DateTime? ParseDateOptional(string[] row, int index, int rowNumber) =>
        string.IsNullOrWhiteSpace(row[index])
            ? null
            : DateTime.TryParseExact(row[index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : throw new ArgumentException($"ASN import row {rowNumber}: pre-advised expiry must be yyyy-MM-dd.");

    private static string? Optional(string[] row, int index) =>
        row.Length > index && !string.IsNullOrWhiteSpace(row[index]) ? row[index].Trim() : null;

    private static string Csv<T>(T? value) =>
        Csv(value switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        });

    private static string Csv(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Contains(',', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;

    private static List<string[]> ParseCsv(string csv)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (character == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            if ((character == '\n' || character == '\r') && !quoted)
            {
                if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
                continue;
            }

            field.Append(character);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add(fields.ToArray());
        }

        return rows;
    }
}
