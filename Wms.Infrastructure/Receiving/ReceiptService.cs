using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Purchasing;
using Wms.Application.Quality;
using Wms.Application.Receiving;
using Wms.Application.Units;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Receiving;

public sealed class ReceiptService : IReceiptService
{
    private readonly WmsDbContext _context;
    private readonly IWarehouseAccessService _warehouseAccessService;
    private readonly IItemQuantityConversionService _quantityConversionService;
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IAdvanceShippingNoticeService _advanceShippingNoticeService;
    private readonly IStockMovementService _stockMovementService;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly ILogger<ReceiptService> _logger;
    private readonly IQualityInspectionService? _qualityInspectionService;
    private readonly IWarehouseWorkService? _warehouseWorkService;

    public ReceiptService(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService,
        IItemQuantityConversionService quantityConversionService,
        IPurchaseOrderService purchaseOrderService,
        IAdvanceShippingNoticeService advanceShippingNoticeService,
        IStockMovementService stockMovementService,
        IAuditWriter auditWriter,
        IClock clock,
        ILogger<ReceiptService> logger,
        IQualityInspectionService? qualityInspectionService = null,
        IWarehouseWorkService? warehouseWorkService = null)
    {
        _context = context;
        _warehouseAccessService = warehouseAccessService;
        _quantityConversionService = quantityConversionService;
        _purchaseOrderService = purchaseOrderService;
        _advanceShippingNoticeService = advanceShippingNoticeService;
        _stockMovementService = stockMovementService;
        _auditWriter = auditWriter;
        _clock = clock;
        _logger = logger;
        _qualityInspectionService = qualityInspectionService;
        _warehouseWorkService = warehouseWorkService;
    }

    public async Task<Result<ReceiptPageDto>> ListAsync(
        ReceiptListQuery request,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceiptsRead,
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceiptPageDto>();
        }

        var query = _context.Receipts
            .AsNoTracking()
            .Include(receipt => receipt.Warehouse)
            .Include(receipt => receipt.Supplier)
            .Include(receipt => receipt.Lines)
            .AsQueryable();
        query = await ApplyScopeAsync(query, cancellationToken);

        if (request.WarehouseId.HasValue)
        {
            query = query.Where(receipt => receipt.WarehouseId == request.WarehouseId.Value);
        }

        if (request.SupplierId.HasValue)
        {
            query = query.Where(receipt => receipt.SupplierId == request.SupplierId.Value);
        }

        if (request.Status.HasValue)
        {
            query = query.Where(receipt => receipt.Status == request.Status.Value);
        }
        else if (!request.IncludeCancelled)
        {
            query = query.Where(receipt => receipt.Status != ReceiptStatus.Cancelled);
        }

        if (request.ReceivedFromUtc.HasValue)
        {
            var from = NormalizeUtc(request.ReceivedFromUtc.Value);
            query = query.Where(receipt => receipt.ReceivedAtUtc >= from);
        }

        if (request.ReceivedToUtc.HasValue)
        {
            var to = NormalizeUtc(request.ReceivedToUtc.Value);
            query = query.Where(receipt => receipt.ReceivedAtUtc < to);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var search = request.SearchTerm.Trim();
            query = query.Where(receipt =>
                receipt.DocumentNumber.Contains(search) ||
                (receipt.ExternalReference != null && receipt.ExternalReference.Contains(search)) ||
                (receipt.SourceReference != null && receipt.SourceReference.Contains(search)) ||
                (receipt.SupplierCodeSnapshot != null && receipt.SupplierCodeSnapshot.Contains(search)));
        }

        query = request.SortBy switch
        {
            ReceiptSortField.Status => request.Descending
                ? query.OrderByDescending(receipt => receipt.Status).ThenByDescending(receipt => receipt.Id)
                : query.OrderBy(receipt => receipt.Status).ThenBy(receipt => receipt.Id),
            ReceiptSortField.WarehouseCode => request.Descending
                ? query.OrderByDescending(receipt => receipt.WarehouseCodeSnapshot).ThenByDescending(receipt => receipt.Id)
                : query.OrderBy(receipt => receipt.WarehouseCodeSnapshot).ThenBy(receipt => receipt.Id),
            ReceiptSortField.SupplierCode => request.Descending
                ? query.OrderByDescending(receipt => receipt.SupplierCodeSnapshot).ThenByDescending(receipt => receipt.Id)
                : query.OrderBy(receipt => receipt.SupplierCodeSnapshot).ThenBy(receipt => receipt.Id),
            ReceiptSortField.UpdatedAt => request.Descending
                ? query.OrderByDescending(receipt => receipt.UpdatedAt).ThenByDescending(receipt => receipt.Id)
                : query.OrderBy(receipt => receipt.UpdatedAt).ThenBy(receipt => receipt.Id),
            _ => request.Descending
                ? query.OrderByDescending(receipt => receipt.ReceivedAtUtc).ThenByDescending(receipt => receipt.Id)
                : query.OrderBy(receipt => receipt.ReceivedAtUtc).ThenBy(receipt => receipt.Id)
        };

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var totalCount = await query.CountAsync(cancellationToken);
        var receipts = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result.Success(new ReceiptPageDto(
            receipts.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount,
            totalPages));
    }

    public async Task<Result<ReceiptDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var receipt = await LoadReceiptAsync(id, asNoTracking: true, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure<ReceiptDto>(WmsErrors.NotFound(
                "receipt.not_found",
                "The requested receipt was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceiptsRead,
            receipt.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<ReceiptDto>()
            : Result.Success(Map(receipt));
    }

    public async Task<Result<ReceiptDto>> CreateAsync(
        ReceiptInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateHeaderAsync(input, WmsPermissions.ReceiptsManage, cancellationToken);
        if (validation.IsFailure)
        {
            return validation.ToFailure<ReceiptDto>();
        }

        var lines = input.Lines?.Where(line => line is not null).ToArray() ?? [];
        if (lines.Length == 0)
        {
            return Result.Failure<ReceiptDto>(WmsErrors.Validation(
                "receipt.lines_required",
                "At least one receipt line is required."));
        }

        var warehouse = validation.Value.Warehouse;
        var supplier = validation.Value.Supplier;
        var receipt = new Receipt(
            await AllocateDocumentNumberAsync(warehouse, cancellationToken),
            warehouse.Id,
            warehouse.Code,
            supplier?.Id ?? input.SupplierId,
            supplier?.Code,
            supplier?.LegalName,
            input.PurchaseOrderId,
            input.AdvanceShippingNoticeId,
            input.DockLocationId,
            input.ReceivingLocationId,
            input.SourceType,
            input.SourceReference,
            input.ExternalReference,
            input.SessionReference,
            input.Notes,
            userId);

        var lineNumber = 1;
        foreach (var lineInput in lines)
        {
            var lineResult = await BuildLineAsync(
                receipt,
                lineNumber++,
                lineInput,
                input.WarehouseId,
                input.ReceivingLocationId,
                cancellationToken);
            if (lineResult.IsFailure)
            {
                return lineResult.ToFailure<ReceiptDto>();
            }

            receipt.AddLine(lineResult.Value);
        }

        _context.Receipts.Add(receipt);
        await _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ReceiptCreated,
                WmsAuditEntityTypes.Receipt,
                receipt.DocumentNumber,
                receipt.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["status"] = receipt.Status.ToString(),
                    ["lineCount"] = receipt.Lines.Count
                },
                ActorUserId: userId),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(receipt));
    }

    public async Task<Result<ReceiptReceivingPlan>> OpenForReceivingAsync(
        ReceiptReceivingInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceiptsManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceiptReceivingPlan>();
        }

        if (input.Quantity.Value <= 0m)
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.Validation(
                "receipt.quantity_invalid",
                "Receipt quantity must be positive."));
        }

        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(
            candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
            cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The receipt warehouse was not found."));
        }

        var receivingLocation = await _context.Locations.FirstOrDefaultAsync(
            location => location.Id == input.ReceivingLocationId &&
                        location.WarehouseId == input.WarehouseId &&
                        location.IsActive && location.IsReceivable,
            cancellationToken);
        if (receivingLocation is null)
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.Validation(
                "receipt.location_invalid",
                "The receipt receiving location is not active or receivable in the selected warehouse."));
        }

        var item = await _context.Items.FirstOrDefaultAsync(
            candidate => candidate.Id == input.ItemId && candidate.IsActive,
            cancellationToken);
        if (item is null)
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.NotFound(
                "item.not_found",
                "The receipt item was not found or is inactive."));
        }

        var ownerValidation = await ValidateOwnerAsync(
            input.OwnerKind,
            input.InventoryOwnerId,
            input.OwnerCodeSnapshot,
            cancellationToken);
        if (ownerValidation.IsFailure)
        {
            return ownerValidation.ToFailure<ReceiptReceivingPlan>();
        }

        var sourceValidation = ValidateSourcePlans(input);
        if (sourceValidation.IsFailure)
        {
            return sourceValidation.ToFailure<ReceiptReceivingPlan>();
        }

        var source = await ResolveSourceAsync(input, cancellationToken);
        if (source.IsFailure)
        {
            return source.ToFailure<ReceiptReceivingPlan>();
        }

        var accepted = input.AcceptedBaseQuantity ?? input.Quantity.Value;
        var rejected = input.RejectedBaseQuantity ?? 0m;
        var damaged = input.DamagedBaseQuantity ?? 0m;
        var quarantined = input.QuarantinedBaseQuantity ?? 0m;
        var balance = accepted + rejected + damaged + quarantined;
        if (accepted < 0m || rejected < 0m || damaged < 0m || quarantined < 0m ||
            balance != input.Quantity.Value)
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.Validation(
                "receipt.quantity_unbalanced",
                "Accepted, rejected, damaged, and quarantined quantities must equal received quantity."));
        }

        var conversion = EnsureConversionSnapshot(input.Quantity, item);
        var expected = input.AdvanceShippingNoticePlan?.ExpectedBaseQuantity ??
                       input.PurchaseOrderPlan?.ExpectedBaseQuantity ??
                       input.Quantity.Value;
        if (expected <= 0m)
        {
            expected = input.Quantity.Value;
        }

        var licensePlate = input.LicensePlateId.HasValue
            ? await _context.LicensePlates.FirstOrDefaultAsync(
                candidate => candidate.Id == input.LicensePlateId.Value,
                cancellationToken)
            : null;
        if (input.LicensePlateId.HasValue &&
            (licensePlate is null || licensePlate.WarehouseId != input.WarehouseId))
        {
            return Result.Failure<ReceiptReceivingPlan>(WmsErrors.Validation(
                "receipt.license_plate_invalid",
                "The receipt license plate does not belong to the selected warehouse."));
        }

        var receipt = new Receipt(
            await AllocateDocumentNumberAsync(warehouse, cancellationToken),
            warehouse.Id,
            warehouse.Code,
            source.Value.SupplierId ?? input.SupplierId,
            source.Value.SupplierCode,
            source.Value.SupplierName,
            source.Value.PurchaseOrderId,
            source.Value.AdvanceShippingNoticeId,
            input.DockLocationId,
            receivingLocation.Id,
            input.SourceType,
            input.SourceReference ?? input.ReferenceNumber,
            input.ReferenceNumber,
            input.SessionReference,
            input.Notes,
            userId);
        var status = await _context.InventoryStatuses.AsNoTracking().FirstOrDefaultAsync(
            candidate => candidate.Id == InventoryStatusSystemIds.Available,
            cancellationToken);
        var line = CreateLineFromQuantity(
            receipt,
            1,
            input.ItemId,
            item.Sku,
            item.Name,
            input.Quantity,
            expected,
            input.PurchaseOrderId ?? input.AdvanceShippingNoticePlan?.PurchaseOrderPlan?.PurchaseOrderId,
            input.PurchaseOrderLineId ?? input.AdvanceShippingNoticePlan?.PurchaseOrderPlan?.PurchaseOrderLineId,
            input.AdvanceShippingNoticeId,
            input.AdvanceShippingNoticeLineId,
            input.ReceivingLocationId,
            input.LotNumber,
            input.ExpiryDate,
            input.SerialNumber,
            licensePlate,
            status,
            input.Notes,
            conversion,
            input.OwnerKind,
            input.InventoryOwnerId,
            input.OwnerCodeSnapshot);
        receipt.AddLine(line);
        receipt.Open(userId, _clock.UtcNow.UtcDateTime);
        _context.Receipts.Add(receipt);
        await _auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ReceiptOpened,
                WmsAuditEntityTypes.Receipt,
                receipt.DocumentNumber,
                receipt.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["status"] = receipt.Status.ToString(),
                    ["itemId"] = input.ItemId,
                    ["baseQuantity"] = input.Quantity.Value
                },
                ActorUserId: userId),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReceiptReceivingPlan(
            receipt.Id,
            line.Id,
            receipt.DocumentNumber,
            receipt.WarehouseId,
            line.ItemId,
            input.Quantity.Value,
            accepted,
            rejected,
            damaged,
            quarantined,
            input.PurchaseOrderPlan,
            input.AdvanceShippingNoticePlan,
            input.OwnerKind,
            input.InventoryOwnerId,
            line.OwnerCodeSnapshot));
    }

    public async Task<Result> FinalizeReceivingAsync(
        ReceiptReceivingPlan plan,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(movement);
        var receipt = await LoadReceiptAsync(plan.ReceiptId, asNoTracking: false, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure(WmsErrors.NotFound("receipt.not_found", "The receipt was not found."));
        }

        var line = receipt.Lines.SingleOrDefault(candidate => candidate.Id == plan.ReceiptLineId);
        if (line is null)
        {
            return Result.Failure(WmsErrors.NotFound("receipt.line_not_found", "The receipt line was not found."));
        }

        var hasOpenInboundException = await _context.InboundExceptions.AnyAsync(
            exception => exception.ReceiptId == receipt.Id &&
                         (exception.ReceiptLineId == null || exception.ReceiptLineId == line.Id) &&
                         exception.Status != InboundExceptionStatus.Resolved &&
                         exception.Status != InboundExceptionStatus.Cancelled,
            cancellationToken);
        if (hasOpenInboundException)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "receipt.inbound_exception_open",
                "This receipt line is blocked by an open inbound exception. Resolve it before receiving more stock."));
        }

        if (movement.Type != MovementType.Receipt ||
            movement.ReceiptId != receipt.Id ||
            movement.ReceiptLineId != line.Id ||
            movement.ItemId != plan.ItemId ||
            movement.Quantity.Value != plan.BaseQuantity)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.movement_mismatch",
                "The stock movement does not match the persisted receipt line."));
        }

        var ownerCode = InventoryOwnershipDimension.NormalizeOwnerCode(
            line.OwnerKind,
            line.InventoryOwnerId,
            line.OwnerCodeSnapshot);
        if (movement.OwnerKind != line.OwnerKind ||
            movement.InventoryOwnerId != line.InventoryOwnerId ||
            movement.OwnerCodeSnapshot != ownerCode)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.ownership_mismatch",
                "The receiving movement ownership does not match the persisted receipt line owner."));
        }

        if (movement.ToLocationId is null)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.receiving_location_missing",
                "A receipt movement must have a receiving location before putaway work can be generated."));
        }

        try
        {
            receipt.StartReceiving(userId, movement.Timestamp);
            line.RecordPhysicalReceipt(
                plan.BaseQuantity,
                plan.AcceptedBaseQuantity,
                plan.RejectedBaseQuantity,
                plan.DamagedBaseQuantity,
                plan.QuarantinedBaseQuantity);
            var status = await _context.InventoryStatuses.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id == movement.InventoryStatusId,
                cancellationToken);
            line.SetInventoryStatusSnapshot(
                movement.InventoryStatusId,
                status?.Code,
                status?.Name);
            _context.ReceiptLineMovements.Add(new ReceiptLineMovement(
                line,
                movement,
                plan.BaseQuantity,
                ReceiptMovementKind.Receipt,
                userId,
                movement.Timestamp,
                acceptedBaseQuantity: plan.AcceptedBaseQuantity,
                rejectedBaseQuantity: plan.RejectedBaseQuantity,
                damagedBaseQuantity: plan.DamagedBaseQuantity,
                quarantinedBaseQuantity: plan.QuarantinedBaseQuantity));

            if (plan.AdvanceShippingNoticePlan is not null)
            {
                var asnResult = await _advanceShippingNoticeService.RecordReceiptAsync(
                    plan.AdvanceShippingNoticePlan,
                    movement,
                    userId,
                    cancellationToken);
                if (asnResult.IsFailure)
                {
                    return asnResult;
                }
            }
            else if (plan.PurchaseOrderPlan is not null)
            {
                var purchaseOrderResult = await _purchaseOrderService.RecordReceiptAsync(
                    plan.PurchaseOrderPlan,
                    movement,
                    userId,
                    cancellationToken);
                if (purchaseOrderResult.IsFailure)
                {
                    return purchaseOrderResult;
                }
            }

            var qualityInspectionPending = false;
            if (_qualityInspectionService is not null)
            {
                var inspectionResult = await _qualityInspectionService.EnsureForReceiptAsync(
                    receipt.Id,
                    line.Id,
                    movement,
                    userId,
                    cancellationToken);
                if (inspectionResult.IsFailure)
                {
                    return Result.Failure(inspectionResult.Errors);
                }

                qualityInspectionPending = inspectionResult.Value is not null;
            }

            if (_warehouseWorkService is not null)
            {
                var movementKey = movement.Id > 0
                    ? $"id:{movement.Id.ToString(CultureInfo.InvariantCulture)}"
                    : $"ts:{movement.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture)}" +
                      $":qty:{movement.Quantity.Value.ToString("G29", CultureInfo.InvariantCulture)}" +
                      $":lpn:{movement.LicensePlateId?.ToString(CultureInfo.InvariantCulture) ?? "none"}";
                var workResult = await _warehouseWorkService.EnsurePutawayForReceiptAsync(
                    new PutawayWorkGenerationInput(
                        receipt.Id,
                        line.Id,
                        receipt.WarehouseId,
                        line.ItemId,
                        movement.Quantity.Value,
                        movement.BaseUnitOfMeasure,
                        movement.ToLocationId.Value,
                        movement.ToLicensePlateId ?? movement.LicensePlateId,
                        movement.LotId,
                        movement.SerialNumberId,
                        movement.SerialNumber,
                        movement.InventoryStatusId,
                        movement.ReferenceNumber,
                        movementKey,
                        qualityInspectionPending,
                        Notes: line.Notes,
                        OwnerKind: line.OwnerKind,
                        InventoryOwnerId: line.InventoryOwnerId,
                        OwnerCodeSnapshot: line.OwnerCodeSnapshot),
                    userId,
                    cancellationToken);
                if (workResult.IsFailure)
                {
                    return Result.Failure(workResult.Errors);
                }
            }

            receipt.Complete(userId, movement.Timestamp);
            await _auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReceiptCompleted,
                    WmsAuditEntityTypes.Receipt,
                    receipt.DocumentNumber,
                    receipt.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = receipt.Status.ToString(),
                        ["receiptLineId"] = line.Id,
                        ["movementId"] = movement.Id,
                        ["receivedBaseQuantity"] = plan.BaseQuantity,
                        ["acceptedBaseQuantity"] = plan.AcceptedBaseQuantity
                    },
                    ActorUserId: userId),
                cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation("receipt.receiving_invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure(WmsErrors.BusinessRule("receipt.receiving_invalid", exception.Message));
        }
    }

    public async Task<Result<ReceiptDto>> CompleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await LoadForManageAsync(id, cancellationToken);
        if (receipt.IsFailure)
        {
            return receipt.ToFailure<ReceiptDto>();
        }

        try
        {
            receipt.Value.Complete(userId, _clock.UtcNow.UtcDateTime);
            await _auditWriter.RecordAsync(
                new AuditRecord(WmsAuditActions.ReceiptCompleted, WmsAuditEntityTypes.Receipt,
                    receipt.Value.DocumentNumber, receipt.Value.WarehouseId,
                    After: new Dictionary<string, object?> { ["status"] = receipt.Value.Status.ToString() },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(receipt.Value));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceiptDto>(WmsErrors.BusinessRule("receipt.lifecycle_invalid", exception.Message));
        }
    }

    public async Task<Result<ReceiptDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var receipt = await LoadForManageAsync(id, cancellationToken);
        if (receipt.IsFailure)
        {
            return receipt.ToFailure<ReceiptDto>();
        }

        try
        {
            receipt.Value.Cancel(userId, _clock.UtcNow.UtcDateTime);
            await _auditWriter.RecordAsync(
                new AuditRecord(WmsAuditActions.ReceiptCancelled, WmsAuditEntityTypes.Receipt,
                    receipt.Value.DocumentNumber, receipt.Value.WarehouseId,
                    After: new Dictionary<string, object?> { ["status"] = receipt.Value.Status.ToString() },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(receipt.Value));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceiptDto>(WmsErrors.BusinessRule("receipt.lifecycle_invalid", exception.Message));
        }
    }

    public async Task<Result<ReceiptDto>> ReverseAsync(
        int id,
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var receiptResult = await LoadForManageAsync(id, cancellationToken);
        if (receiptResult.IsFailure)
        {
            return receiptResult.ToFailure<ReceiptDto>();
        }

        var receipt = receiptResult.Value;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<ReceiptDto>(WmsErrors.Validation(
                "receipt.reversal_reason_required",
                "A receipt reversal reason is required."));
        }

        if (!receipt.CanReverse)
        {
            return Result.Failure<ReceiptDto>(WmsErrors.BusinessRule(
                "receipt.reversal_invalid",
                $"A receipt in {receipt.Status} cannot be reversed."));
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational() && _context.Database.CurrentTransaction is null)
            {
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            }

            foreach (var line in receipt.Lines)
            {
                var originalLinks = line.Movements
                    .Where(link => link.Kind == ReceiptMovementKind.Receipt)
                    .ToArray();
                foreach (var link in originalLinks)
                {
                    var reversal = await _stockMovementService.ReverseReceiptAsync(
                        receipt.Id,
                        line.Id,
                        link.Movement,
                        userId,
                        reason,
                        cancellationToken);
                    var demandReversal = await ReverseDemandReferencesAsync(
                        receipt,
                        line,
                        link.BaseQuantity,
                        _clock.UtcNow.UtcDateTime,
                        cancellationToken);
                    if (demandReversal.IsFailure)
                    {
                        if (transaction is not null)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                        }

                        return demandReversal.ToFailure<ReceiptDto>();
                    }

                    line.ReversePhysicalReceipt(
                        link.BaseQuantity,
                        link.AcceptedBaseQuantity,
                        link.RejectedBaseQuantity,
                        link.DamagedBaseQuantity,
                        link.QuarantinedBaseQuantity);
                    _context.ReceiptLineMovements.Add(new ReceiptLineMovement(
                        line,
                        reversal,
                        link.BaseQuantity,
                        ReceiptMovementKind.Reversal,
                        userId,
                        reversal.Timestamp,
                        link.Movement.Id,
                        reason,
                        link.AcceptedBaseQuantity,
                        link.RejectedBaseQuantity,
                        link.DamagedBaseQuantity,
                        link.QuarantinedBaseQuantity));
                }
            }

            receipt.MarkReversed(userId, _clock.UtcNow.UtcDateTime);
            await _auditWriter.RecordAsync(
                new AuditRecord(WmsAuditActions.ReceiptReversed, WmsAuditEntityTypes.Receipt,
                    receipt.DocumentNumber, receipt.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = receipt.Status.ToString(),
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success(Map(receipt));
        }
        catch (ArgumentException exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<ReceiptDto>(WmsErrors.Validation("receipt.reversal_invalid", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            return Result.Failure<ReceiptDto>(WmsErrors.BusinessRule("receipt.reversal_invalid", exception.Message));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<Result> ReverseDemandReferencesAsync(
        Receipt receipt,
        ReceiptLine line,
        decimal baseQuantity,
        DateTime reversedAtUtc,
        CancellationToken cancellationToken)
    {
        if (line.PurchaseOrderId.HasValue && line.PurchaseOrderLineId.HasValue)
        {
            var purchaseOrder = await _context.PurchaseOrders
                .Include(order => order.Lines)
                .SingleOrDefaultAsync(
                    order => order.Id == line.PurchaseOrderId.Value && order.WarehouseId == receipt.WarehouseId,
                    cancellationToken);
            if (purchaseOrder is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "purchase_order.not_found",
                    "The purchase order for the receipt reversal was not found."));
            }

            try
            {
                purchaseOrder.ReverseReceipt(line.PurchaseOrderLineId.Value, baseQuantity);
            }
            catch (InvalidOperationException exception)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "purchase_order.reversal_invalid",
                    exception.Message));
            }
        }

        if (line.AdvanceShippingNoticeId.HasValue && line.AdvanceShippingNoticeLineId.HasValue)
        {
            var notice = await _context.AdvanceShippingNotices
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == line.AdvanceShippingNoticeId.Value &&
                                 candidate.WarehouseId == receipt.WarehouseId,
                    cancellationToken);
            if (notice is null)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "asn.not_found",
                    "The ASN for the receipt reversal was not found."));
            }

            try
            {
                notice.ReverseReceipt(
                    line.AdvanceShippingNoticeLineId.Value,
                    baseQuantity,
                    reversedAtUtc);
            }
            catch (InvalidOperationException exception)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "asn.reversal_invalid",
                    exception.Message));
            }
        }

        return Result.Success();
    }

    public async Task<Result<ReceiptCorrectionDto>> CorrectAsync(
        int id,
        ReceiptCorrectionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var receiptResult = await LoadForManageAsync(id, cancellationToken);
        if (receiptResult.IsFailure)
        {
            return receiptResult.ToFailure<ReceiptCorrectionDto>();
        }

        var original = receiptResult.Value;
        if (!original.CanCorrect)
        {
            return Result.Failure<ReceiptCorrectionDto>(WmsErrors.BusinessRule(
                "receipt.correction_invalid",
                $"A receipt in {original.Status} cannot be corrected."));
        }

        var reversal = await ReverseAsync(id, userId, input.Reason ?? "Receipt correction", cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.ToFailure<ReceiptCorrectionDto>();
        }

        var correction = new Receipt(
            await AllocateDocumentNumberAsync(original.Warehouse, cancellationToken),
            original.WarehouseId,
            original.WarehouseCodeSnapshot,
            original.SupplierId,
            original.SupplierCodeSnapshot,
            original.SupplierNameSnapshot,
            original.PurchaseOrderId,
            original.AdvanceShippingNoticeId,
            original.DockLocationId,
            original.ReceivingLocationId,
            "CORRECTION",
            original.DocumentNumber,
            original.DocumentNumber,
            original.SessionReference,
            input.Notes ?? original.Notes,
            userId);
        foreach (var sourceLine in original.Lines)
        {
            var line = CloneLineForCorrection(correction, sourceLine);
            correction.AddLine(line);
        }

        _context.Receipts.Add(correction);
        await _context.SaveChangesAsync(cancellationToken);
        original.MarkCorrected(userId, correction.Id, _clock.UtcNow.UtcDateTime);
        await _auditWriter.RecordAsync(
            new AuditRecord(WmsAuditActions.ReceiptCorrected, WmsAuditEntityTypes.Receipt,
                original.DocumentNumber, original.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["status"] = original.Status.ToString(),
                    ["correctionReceiptId"] = correction.Id,
                    ["correctionDocumentNumber"] = correction.DocumentNumber
                },
                ActorUserId: userId),
            cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new ReceiptCorrectionDto(Map(original), Map(correction)));
    }

    public async Task<Result<string>> ExportAsync(
        ReceiptListQuery request,
        CancellationToken cancellationToken = default)
    {
        var pageRequest = request with { Page = 1, PageSize = 200 };
        var page = await ListAsync(pageRequest, cancellationToken);
        if (page.IsFailure)
        {
            return page.ToFailure<string>();
        }

        var builder = new StringBuilder();
        builder.AppendLine("RECEIPT_NUMBER,STATUS,WAREHOUSE,SUPPLIER,PO_ID,ASN_ID,ITEM_SKU,RECEIVED_BASE_QUANTITY,ACCEPTED_BASE_QUANTITY,REJECTED_BASE_QUANTITY,DAMAGED_BASE_QUANTITY,QUARANTINED_BASE_QUANTITY,REMAINING_BASE_QUANTITY,LOT,SERIAL,LPN,RECEIVED_AT_UTC");
        foreach (var receipt in page.Value.Receipts)
        {
            foreach (var line in receipt.Lines)
            {
                builder.AppendLine(string.Join(',',
                    Csv(receipt.DocumentNumber),
                    Csv(receipt.Status),
                    Csv(receipt.WarehouseCode),
                    Csv(receipt.SupplierCode),
                    Csv(receipt.PurchaseOrderId),
                    Csv(receipt.AdvanceShippingNoticeId),
                    Csv(line.ItemSku),
                    Csv(line.ReceivedBaseQuantity),
                    Csv(line.AcceptedBaseQuantity),
                    Csv(line.RejectedBaseQuantity),
                    Csv(line.DamagedBaseQuantity),
                    Csv(line.QuarantinedBaseQuantity),
                    Csv(line.RemainingBaseQuantity),
                    Csv(line.LotNumber),
                    Csv(line.SerialNumber),
                    Csv(line.LicensePlateNumber),
                    Csv(receipt.ReceivedAtUtc?.ToString("O", CultureInfo.InvariantCulture))));
            }
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result> AddLinkAsync(
        int receiptLineId,
        ReceiptLineLinkInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var line = await _context.ReceiptLines
            .Include(candidate => candidate.Receipt)
            .FirstOrDefaultAsync(candidate => candidate.Id == receiptLineId, cancellationToken);
        if (line is null)
        {
            return Result.Failure(WmsErrors.NotFound("receipt.line_not_found", "The receipt line was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceiptsManage,
            line.Receipt.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization;
        }

        try
        {
            _context.ReceiptLineLinks.Add(new ReceiptLineLink(line, input.Type, input.Reference, userId, input.Notes));
            await _auditWriter.RecordAsync(
                new AuditRecord(WmsAuditActions.ReceiptLinkAdded, WmsAuditEntityTypes.ReceiptLineLink,
                    receiptLineId.ToString(CultureInfo.InvariantCulture), line.Receipt.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["type"] = input.Type.ToString(),
                        ["reference"] = input.Reference
                    },
                    ActorUserId: userId),
                cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation("receipt.link_invalid", exception.Message));
        }
    }

    private async Task<Result<ReceiptHeaderValidation>> ValidateHeaderAsync(
        ReceiptInput input,
        string permission,
        CancellationToken cancellationToken)
    {
        var authorization = await _warehouseAccessService.AuthorizeAsync(
            permission,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceiptHeaderValidation>();
        }

        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(
            candidate => candidate.Id == input.WarehouseId && candidate.IsActive,
            cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<ReceiptHeaderValidation>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The receipt warehouse was not found."));
        }

        var receivingLocation = await _context.Locations.FirstOrDefaultAsync(
            location => location.Id == input.ReceivingLocationId &&
                        location.WarehouseId == input.WarehouseId &&
                        location.IsActive && location.IsReceivable,
            cancellationToken);
        if (receivingLocation is null)
        {
            return Result.Failure<ReceiptHeaderValidation>(WmsErrors.Validation(
                "receipt.location_invalid",
                "The receipt receiving location is not active or receivable in the selected warehouse."));
        }

        if (input.DockLocationId.HasValue)
        {
            var dock = await _context.Locations.FirstOrDefaultAsync(
                location => location.Id == input.DockLocationId.Value &&
                            location.WarehouseId == input.WarehouseId &&
                            location.IsActive && location.Type == LocationType.Dock,
                cancellationToken);
            if (dock is null)
            {
                return Result.Failure<ReceiptHeaderValidation>(WmsErrors.Validation(
                    "receipt.dock_invalid",
                    "The receipt dock does not belong to the selected warehouse."));
            }
        }

        Supplier? supplier = null;
        if (input.SupplierId.HasValue)
        {
            supplier = await _context.Suppliers.FirstOrDefaultAsync(
                candidate => candidate.Id == input.SupplierId.Value,
                cancellationToken);
            if (supplier is null || !supplier.IsActive)
            {
                return Result.Failure<ReceiptHeaderValidation>(WmsErrors.Validation(
                    "receipt.supplier_invalid",
                    "The receipt supplier was not found or is inactive."));
            }
        }

        return Result.Success(new ReceiptHeaderValidation(warehouse, receivingLocation, supplier));
    }

    private async Task<Result<ReceiptLine>> BuildLineAsync(
        Receipt receipt,
        int lineNumber,
        ReceiptLineInput input,
        int warehouseId,
        int receivingLocationId,
        CancellationToken cancellationToken)
    {
        if (input.Quantity <= 0m)
        {
            return Result.Failure<ReceiptLine>(WmsErrors.Validation(
                "receipt.quantity_invalid",
                "Receipt line quantity must be positive."));
        }

        var itemSku = input.ItemSku.Trim().ToUpperInvariant();
        var item = await _context.Items.FirstOrDefaultAsync(
            candidate => candidate.Sku == itemSku,
            cancellationToken);
        if (item is null || !item.IsActive)
        {
            return Result.Failure<ReceiptLine>(WmsErrors.NotFound(
                "item.not_found",
                $"Item '{input.ItemSku}' was not found or is inactive."));
        }

        var ownerValidation = await ValidateOwnerAsync(
            input.OwnerKind,
            input.InventoryOwnerId,
            input.OwnerCodeSnapshot,
            cancellationToken);
        if (ownerValidation.IsFailure)
        {
            return ownerValidation.ToFailure<ReceiptLine>();
        }

        var conversion = !string.IsNullOrWhiteSpace(input.PackagingCode)
            ? await _quantityConversionService.ConvertPackagingToBaseAsync(
                item.Id,
                input.Quantity,
                input.PackagingCode!,
                cancellationToken: cancellationToken)
            : await _quantityConversionService.ConvertToBaseAsync(
                item.Id,
                input.Quantity,
                input.UnitOfMeasure,
                cancellationToken: cancellationToken);
        if (conversion.IsFailure)
        {
            return conversion.ToFailure<ReceiptLine>();
        }

        var sourceResult = await ValidateSourceReferencesAsync(
            warehouseId,
            item.Id,
            input,
            cancellationToken);
        if (sourceResult.IsFailure)
        {
            return sourceResult.ToFailure<ReceiptLine>();
        }

        var licensePlate = input.LicensePlateId.HasValue
            ? await _context.LicensePlates.FirstOrDefaultAsync(
                candidate => candidate.Id == input.LicensePlateId.Value && candidate.WarehouseId == warehouseId,
                cancellationToken)
            : null;
        if (input.LicensePlateId.HasValue && licensePlate is null)
        {
            return Result.Failure<ReceiptLine>(WmsErrors.Validation(
                "receipt.license_plate_invalid",
                "The receipt license plate does not belong to the receipt warehouse."));
        }

        var status = await _context.InventoryStatuses.AsNoTracking().FirstOrDefaultAsync(
            candidate => candidate.Id == InventoryStatusSystemIds.Available,
            cancellationToken);
        return Result.Success(CreateLineFromQuantity(
            receipt,
            lineNumber,
            item.Id,
            item.Sku,
            item.Name,
            new Quantity(conversion.Value.BaseQuantity, conversion.Value.ToSnapshot()),
            conversion.Value.BaseQuantity,
            input.PurchaseOrderId,
            input.PurchaseOrderLineId,
            input.AdvanceShippingNoticeId,
            input.AdvanceShippingNoticeLineId,
            receivingLocationId,
            input.LotNumber,
            input.ExpiryDate,
            input.SerialNumber,
            licensePlate,
            status,
            input.Notes,
            conversion.Value.ToSnapshot(),
            input.OwnerKind,
            input.InventoryOwnerId,
            input.OwnerCodeSnapshot));
    }

    private async Task<Result> ValidateSourceReferencesAsync(
        int warehouseId,
        int itemId,
        ReceiptLineInput input,
        CancellationToken cancellationToken)
    {
        if (input.PurchaseOrderId.HasValue != input.PurchaseOrderLineId.HasValue ||
            input.AdvanceShippingNoticeId.HasValue != input.AdvanceShippingNoticeLineId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.source_reference_incomplete",
                "Both document and line IDs are required for each receipt source."));
        }

        if (input.PurchaseOrderId.HasValue && input.AdvanceShippingNoticeId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.source_reference_ambiguous",
                "A receipt line may reference a PO or an ASN, not both directly."));
        }

        if (input.PurchaseOrderLineId.HasValue)
        {
            var purchaseOrderId = input.PurchaseOrderId!.Value;
            var line = await _context.PurchaseOrderLines
                .Include(candidate => candidate.PurchaseOrder)
                .FirstOrDefaultAsync(candidate => candidate.Id == input.PurchaseOrderLineId.Value &&
                                                  candidate.PurchaseOrderId == purchaseOrderId,
                    cancellationToken);
            if (line is null || line.ItemId != itemId || line.PurchaseOrder.WarehouseId != warehouseId)
            {
                return Result.Failure(WmsErrors.Validation(
                    "receipt.purchase_order_source_invalid",
                    "The purchase-order line does not match the receipt item or warehouse."));
            }
        }

        if (input.AdvanceShippingNoticeLineId.HasValue)
        {
            var advanceShippingNoticeId = input.AdvanceShippingNoticeId!.Value;
            var line = await _context.AdvanceShippingNoticeLines
                .Include(candidate => candidate.AdvanceShippingNotice)
                .FirstOrDefaultAsync(candidate => candidate.Id == input.AdvanceShippingNoticeLineId.Value &&
                                                  candidate.AdvanceShippingNoticeId == advanceShippingNoticeId,
                    cancellationToken);
            if (line is null || line.ItemId != itemId || line.AdvanceShippingNotice.WarehouseId != warehouseId)
            {
                return Result.Failure(WmsErrors.Validation(
                    "receipt.asn_source_invalid",
                    "The ASN line does not match the receipt item or warehouse."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateSourcePlans(ReceiptReceivingInput input)
    {
        if (input.PurchaseOrderPlan is not null && input.AdvanceShippingNoticePlan is not null)
        {
            return Result.Failure(WmsErrors.Validation(
                "receipt.source_plan_ambiguous",
                "A receipt may use the ASN plan, whose PO link is derived, or a direct PO plan."));
        }

        if (input.PurchaseOrderPlan is not null &&
            (input.PurchaseOrderId != input.PurchaseOrderPlan.PurchaseOrderId ||
             input.PurchaseOrderLineId != input.PurchaseOrderPlan.PurchaseOrderLineId))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receipt.purchase_order_plan_mismatch",
                "The persisted PO plan does not match the requested receipt source."));
        }

        if (input.AdvanceShippingNoticePlan is not null &&
            (input.AdvanceShippingNoticeId != input.AdvanceShippingNoticePlan.AdvanceShippingNoticeId ||
             input.AdvanceShippingNoticeLineId != input.AdvanceShippingNoticePlan.AdvanceShippingNoticeLineId))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receipt.asn_plan_mismatch",
                "The persisted ASN plan does not match the requested receipt source."));
        }

        if (input.AdvanceShippingNoticePlan is null && input.PurchaseOrderPlan is null &&
            (input.PurchaseOrderId.HasValue || input.PurchaseOrderLineId.HasValue ||
             input.AdvanceShippingNoticeId.HasValue || input.AdvanceShippingNoticeLineId.HasValue))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receipt.source_plan_required",
                "Receipt source validation must complete before stock receiving."));
        }

        return Result.Success();
    }

    private async Task<Result<ReceiptSourceSnapshot>> ResolveSourceAsync(
        ReceiptReceivingInput input,
        CancellationToken cancellationToken)
    {
        if (input.AdvanceShippingNoticePlan is not null)
        {
            var notice = await _context.AdvanceShippingNotices.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id == input.AdvanceShippingNoticePlan.AdvanceShippingNoticeId,
                cancellationToken);
            if (notice is null)
            {
                return Result.Failure<ReceiptSourceSnapshot>(WmsErrors.NotFound(
                    "asn.not_found",
                    "The receipt ASN was not found."));
            }

            return Result.Success(new ReceiptSourceSnapshot(
                notice.SupplierId,
                notice.SupplierCodeSnapshot,
                notice.SupplierNameSnapshot,
                notice.PurchaseOrderIdOrNull(),
                notice.Id));
        }

        if (input.PurchaseOrderPlan is not null)
        {
            var order = await _context.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id == input.PurchaseOrderPlan.PurchaseOrderId,
                cancellationToken);
            if (order is null)
            {
                return Result.Failure<ReceiptSourceSnapshot>(WmsErrors.NotFound(
                    "purchase_order.not_found",
                    "The receipt purchase order was not found."));
            }

            return Result.Success(new ReceiptSourceSnapshot(
                order.SupplierId,
                order.SupplierCodeSnapshot,
                order.SupplierNameSnapshot,
                order.Id,
                null));
        }

        if (input.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id == input.SupplierId.Value,
                cancellationToken);
            return supplier is null
                ? Result.Failure<ReceiptSourceSnapshot>(WmsErrors.NotFound(
                    "receipt.supplier_invalid",
                    "The receipt supplier was not found."))
                : Result.Success(new ReceiptSourceSnapshot(
                    supplier.Id,
                    supplier.Code,
                    supplier.LegalName,
                    null,
                    null));
        }

        return Result.Success(new ReceiptSourceSnapshot(null, null, null, null, null));
    }

    private async Task<Result> ValidateOwnerAsync(
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string? ownerCodeSnapshot,
        CancellationToken cancellationToken)
    {
        string normalizedCode;
        try
        {
            normalizedCode = InventoryOwnershipDimension.NormalizeOwnerCode(
                ownerKind,
                inventoryOwnerId,
                ownerCodeSnapshot);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation(
                "inventory.owner_invalid",
                exception.Message));
        }

        if (ownerKind == InventoryOwnerKind.CompanyOwned)
        {
            return Result.Success();
        }

        var owner = await _context.InventoryOwners
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == inventoryOwnerId && candidate.IsActive,
                cancellationToken);
        if (owner is null || owner.Kind != ownerKind || owner.OwnerCode != normalizedCode)
        {
            return Result.Failure(WmsErrors.Validation(
                "inventory.owner_invalid",
                "The selected inventory owner was not found, is inactive, or does not match the supplied owner snapshot."));
        }

        return Result.Success();
    }

    private static ReceiptLine CreateLineFromQuantity(
        Receipt receipt,
        int lineNumber,
        int itemId,
        string itemSku,
        string itemName,
        Quantity quantity,
        decimal expectedBaseQuantity,
        int? purchaseOrderId,
        int? purchaseOrderLineId,
        int? advanceShippingNoticeId,
        int? advanceShippingNoticeLineId,
        int receivingLocationId,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        LicensePlate? licensePlate,
        InventoryStatus? status,
        string? notes,
        QuantityConversionSnapshot conversion,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        var packaging = conversion.PackagingSnapshot;
        return new ReceiptLine(
            lineNumber,
            receipt.WarehouseId,
            itemId,
            itemSku,
            itemName,
            conversion.EnteredUnitOfMeasure,
            conversion.EnteredQuantity,
            conversion.BaseUnitOfMeasure,
            expectedBaseQuantity,
            conversion.ConversionFactorToBase,
            conversion.ResultPrecision,
            conversion.RoundingMode,
            conversion.RoundingDelta,
            conversion.ConversionPath,
            conversion.ConversionRuleIds,
            packaging?.PackagingId,
            packaging?.Version,
            packaging?.Code,
            packaging?.Name,
            packaging?.LocalizedName,
            packaging?.Type,
            packaging?.UnitOfMeasure,
            packaging?.UnitsPerPackage,
            packaging?.PartialPackagePolicy,
            packaging?.GrossWeightKg,
            packaging?.LengthCm,
            packaging?.WidthCm,
            packaging?.HeightCm,
            packaging?.VolumeCubicMeters,
            purchaseOrderId,
            purchaseOrderLineId,
            advanceShippingNoticeId,
            advanceShippingNoticeLineId,
            receivingLocationId,
            lotNumber,
            expiryDate,
            serialNumber,
            licensePlate?.Id,
            licensePlate?.Number,
            licensePlate?.IsSscc ?? false,
            status?.Id ?? InventoryStatusSystemIds.Available,
            status?.Code,
            status?.Name,
            notes,
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
    }

    private static ReceiptLine CloneLineForCorrection(Receipt receipt, ReceiptLine source)
    {
        return new ReceiptLine(
            source.LineNumber,
            receipt.WarehouseId,
            source.ItemId,
            source.ItemSkuSnapshot,
            source.ItemNameSnapshot,
            source.EnteredUnitOfMeasure,
            source.EnteredQuantity,
            source.BaseUnitOfMeasure,
            Math.Max(source.ReceivedBaseQuantity, source.ExpectedBaseQuantity),
            source.ConversionFactorToBase,
            source.ConversionPrecision,
            source.ConversionRoundingMode,
            source.ConversionRoundingDelta,
            source.ConversionPath,
            source.ConversionRuleIds,
            source.PackagingId,
            source.PackagingVersion,
            source.PackagingCode,
            source.PackagingName,
            source.PackagingLocalizedName,
            source.PackagingType,
            source.PackagingUnitOfMeasure,
            source.PackagingUnitsPerPackage,
            source.PackagingPartialPackagePolicy,
            source.PackagingGrossWeightKg,
            source.PackagingLengthCm,
            source.PackagingWidthCm,
            source.PackagingHeightCm,
            source.PackagingVolumeCubicMeters,
            source.PurchaseOrderId,
            source.PurchaseOrderLineId,
            source.AdvanceShippingNoticeId,
            source.AdvanceShippingNoticeLineId,
            source.ReceivingLocationId,
            source.LotNumberSnapshot,
            source.ExpiryDateSnapshot,
            source.SerialNumberSnapshot,
            source.LicensePlateId,
            source.LicensePlateNumberSnapshot,
            source.LicensePlateIsSscc,
            source.InventoryStatusId,
            source.InventoryStatusCodeSnapshot,
            source.InventoryStatusNameSnapshot,
            source.Notes,
            source.OwnerKind,
            source.InventoryOwnerId,
            source.OwnerCodeSnapshot);
    }

    private async Task<Receipt?> LoadReceiptAsync(
        int id,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = _context.Receipts
            .Include(receipt => receipt.Warehouse)
            .Include(receipt => receipt.Supplier)
            .Include(receipt => receipt.PurchaseOrder)
            .Include(receipt => receipt.AdvanceShippingNotice)
            .Include(receipt => receipt.DockLocation)
            .Include(receipt => receipt.ReceivingLocation)
            .Include(receipt => receipt.Lines)
                .ThenInclude(line => line.Movements)
                    .ThenInclude(link => link.Movement)
            .Include(receipt => receipt.Lines)
                .ThenInclude(line => line.Links)
            .AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(receipt => receipt.Id == id, cancellationToken);
    }

    private async Task<Result<Receipt>> LoadForManageAsync(int id, CancellationToken cancellationToken)
    {
        var receipt = await LoadReceiptAsync(id, asNoTracking: false, cancellationToken);
        if (receipt is null)
        {
            return Result.Failure<Receipt>(WmsErrors.NotFound("receipt.not_found", "The requested receipt was not found."));
        }

        var authorization = await _warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceiptsManage,
            receipt.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<Receipt>()
            : Result.Success(receipt);
    }

    private async Task<IQueryable<Receipt>> ApplyScopeAsync(
        IQueryable<Receipt> query,
        CancellationToken cancellationToken)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        return scope.HasGlobalAccess
            ? query
            : query.Where(receipt => scope.WarehouseIds.Contains(receipt.WarehouseId));
    }

    private async Task<string> AllocateDocumentNumberAsync(
        Warehouse warehouse,
        CancellationToken cancellationToken)
    {
        var updatedAt = _clock.UtcNow.UtcDateTime;
        var rowsUpdated = await _context.WarehouseNumberSequences
            .Where(sequence => sequence.WarehouseId == warehouse.Id &&
                               sequence.NextReceiptNumber < long.MaxValue)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(sequence => sequence.NextReceiptNumber, sequence => sequence.NextReceiptNumber + 1)
                .SetProperty(sequence => sequence.Revision, sequence => sequence.Revision + 1)
                .SetProperty(sequence => sequence.UpdatedAt, updatedAt),
                cancellationToken);

        long allocatedNumber;
        if (rowsUpdated == 1)
        {
            var nextNumber = await _context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouse.Id)
                .Select(sequence => sequence.NextReceiptNumber)
                .SingleAsync(cancellationToken);
            var trackedSequence = _context.ChangeTracker
                .Entries<WarehouseNumberSequence>()
                .FirstOrDefault(entry => entry.Entity.WarehouseId == warehouse.Id);
            if (trackedSequence is not null)
            {
                await trackedSequence.ReloadAsync(cancellationToken);
            }

            allocatedNumber = nextNumber - 1;
        }
        else
        {
            var currentNumber = await _context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouse.Id)
                .Select(sequence => (long?)sequence.NextReceiptNumber)
                .SingleOrDefaultAsync(cancellationToken);
            if (currentNumber.HasValue)
            {
                throw new InvalidOperationException("The warehouse receipt number sequence is exhausted.");
            }

            var sequence = new WarehouseNumberSequence(warehouse.Id);
            allocatedNumber = sequence.AllocateReceiptNumber();
            _context.WarehouseNumberSequences.Add(sequence);
        }

        return $"RCPT-{warehouse.Code}-{allocatedNumber:D6}";
    }

    private static QuantityConversionSnapshot EnsureConversionSnapshot(Quantity quantity, Item item)
    {
        return quantity.ConversionSnapshot ?? new QuantityConversionSnapshot(
            quantity.Value,
            item.PurchaseUnit,
            item.UnitOfMeasure,
            1m,
            4,
            QuantityRoundingMode.Reject,
            0m,
            "BASE");
    }

    private static ReceiptDto Map(Receipt receipt)
    {
        return new ReceiptDto(
            receipt.Id,
            receipt.DocumentNumber,
            receipt.WarehouseId,
            receipt.WarehouseCodeSnapshot,
            receipt.SupplierId,
            receipt.SupplierCodeSnapshot,
            receipt.SupplierNameSnapshot,
            receipt.PurchaseOrderId,
            receipt.AdvanceShippingNoticeId,
            receipt.DockLocationId,
            receipt.ReceivingLocationId,
            receipt.SourceType,
            receipt.SourceReference,
            receipt.ExternalReference,
            receipt.SessionReference,
            receipt.Notes,
            receipt.Status,
            receipt.CreatedAt,
            receipt.ReceivedAtUtc,
            receipt.OpenedAtUtc,
            receipt.CompletedAtUtc,
            receipt.CancelledAtUtc,
            receipt.ReversedAtUtc,
            receipt.CorrectedAtUtc,
            receipt.CorrectedByReceiptId,
            receipt.Revision,
            receipt.Lines.OrderBy(line => line.LineNumber).Select(line => new ReceiptLineDto(
                line.Id,
                line.LineNumber,
                line.ItemId,
                line.ItemSkuSnapshot,
                line.ItemNameSnapshot,
                line.EnteredUnitOfMeasure,
                line.EnteredQuantity,
                line.BaseUnitOfMeasure,
                line.ExpectedBaseQuantity,
                line.ReceivedBaseQuantity,
                line.AcceptedBaseQuantity,
                line.RejectedBaseQuantity,
                line.DamagedBaseQuantity,
                line.QuarantinedBaseQuantity,
                line.RemainingBaseQuantity,
                line.PurchaseOrderId,
                line.PurchaseOrderLineId,
                line.AdvanceShippingNoticeId,
                line.AdvanceShippingNoticeLineId,
                line.LotNumberSnapshot,
                line.ExpiryDateSnapshot,
                line.SerialNumberSnapshot,
                line.LicensePlateId,
                line.LicensePlateNumberSnapshot,
                line.LicensePlateIsSscc,
                line.InventoryStatusId,
                line.InventoryStatusCodeSnapshot,
                line.InventoryStatusNameSnapshot,
                line.Notes,
                line.Movements.Select(movement => movement.MovementId).ToArray(),
                line.Links.Select(link => new ReceiptLineLinkDto(
                    link.Id,
                    link.Type,
                    link.Reference,
                    link.UserId,
                    link.Notes)).ToArray(),
                line.IsFullyReceived,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot)).ToArray(),
            receipt.CanEdit,
            receipt.Status is ReceiptStatus.Receiving or ReceiptStatus.Exception,
            receipt.CanCancel,
            receipt.CanReverse,
            receipt.CanCorrect);
    }

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record ReceiptHeaderValidation(
        Warehouse Warehouse,
        Location ReceivingLocation,
        Supplier? Supplier);

    private sealed record ReceiptSourceSnapshot(
        int? SupplierId,
        string? SupplierCode,
        string? SupplierName,
        int? PurchaseOrderId,
        int? AdvanceShippingNoticeId);
}

internal static class AdvanceShippingNoticeReceiptSourceExtensions
{
    public static int? PurchaseOrderIdOrNull(this AdvanceShippingNotice notice) =>
        notice.Lines.Select(line => line.PurchaseOrderId).FirstOrDefault(value => value.HasValue);
}
