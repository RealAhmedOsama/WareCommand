using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.DTOs;
using Wms.Application.Identification;
using Wms.Application.Identity;
using Wms.Application.LicensePlates;
using Wms.Application.Receiving;
using Wms.Application.Units;
using Wms.Application.UseCases.Receiving;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Receiving;

/// <summary>
/// Server-owned receiving execution ledger. It keeps the operator session and
/// every client operation durable while delegating the actual stock mutation to
/// the existing transactional receiving use case.
/// </summary>
public sealed class ReceivingExecutionService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    IAuditWriter auditWriter,
    IIdentificationService identificationService,
    IItemQuantityConversionService quantityConversionService,
    ILicensePlateService licensePlateService,
    IReceiptService receiptService,
    IReceiveItemUseCase receiveItemUseCase,
    ILogger<ReceivingExecutionService> logger) : IReceivingExecutionService
{
    public async Task<Result<ReceivingSessionDto>> StartAsync(
        ReceivingSessionStartInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        var overrideResult = await RequireOverrideAsync(
            input.WarehouseId,
            input.SupervisorOverride || input.SourceType == ReceivingSessionSourceType.BlindReceipt,
            input.SupervisorOverrideReason,
            cancellationToken);
        if (overrideResult.IsFailure)
        {
            return overrideResult.ToFailure<ReceivingSessionDto>();
        }

        var warehouse = await context.Warehouses
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == input.WarehouseId && candidate.IsActive, cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "warehouse.not_found",
                "The receiving warehouse was not found or is inactive."));
        }

        var receivingLocation = await context.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(location =>
                location.Id == input.ReceivingLocationId &&
                location.WarehouseId == input.WarehouseId &&
                location.IsActive &&
                location.IsReceivable &&
                (location.Type == LocationType.Dock ||
                 location.Type == LocationType.Receiving ||
                 location.Type == LocationType.Staging ||
                 location.Type == LocationType.Storage ||
                 location.Type == LocationType.Bulk ||
                 location.Type == LocationType.Returns ||
                 location.Type == LocationType.Transit),
                cancellationToken);
        if (receivingLocation is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Validation(
                "receiving.session_location_invalid",
                "The receiving location is not active, receivable, or part of the selected warehouse."));
        }

        var sourceValidation = await ValidateStartSourceAsync(input, cancellationToken);
        if (sourceValidation.IsFailure)
        {
            return sourceValidation.ToFailure<ReceivingSessionDto>();
        }

        var sessionReference = NormalizeSessionReference(input.SessionReference);
        var existing = await context.ReceivingSessions
            .Include(session => session.Lines)
            .Include(session => session.Scans)
            .SingleOrDefaultAsync(session =>
                session.WarehouseId == input.WarehouseId &&
                session.SessionReference == sessionReference,
                cancellationToken);
        if (existing is not null)
        {
            return existing.CreatedByUserId == userId &&
                   existing.WarehouseId == input.WarehouseId &&
                   existing.ReceivingLocationId == input.ReceivingLocationId &&
                   existing.SourceType == input.SourceType &&
                   existing.PurchaseOrderId == input.PurchaseOrderId &&
                   existing.AdvanceShippingNoticeId == input.AdvanceShippingNoticeId &&
                   existing.DockLocationId == input.DockLocationId &&
                   string.Equals(existing.ExternalReference, input.ExternalReference?.Trim().ToUpperInvariant(), StringComparison.Ordinal)
                ? Result.Success(Map(existing))
                : Result.Failure<ReceivingSessionDto>(WmsErrors.Conflict(
                    "receiving.session_reference_conflict",
                    "The receiving session reference is already in use."));
        }

        var now = clock.UtcNow.UtcDateTime;
        ReceivingSession session;
        try
        {
            session = new ReceivingSession(
                input.WarehouseId,
                input.ReceivingLocationId,
                input.SourceType,
                sessionReference,
                userId,
                now,
                input.PurchaseOrderId,
                input.AdvanceShippingNoticeId,
                input.DockLocationId,
                input.ExternalReference,
                input.Notes,
                input.SupervisorOverride || input.SourceType == ReceivingSessionSourceType.BlindReceipt,
                input.SupervisorOverrideReason);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Validation(
                "receiving.session_invalid",
                exception.Message));
        }

        context.ReceivingSessions.Add(session);
        var demandResult = await AddDemandLinesAsync(session, input, cancellationToken);
        if (demandResult.IsFailure)
        {
            return demandResult.ToFailure<ReceivingSessionDto>();
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ReceivingSessionStarted,
                WmsAuditEntityTypes.ReceivingSession,
                sessionReference,
                input.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["sourceType"] = input.SourceType.ToString(),
                    ["purchaseOrderId"] = input.PurchaseOrderId,
                    ["advanceShippingNoticeId"] = input.AdvanceShippingNoticeId,
                    ["lineCount"] = session.Lines.Count,
                    ["supervisorOverride"] = session.SupervisorOverride
                },
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Receiving session {SessionReference} started for warehouse {WarehouseId} by {UserId}",
            sessionReference,
            input.WarehouseId,
            userId);
        return Result.Success(Map(session));
    }

    public async Task<Result<ReceivingSessionDto>> GetAsync(
        int sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, asNoTracking: true, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<ReceivingSessionDto>()
            : Result.Success(Map(session));
    }

    public Task<Result<ReceivingSessionDto>> PauseAsync(
        int sessionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            sessionId,
            userId,
            "pause",
            static (session, at) => session.Pause(at),
            cancellationToken);

    public Task<Result<ReceivingSessionDto>> ResumeAsync(
        int sessionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ChangeLifecycleAsync(
            sessionId,
            userId,
            "resume",
            static (session, at) => session.Resume(at),
            cancellationToken);

    public async Task<Result<ReceivingSessionDto>> ScanAsync(
        int sessionId,
        ReceivingScanInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.ClientOperationId) || string.IsNullOrWhiteSpace(input.RawScanValue))
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Validation(
                "receiving.scan_identity_required",
                "A client operation ID and raw scan value are required."));
        }

        var session = await LoadSessionAsync(sessionId, asNoTracking: false, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        if (!session.CanScan)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.session_not_open",
                $"A receiving session in {session.Status} cannot accept a scan."));
        }

        var scanOverride = await RequireOverrideAsync(
            session.WarehouseId,
            input.CreateLicensePlate,
            input.SupervisorOverrideReason,
            cancellationToken);
        if (scanOverride.IsFailure)
        {
            return scanOverride.ToFailure<ReceivingSessionDto>();
        }

        var existingScan = session.Scans.SingleOrDefault(scan =>
            string.Equals(scan.ClientOperationId, input.ClientOperationId.Trim(), StringComparison.Ordinal));
        if (existingScan is not null &&
            !string.Equals(existingScan.RawScanValue, input.RawScanValue.Trim(), StringComparison.Ordinal))
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Conflict(
                "receiving.scan_operation_conflict",
                "The client operation ID was already used with a different scan value."));
        }

        if (existingScan is not null &&
            existingScan.Status is (ReceivingScanStatus.Completed or ReceivingScanStatus.Corrected))
        {
            return Result.Success(Map(session));
        }

        var resolved = await ResolveScanAsync(session, input, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.ToFailure<ReceivingSessionDto>();
        }

        var item = await context.Items
            .Include(candidate => candidate.Packagings)
            .SingleOrDefaultAsync(candidate => candidate.Sku == resolved.Value.ItemSku && candidate.IsActive, cancellationToken);
        if (item is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "item.not_found",
                $"Item with SKU '{resolved.Value.ItemSku}' was not found or is inactive."));
        }

        var location = await context.Locations
            .SingleOrDefaultAsync(candidate =>
                candidate.Code == resolved.Value.DestinationLocationCode &&
                candidate.WarehouseId == session.WarehouseId &&
                candidate.IsActive &&
                candidate.IsReceivable &&
                (candidate.Type == LocationType.Dock ||
                 candidate.Type == LocationType.Receiving ||
                 candidate.Type == LocationType.Staging ||
                 candidate.Type == LocationType.Storage ||
                 candidate.Type == LocationType.Bulk ||
                 candidate.Type == LocationType.Returns ||
                 candidate.Type == LocationType.Transit),
                cancellationToken);
        if (location is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Validation(
                "receiving.scan_location_invalid",
                "The scanned destination is not an active receiving location in this warehouse."));
        }

        var conversion = await ConvertToBaseAsync(item, resolved.Value, cancellationToken);
        if (conversion.IsFailure)
        {
            return conversion.ToFailure<ReceivingSessionDto>();
        }

        var lineResult = ResolveOrCreateSessionLine(
            session,
            item,
            resolved.Value,
            input.PurchaseOrderLineId,
            input.AdvanceShippingNoticeLineId);
        if (lineResult.IsFailure)
        {
            return lineResult.ToFailure<ReceivingSessionDto>();
        }

        var line = lineResult.Value;

        try
        {
            line.EnsureCanRecordScan(conversion.Value);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Conflict(
                "receiving.scan_tolerance_exceeded",
                exception.Message));
        }

        var licensePlateResult = await ResolveLicensePlateAsync(
            session,
            location.Id,
            resolved.Value,
            userId,
            cancellationToken);
        if (licensePlateResult.IsFailure)
        {
            return licensePlateResult.ToFailure<ReceivingSessionDto>();
        }

        var identityAfterLicensePlate = await ValidateDemandIdentityAsync(
            line,
            resolved.Value,
            licensePlateResult.Value,
            cancellationToken);
        if (identityAfterLicensePlate.IsFailure)
        {
            return identityAfterLicensePlate.ToFailure<ReceivingSessionDto>();
        }

        if (line.Id == 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        var now = clock.UtcNow.UtcDateTime;
        var clientOperationId = input.ClientOperationId.Trim();
        var scan = existingScan ?? new ReceivingSessionScan(
            session,
            clientOperationId,
            input.RawScanValue,
            item.Sku,
            resolved.Value.Quantity,
            resolved.Value.UnitOfMeasure,
            resolved.Value.PackagingCode,
            resolved.Value.LotNumber,
            resolved.Value.ExpiryDate,
            resolved.Value.SerialNumber,
            licensePlateResult.Value,
            line.Id,
            userId,
            now,
            location.Code,
            resolved.Value.Notes);
        if (existingScan is null)
        {
            session.AddScan(scan);
        }

        var idempotencyKey = $"receiving-session:{session.Id}:{clientOperationId}";
        scan.SetIdempotencyKey(idempotencyKey);
        await context.SaveChangesAsync(cancellationToken);

        var receiveResult = await receiveItemUseCase.ExecuteAsync(
            new ReceiveItemDto(
                item.Sku,
                location.Code,
                resolved.Value.Quantity,
                resolved.Value.LotNumber,
                resolved.Value.SerialNumber,
                resolved.Value.ReferenceNumber ?? session.SessionReference,
                resolved.Value.Notes,
                resolved.Value.ExpiryDate,
                resolved.Value.ManufacturedDate,
                resolved.Value.UnitOfMeasure,
                resolved.Value.PackagingCode,
                licensePlateResult.Value,
                session.PurchaseOrderId,
                line.PurchaseOrderLineId,
                session.AdvanceShippingNoticeId,
                line.AdvanceShippingNoticeLineId,
                session.SessionReference),
            userId,
            idempotencyKey,
            cancellationToken);
        if (receiveResult.IsFailure)
        {
            scan.MarkFailed(receiveResult.ErrorCode, receiveResult.Error, now);
            session.Touch(now);
            await context.SaveChangesAsync(cancellationToken);
            return receiveResult.ToFailure<ReceivingSessionDto>();
        }

        try
        {
            line.RecordScan(conversion.Value, now);
            scan.MarkCompleted(
                conversion.Value,
                receiveResult.Value.MovementId,
                receiveResult.Value.ReceiptId,
                receiveResult.Value.ReceiptDocumentNumber,
                now);
            session.Touch(now);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogCritical(
                exception,
                "Receiving session {SessionId} could not reconcile successful movement {MovementId}",
                session.Id,
                receiveResult.Value.MovementId);
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Unexpected(
                "receiving.session_reconciliation_failed",
                "The movement succeeded but the receiving session ledger could not be reconciled."));
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.ReceivingScanCompleted,
                WmsAuditEntityTypes.ReceivingSessionScan,
                scan.Id.ToString(CultureInfo.InvariantCulture),
                session.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["sessionReference"] = session.SessionReference,
                    ["clientOperationId"] = scan.ClientOperationId,
                    ["movementId"] = scan.MovementId,
                    ["receiptId"] = scan.ReceiptId,
                    ["baseQuantity"] = scan.BaseQuantity
                },
                ActorUserId: userId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(Map(session));
    }

    public async Task<Result<ReceivingSessionDto>> CompleteAsync(
        int sessionId,
        ReceivingSessionCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, asNoTracking: false, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        var overrideResult = await RequireOverrideAsync(
            session.WarehouseId,
            input.AllowPartial && session.HasOpenDemand,
            input.SupervisorOverrideReason,
            cancellationToken);
        if (overrideResult.IsFailure)
        {
            return overrideResult.ToFailure<ReceivingSessionDto>();
        }

        try
        {
            var now = clock.UtcNow.UtcDateTime;
            session.Complete(input.AllowPartial, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReceivingSessionCompleted,
                    WmsAuditEntityTypes.ReceivingSession,
                    session.SessionReference,
                    session.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = session.Status.ToString(),
                        ["allowPartial"] = input.AllowPartial,
                        ["openDemand"] = session.HasOpenDemand
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(session));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.session_completion_invalid",
                exception.Message));
        }
    }

    public async Task<Result<ReceivingSessionDto>> CancelAsync(
        int sessionId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, asNoTracking: false, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        var overrideResult = await RequireOverrideAsync(
            session.WarehouseId,
            session.HasScans,
            reason,
            cancellationToken);
        if (overrideResult.IsFailure)
        {
            return overrideResult.ToFailure<ReceivingSessionDto>();
        }

        try
        {
            var now = clock.UtcNow.UtcDateTime;
            session.Cancel(userId, reason, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReceivingSessionCancelled,
                    WmsAuditEntityTypes.ReceivingSession,
                    session.SessionReference,
                    session.WarehouseId,
                    After: new Dictionary<string, object?> { ["reason"] = reason },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(session));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.session_cancellation_invalid",
                exception.Message));
        }
    }

    public async Task<Result<ReceivingSessionDto>> CorrectScanAsync(
        int sessionId,
        int scanId,
        ReceivingSessionCorrectionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId, asNoTracking: false, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        var overrideResult = await RequireOverrideAsync(
            session.WarehouseId,
            true,
            input.Reason,
            cancellationToken);
        if (overrideResult.IsFailure)
        {
            return overrideResult.ToFailure<ReceivingSessionDto>();
        }

        var scan = session.Scans.SingleOrDefault(candidate => candidate.Id == scanId);
        if (scan is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.scan_not_found",
                "The receiving scan was not found in this session."));
        }

        if (scan.Status != ReceivingScanStatus.Completed || !scan.ReceiptId.HasValue || !scan.BaseQuantity.HasValue)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.scan_not_correctable",
                "Only a completed scan with a persisted receipt can be corrected."));
        }

        var reversal = await receiptService.ReverseAsync(
            scan.ReceiptId.Value,
            userId,
            input.Reason,
            cancellationToken);
        if (reversal.IsFailure)
        {
            return reversal.ToFailure<ReceivingSessionDto>();
        }

        var line = scan.SessionLine;
        if (line is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.Unexpected(
                "receiving.scan_line_missing",
                "The receiving scan has no session line."));
        }

        try
        {
            line.ReverseScan(scan.BaseQuantity.Value, clock.UtcNow.UtcDateTime);
            scan.MarkCorrected(input.Reason, clock.UtcNow.UtcDateTime);
            session.Touch(clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ReceivingScanCorrected,
                    WmsAuditEntityTypes.ReceivingSessionScan,
                    scan.Id.ToString(CultureInfo.InvariantCulture),
                    session.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["sessionReference"] = session.SessionReference,
                        ["receiptId"] = scan.ReceiptId,
                        ["reason"] = input.Reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(session));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.scan_correction_invalid",
                exception.Message));
        }
    }

    private async Task<Result<ReceivingSessionDto>> ChangeLifecycleAsync(
        int sessionId,
        string userId,
        string operation,
        Action<ReceivingSession, DateTime> change,
        CancellationToken cancellationToken)
    {
        var session = await LoadSessionAsync(sessionId, asNoTracking: false, cancellationToken);
        if (session is null)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.NotFound(
                "receiving.session_not_found",
                "The receiving session was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            session.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ReceivingSessionDto>();
        }

        try
        {
            var now = clock.UtcNow.UtcDateTime;
            change(session, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    operation == "pause"
                        ? WmsAuditActions.ReceivingSessionPaused
                        : WmsAuditActions.ReceivingSessionResumed,
                    WmsAuditEntityTypes.ReceivingSession,
                    session.SessionReference,
                    session.WarehouseId,
                    After: new Dictionary<string, object?> { ["status"] = session.Status.ToString() },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(session));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<ReceivingSessionDto>(WmsErrors.BusinessRule(
                "receiving.session_lifecycle_invalid",
                exception.Message));
        }
    }

    private async Task<Result> ValidateStartSourceAsync(
        ReceivingSessionStartInput input,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(input.SourceType))
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.source_type_invalid",
                "The receiving source type is not supported."));
        }

        if (input.SourceType == ReceivingSessionSourceType.PurchaseOrder && !input.PurchaseOrderId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.purchase_order_required",
                "A purchase order is required for a purchase-order receiving session."));
        }

        if (input.SourceType == ReceivingSessionSourceType.AdvanceShippingNotice && !input.AdvanceShippingNoticeId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.asn_required",
                "An ASN is required for an ASN receiving session."));
        }

        if (input.SourceType == ReceivingSessionSourceType.DockArrival && !input.DockLocationId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.dock_required",
                "A dock location is required for a dock-arrival receiving session."));
        }

        if (input.SourceType == ReceivingSessionSourceType.ExternalReference && string.IsNullOrWhiteSpace(input.ExternalReference))
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.external_reference_required",
                "An external reference is required for an external receiving session."));
        }

        if (input.DockLocationId.HasValue)
        {
            var dockExists = await context.Locations.AnyAsync(location =>
                location.Id == input.DockLocationId.Value &&
                location.WarehouseId == input.WarehouseId &&
                location.IsActive &&
                location.Type == LocationType.Dock,
                cancellationToken);
            if (!dockExists)
            {
                return Result.Failure(WmsErrors.Validation(
                    "receiving.dock_invalid",
                    "The dock location is not active in the selected warehouse."));
            }
        }

        if (input.PurchaseOrderId.HasValue && input.AdvanceShippingNoticeId.HasValue)
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.source_ambiguous",
                "A receiving session cannot be linked to both a purchase order and an ASN."));
        }

        if (input.SourceType == ReceivingSessionSourceType.PurchaseOrder)
        {
            var exists = await context.PurchaseOrders.AnyAsync(order =>
                order.Id == input.PurchaseOrderId && order.WarehouseId == input.WarehouseId,
                cancellationToken);
            if (!exists)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "purchase_order.not_found",
                    "The purchase order was not found in the selected warehouse."));
            }
        }

        if (input.SourceType == ReceivingSessionSourceType.AdvanceShippingNotice)
        {
            var exists = await context.AdvanceShippingNotices.AnyAsync(notice =>
                notice.Id == input.AdvanceShippingNoticeId && notice.WarehouseId == input.WarehouseId,
                cancellationToken);
            if (!exists)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "asn.not_found",
                    "The ASN was not found in the selected warehouse."));
            }
        }

        return Result.Success();
    }

    private async Task<Result> AddDemandLinesAsync(
        ReceivingSession session,
        ReceivingSessionStartInput input,
        CancellationToken cancellationToken)
    {
        if (input.SourceType == ReceivingSessionSourceType.PurchaseOrder)
        {
            var order = await context.PurchaseOrders
                .Include(candidate => candidate.Lines)
                .SingleAsync(candidate => candidate.Id == input.PurchaseOrderId, cancellationToken);
            if (!order.CanReceive)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "purchase_order.not_receivable",
                    $"A purchase order in {order.Status} cannot be received."));
            }

            foreach (var line in order.Lines.Where(candidate => !candidate.IsClosed))
            {
                session.AddLine(new ReceivingSessionLine(
                    session,
                    line.LineNumber,
                    line.ItemId,
                    line.ItemSkuSnapshot,
                    line.ItemNameSnapshot,
                    line.BaseUnitOfMeasure,
                    line.OrderedBaseQuantity,
                    line.OverDeliveryTolerancePercent,
                    line.UnderDeliveryTolerancePercent,
                    line.Id,
                    previouslyReceivedBaseQuantity: line.ReceivedBaseQuantity));
            }
        }
        else if (input.SourceType == ReceivingSessionSourceType.AdvanceShippingNotice)
        {
            var notice = await context.AdvanceShippingNotices
                .Include(candidate => candidate.Lines)
                .SingleAsync(candidate => candidate.Id == input.AdvanceShippingNoticeId, cancellationToken);
            if (!notice.CanReceive)
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "asn.not_receivable",
                    $"An ASN in {notice.Status} cannot be received."));
            }

            foreach (var line in notice.Lines.Where(candidate => !candidate.IsClosed))
            {
                session.AddLine(new ReceivingSessionLine(
                    session,
                    line.LineNumber,
                    line.ItemId,
                    line.ItemSkuSnapshot,
                    line.ItemNameSnapshot,
                    line.BaseUnitOfMeasure,
                    line.ExpectedBaseQuantity,
                    line.OverDeliveryTolerancePercent,
                    line.UnderDeliveryTolerancePercent,
                    advanceShippingNoticeLineId: line.Id,
                    previouslyReceivedBaseQuantity: line.ReceivedBaseQuantity,
                    expectedLotNumber: line.PreAdvisedLotNumber,
                    expectedExpiryDate: line.PreAdvisedExpiryDate,
                    expectedSerialNumber: line.PreAdvisedSerialNumber,
                    expectedLicensePlateNumber: line.ExpectedLicensePlateNumber));
            }
        }

        if ((input.SourceType is ReceivingSessionSourceType.PurchaseOrder or ReceivingSessionSourceType.AdvanceShippingNotice) &&
            session.Lines.Count == 0)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "receiving.demand_empty",
                "The selected inbound demand has no open lines."));
        }

        return Result.Success();
    }

    private async Task<Result<ResolvedReceivingScan>> ResolveScanAsync(
        ReceivingSession session,
        ReceivingScanInput input,
        CancellationToken cancellationToken)
    {
        var itemSku = input.ItemSku?.Trim().ToUpperInvariant();
        var packagingCode = input.PackagingCode?.Trim().ToUpperInvariant();
        var lotNumber = input.LotNumber?.Trim().ToUpperInvariant();
        var serialNumber = input.SerialNumber?.Trim().ToUpperInvariant();
        var licensePlateNumber = input.LicensePlateNumber?.Trim().ToUpperInvariant();
        var expiryDate = input.ExpiryDate;
        var quantity = input.Quantity;
        var destinationLocationCode = input.DestinationLocationCode?.Trim().ToUpperInvariant();

        var identification = await identificationService.ResolveAsync(
            new IdentificationLookupRequest(input.RawScanValue, session.WarehouseId),
            cancellationToken);
        var gs1Applied = false;
        if (identification.IsSuccess)
        {
            var resolved = identification.Value;
            itemSku ??= resolved.ItemSku?.Trim().ToUpperInvariant();
            packagingCode ??= resolved.PackagingCode?.Trim().ToUpperInvariant();
            destinationLocationCode ??= resolved.LocationCode?.Trim().ToUpperInvariant();
            licensePlateNumber ??= resolved.LicensePlate?.Trim().ToUpperInvariant();
            if (resolved.Gs1 is not null)
            {
                gs1Applied = true;
                lotNumber ??= resolved.Gs1.Lot?.Trim().ToUpperInvariant();
                serialNumber ??= resolved.Gs1.Serial?.Trim().ToUpperInvariant();
                licensePlateNumber ??= resolved.Gs1.Sscc?.Trim().ToUpperInvariant();
                quantity ??= resolved.Gs1.Quantity;
                expiryDate ??= resolved.Gs1.ExpiryDate.HasValue
                    ? resolved.Gs1.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                    : null;
            }
        }

        if (!gs1Applied && Gs1Parser.TryParse(input.RawScanValue, out var gs1) && gs1 is not null)
        {
            lotNumber ??= gs1.Lot;
            serialNumber ??= gs1.Serial;
            licensePlateNumber ??= gs1.Sscc;
            quantity ??= gs1.Quantity;
            expiryDate ??= gs1.ExpiryDate.HasValue
                ? gs1.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
                : null;
        }

        if (string.IsNullOrWhiteSpace(itemSku))
        {
            return Result.Failure<ResolvedReceivingScan>(WmsErrors.NotFound(
                "receiving.item_scan_unresolved",
                "The scan did not resolve to an active item. Scan an item/GTIN or provide the item SKU."));
        }

        if (quantity is <= 0m)
        {
            return Result.Failure<ResolvedReceivingScan>(WmsErrors.Validation(
                "receiving.scan_quantity_invalid",
                "The scanned quantity must be positive."));
        }

        quantity ??= 1m;

        var locationCode = destinationLocationCode;
        if (string.IsNullOrWhiteSpace(locationCode))
        {
            locationCode = await context.Locations
                .Where(location => location.Id == session.ReceivingLocationId)
                .Select(location => location.Code)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(locationCode))
        {
            return Result.Failure<ResolvedReceivingScan>(WmsErrors.NotFound(
                "receiving.location_scan_unresolved",
                "A receiving destination is required."));
        }

        return Result.Success(new ResolvedReceivingScan(
            itemSku,
            quantity.Value,
            input.UnitOfMeasure,
            packagingCode,
            lotNumber,
            expiryDate,
            input.ManufacturedDate,
            serialNumber,
            licensePlateNumber,
            input.LicensePlateId,
            locationCode,
            input.ReferenceNumber,
            input.Notes)
        {
            CreateLicensePlate = input.CreateLicensePlate
        });
    }

    private static Result<ReceivingSessionLine> ResolveOrCreateSessionLine(
        ReceivingSession session,
        Item item,
        ResolvedReceivingScan resolved,
        int? purchaseOrderLineId,
        int? advanceShippingNoticeLineId)
    {
        var candidates = session.Lines
            .Where(line => line.ItemId == item.Id)
            .Where(line => !purchaseOrderLineId.HasValue || line.PurchaseOrderLineId == purchaseOrderLineId)
            .Where(line => !advanceShippingNoticeLineId.HasValue || line.AdvanceShippingNoticeLineId == advanceShippingNoticeLineId)
            .ToArray();
        if (candidates.Length == 1)
        {
            return Result.Success(candidates[0]);
        }

        if (candidates.Length > 1)
        {
            var matchingLot = candidates.FirstOrDefault(line =>
                string.Equals(line.ExpectedLotNumber, resolved.LotNumber, StringComparison.OrdinalIgnoreCase));
            if (matchingLot is not null)
            {
                return Result.Success(matchingLot);
            }

            return Result.Failure<ReceivingSessionLine>(WmsErrors.Validation(
                "receiving.demand_line_required",
                "The scanned item matches multiple demand lines; select the demand line before scanning quantity."));
        }

        if (session.SourceType is ReceivingSessionSourceType.PurchaseOrder or ReceivingSessionSourceType.AdvanceShippingNotice)
        {
            return Result.Failure<ReceivingSessionLine>(WmsErrors.Conflict(
                "receiving.item_not_in_demand",
                $"Item '{item.Sku}' is not an open line in the selected inbound demand."));
        }

        try
        {
            var line = new ReceivingSessionLine(
                session,
                session.Lines.Count + 1,
                item.Id,
                item.Sku,
                item.Name,
                item.UnitOfMeasure,
                expectedBaseQuantity: null);
            session.AddLine(line);
            return Result.Success(line);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<ReceivingSessionLine>(WmsErrors.Validation(
                "receiving.demand_line_invalid",
                exception.Message));
        }
    }

    private async Task<Result<decimal>> ConvertToBaseAsync(
        Item item,
        ResolvedReceivingScan scan,
        CancellationToken cancellationToken)
    {
        var result = !string.IsNullOrWhiteSpace(scan.PackagingCode)
            ? await quantityConversionService.ConvertPackagingToBaseAsync(
                item.Id,
                scan.Quantity,
                scan.PackagingCode,
                cancellationToken: cancellationToken)
            : await quantityConversionService.ConvertToBaseAsync(
                item.Id,
                scan.Quantity,
                scan.UnitOfMeasure ?? item.PurchaseUnit,
                cancellationToken: cancellationToken);
        return result.IsFailure
            ? result.ToFailure<decimal>()
            : Result.Success(result.Value.BaseQuantity);
    }

    private async Task<Result> ValidateDemandIdentityAsync(
        ReceivingSessionLine line,
        ResolvedReceivingScan scan,
        int? licensePlateId,
        CancellationToken cancellationToken)
    {
        if (line.ExpectedLotNumber is not null &&
            !string.Equals(line.ExpectedLotNumber, scan.LotNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receiving.lot_mismatch",
                "The scanned lot does not match the pre-advised receiving demand."));
        }

        if (line.ExpectedSerialNumber is not null &&
            !string.Equals(line.ExpectedSerialNumber, scan.SerialNumber, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receiving.serial_mismatch",
                "The scanned serial number does not match the pre-advised receiving demand."));
        }

        if (line.ExpectedExpiryDate.HasValue &&
            (!scan.ExpiryDate.HasValue || scan.ExpiryDate.Value.Date != line.ExpectedExpiryDate.Value.Date))
        {
            return Result.Failure(WmsErrors.Conflict(
                "receiving.expiry_mismatch",
                "The scanned expiry date does not match the pre-advised receiving demand."));
        }

        if (line.ExpectedLicensePlateNumber is null)
        {
            return Result.Success();
        }

        var licensePlateNumber = scan.LicensePlateNumber;
        if (licensePlateId.HasValue && string.IsNullOrWhiteSpace(licensePlateNumber))
        {
            licensePlateNumber = await context.LicensePlates
                .Where(plate => plate.Id == licensePlateId.Value)
                .Select(plate => plate.Number)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return string.Equals(
            line.ExpectedLicensePlateNumber,
            licensePlateNumber,
            StringComparison.OrdinalIgnoreCase)
            ? Result.Success()
            : Result.Failure(WmsErrors.Conflict(
                "receiving.license_plate_mismatch",
                "The scanned license plate does not match the pre-advised receiving demand."));
    }

    private async Task<Result<int?>> ResolveLicensePlateAsync(
        ReceivingSession session,
        int locationId,
        ResolvedReceivingScan resolved,
        string userId,
        CancellationToken cancellationToken)
    {
        if (resolved.LicensePlateId.HasValue)
        {
            return Result.Success<int?>(resolved.LicensePlateId.Value);
        }

        if (string.IsNullOrWhiteSpace(resolved.LicensePlateNumber))
        {
            return Result.Success<int?>(null);
        }

        var existing = await context.LicensePlates.SingleOrDefaultAsync(plate =>
            plate.WarehouseId == session.WarehouseId &&
            plate.Number == resolved.LicensePlateNumber,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Success<int?>(existing.Id);
        }

        if (!resolved.CreateLicensePlate)
        {
            return Result.Failure<int?>(WmsErrors.NotFound(
                "license_plate.not_found",
                "The scanned license plate does not exist. Enable authorized LPN creation to create it."));
        }

        var created = await licensePlateService.CreateAsync(
            new LicensePlateCreateInput(
                session.WarehouseId,
                locationId,
                resolved.LicensePlateNumber,
                resolved.LicensePlateNumber.StartsWith("00", StringComparison.Ordinal),
                LicensePlateType.Pallet,
                SourceReference: session.SessionReference),
            userId,
            cancellationToken);
        return created.IsFailure
            ? created.ToFailure<int?>()
            : Result.Success<int?>(created.Value.Id);
    }

    private async Task<Result> RequireOverrideAsync(
        int warehouseId,
        bool required,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (!required)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(WmsErrors.Validation(
                "receiving.supervisor_reason_required",
                "A supervisor override reason is required."));
        }

        return await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReceivingOverride,
            warehouseId,
            cancellationToken);
    }

    private async Task<ReceivingSession?> LoadSessionAsync(
        int sessionId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var query = context.ReceivingSessions
            .Include(session => session.Lines)
            .Include(session => session.Scans)
                .ThenInclude(scan => scan.SessionLine)
            .Include(session => session.ReceivingLocation)
            .AsQueryable();
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);
    }

    private static string NormalizeSessionReference(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? $"RCV-{Guid.NewGuid():N}".ToUpperInvariant()
            : new string(value.Trim().ToUpperInvariant().Take(100).ToArray());

    private static ReceivingSessionDto Map(ReceivingSession session) =>
        new(
            session.Id,
            session.WarehouseId,
            session.ReceivingLocationId,
            session.SourceType,
            session.SessionReference,
            session.PurchaseOrderId,
            session.AdvanceShippingNoticeId,
            session.DockLocationId,
            session.ExternalReference,
            session.Notes,
            session.Status,
            session.CreatedByUserId,
            session.SupervisorOverride,
            session.SupervisorOverrideReason,
            session.OpenedAtUtc,
            session.PausedAtUtc,
            session.CompletedAtUtc,
            session.CancelledAtUtc,
            session.CancelledByUserId,
            session.CancellationReason,
            session.LastActivityAtUtc,
            session.Revision,
            session.Lines.OrderBy(line => line.LineNumber).Select(Map).ToArray(),
            session.Scans.OrderBy(scan => scan.Id).Select(Map).ToArray(),
            session.CanScan,
            session.HasOpenDemand);

    private static ReceivingSessionLineDto Map(ReceivingSessionLine line) =>
        new(
            line.Id,
            line.LineNumber,
            line.ItemId,
            line.ItemSkuSnapshot,
            line.ItemNameSnapshot,
            line.BaseUnitOfMeasure,
            line.ExpectedBaseQuantity,
            line.PreviouslyReceivedBaseQuantity,
            line.ReceivedBaseQuantity,
            line.RemainingBaseQuantity,
            line.MaximumReceivableBaseQuantity,
            line.OverDeliveryTolerancePercent,
            line.UnderDeliveryTolerancePercent,
            line.PurchaseOrderLineId,
            line.AdvanceShippingNoticeLineId,
            line.ExpectedLotNumber,
            line.ExpectedExpiryDate,
            line.ExpectedSerialNumber,
            line.ExpectedLicensePlateNumber,
            line.IsFullyReceived,
            line.Revision);

    private static ReceivingScanDto Map(ReceivingSessionScan scan) =>
        new(
            scan.Id,
            scan.ReceivingSessionLineId,
            scan.ClientOperationId,
            scan.RawScanValue,
            scan.ItemSku,
            scan.EnteredQuantity,
            scan.BaseQuantity,
            scan.UnitOfMeasure,
            scan.PackagingCode,
            scan.LotNumber,
            scan.ExpiryDate,
            scan.SerialNumber,
            scan.LicensePlateId,
            scan.DestinationLocationCode,
            scan.Status,
            scan.MovementId,
            scan.ReceiptId,
            scan.ReceiptDocumentNumber,
            scan.IdempotencyKey,
            scan.ErrorCode,
            scan.ErrorMessage,
            scan.RequestedAtUtc,
            scan.CompletedAtUtc,
            scan.CorrectedAtUtc,
            scan.CorrectionReason,
            scan.Revision);

    private sealed record ResolvedReceivingScan(
        string ItemSku,
        decimal Quantity,
        string? UnitOfMeasure,
        string? PackagingCode,
        string? LotNumber,
        DateTime? ExpiryDate,
        DateTime? ManufacturedDate,
        string? SerialNumber,
        string? LicensePlateNumber,
        int? LicensePlateId,
        string DestinationLocationCode,
        string? ReferenceNumber,
        string? Notes)
    {
        public bool CreateLicensePlate { get; init; }
    }
}
