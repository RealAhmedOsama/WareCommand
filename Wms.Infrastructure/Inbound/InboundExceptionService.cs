using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Inbound;

public sealed class InboundExceptionService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<InboundExceptionService> logger) : IInboundExceptionService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<InboundExceptionPageDto>> ListAsync(
        InboundExceptionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InboundExceptionPageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var exceptions = context.InboundExceptions.AsNoTracking().AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            exceptions = exceptions.Where(exception => scope.WarehouseIds.Contains(exception.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Code.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.Code == query.Code.Value);
        }

        if (query.Severity.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.Severity == query.Severity.Value);
        }

        if (query.Status.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.Status == query.Status.Value);
        }
        else if (!query.IncludeClosed)
        {
            exceptions = exceptions.Where(exception =>
                exception.Status != InboundExceptionStatus.Resolved &&
                exception.Status != InboundExceptionStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(query.QueueCode))
        {
            var queueCode = query.QueueCode.Trim().ToUpperInvariant();
            exceptions = exceptions.Where(exception => exception.QueueCode == queueCode);
        }

        if (!string.IsNullOrWhiteSpace(query.OwnerUserId))
        {
            var owner = query.OwnerUserId.Trim();
            exceptions = exceptions.Where(exception => exception.OwnerUserId == owner);
        }

        if (query.ReceiptId.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.ReceiptId == query.ReceiptId.Value);
        }

        if (query.ReceivingSessionId.HasValue)
        {
            exceptions = exceptions.Where(exception => exception.ReceivingSessionId == query.ReceivingSessionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            exceptions = exceptions.Where(exception =>
                exception.ExceptionNumber.Contains(term) ||
                exception.Reason.Contains(term) ||
                (exception.ItemSkuSnapshot ?? string.Empty).Contains(term) ||
                exception.IdempotencyKey.Contains(term));
        }

        var asOfUtc = NormalizeUtc(query.AsOfUtc ?? clock.UtcNow.UtcDateTime);
        if (query.OverdueOnly)
        {
            exceptions = exceptions.Where(exception =>
                exception.DueAtUtc.HasValue &&
                exception.DueAtUtc.Value < asOfUtc &&
                exception.Status != InboundExceptionStatus.Resolved &&
                exception.Status != InboundExceptionStatus.Cancelled);
        }

        var totalCount = await exceptions.CountAsync(cancellationToken);
        var openCount = await exceptions.CountAsync(
            exception => exception.Status != InboundExceptionStatus.Resolved &&
                        exception.Status != InboundExceptionStatus.Cancelled,
            cancellationToken);
        var overdueCount = await exceptions.CountAsync(
            exception => exception.DueAtUtc.HasValue &&
                        exception.DueAtUtc.Value < asOfUtc &&
                        exception.Status != InboundExceptionStatus.Resolved &&
                        exception.Status != InboundExceptionStatus.Cancelled,
            cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var rows = await exceptions
            .OrderByDescending(exception => exception.Severity)
            .ThenBy(exception => exception.DueAtUtc ?? DateTime.MaxValue)
            .ThenBy(exception => exception.CreatedAt)
            .ThenBy(exception => exception.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new InboundExceptionPageDto(
            rows.Select(exception => Map(exception, asOfUtc)).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            openCount,
            overdueCount));
    }

    public async Task<Result<InboundExceptionDto>> GetAsync(
        int exceptionId,
        CancellationToken cancellationToken = default)
    {
        var exception = await context.InboundExceptions.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.NotFound(
                "inbound_exception.not_found",
                "The inbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesRead,
            exception.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<InboundExceptionDto>()
            : Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
    }

    public async Task<Result<InboundExceptionDto>> CreateAsync(
        InboundExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InboundExceptionDto>();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                "inbound_exception.actor_required",
                "An authenticated actor is required."));
        }

        var normalizedKey = input.IdempotencyKey?.Trim() ?? string.Empty;
        var existing = await context.InboundExceptions.AsNoTracking().SingleOrDefaultAsync(
            exception => exception.WarehouseId == input.WarehouseId &&
                         exception.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Code == input.Code && existing.ReceiptId == input.ReceiptId &&
                   existing.ReceiptLineId == input.ReceiptLineId
                ? Result.Success(Map(existing, NormalizeUtc(clock.UtcNow.UtcDateTime)))
                : Result.Failure<InboundExceptionDto>(WmsErrors.Conflict(
                    "inbound_exception.idempotency_conflict",
                    "The idempotency key was already used for a different inbound exception."));
        }

        IDbContextTransaction? transaction = null;
        try
        {
            var references = await LoadReferencesAsync(input, cancellationToken);
            if (references.IsFailure)
            {
                return references.ToFailure<InboundExceptionDto>();
            }

            var stockSafety = await EnsureExistingReceiptStockIsNotAvailableAsync(
                references.Value.ReceiptLine,
                cancellationToken);
            if (stockSafety.IsFailure)
            {
                return stockSafety.ToFailure<InboundExceptionDto>();
            }

            if (context.Database.IsRelational())
            {
                transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            }

            var now = NormalizeUtc(clock.UtcNow.UtcDateTime);
            var exception = new InboundException(
                input.WarehouseId,
                $"INB-EX-{input.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}",
                normalizedKey,
                input.Code,
                input.Severity,
                input.Reason,
                input.QueueCode,
                input.DueAtUtc,
                input.AdvanceShippingNoticeId,
                input.AdvanceShippingNoticeLineId,
                input.ReceiptId,
                input.ReceiptLineId,
                input.ReceivingSessionId,
                input.LicensePlateId,
                input.WarehouseWorkId,
                input.ItemId,
                input.StagingLocationId,
                input.ExpectedBaseQuantity,
                input.ActualBaseQuantity,
                input.VarianceBaseQuantity,
                input.ItemSkuSnapshot,
                input.Notes,
                input.AttachmentReferences,
                userId);
            context.InboundExceptions.Add(exception);

            if (references.Value.Receipt is not null && references.Value.Receipt.Status != ReceiptStatus.Exception)
            {
                references.Value.Receipt.MarkException();
            }

            if (references.Value.AdvanceShippingNotice is not null &&
                references.Value.AdvanceShippingNotice.Status != AdvanceShippingNoticeStatus.Exception)
            {
                references.Value.AdvanceShippingNotice.MarkException();
            }

            if (references.Value.WarehouseWork is not null && !references.Value.WarehouseWork.IsTerminal &&
                references.Value.WarehouseWork.Status != WarehouseWorkStatus.Exception)
            {
                references.Value.WarehouseWork.RecordException(
                    ToWorkExceptionType(input.Code),
                    input.Reason,
                    userId,
                    now);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InboundExceptionCreated,
                    WmsAuditEntityTypes.InboundException,
                    exception.ExceptionNumber,
                    input.WarehouseId,
                    After: Snapshot(exception),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success(Map(exception, now));
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(transaction);
            throw;
        }
        catch (ArgumentException exception)
        {
            await RollbackAsync(transaction);
            return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                "inbound_exception.invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            await RollbackAsync(transaction);
            return Result.Failure<InboundExceptionDto>(WmsErrors.BusinessRule(
                "inbound_exception.blocked",
                exception.Message));
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction);
            logger.LogError(exception, "Inbound exception creation failed for warehouse {WarehouseId}", input.WarehouseId);
            return Result.Failure<InboundExceptionDto>(WmsErrors.FromException(
                exception,
                "inbound_exception.create_failed",
                "The inbound exception could not be recorded."));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<Result<InboundExceptionDto>> AssignAsync(
        int exceptionId,
        InboundExceptionAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var exception = await context.InboundExceptions.SingleOrDefaultAsync(
            candidate => candidate.Id == exceptionId,
            cancellationToken);
        if (exception is null)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.NotFound(
                "inbound_exception.not_found",
                "The inbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            exception.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InboundExceptionDto>();
        }

        try
        {
            var before = Snapshot(exception);
            exception.Assign(input.OwnerUserId, input.TeamCode, userId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InboundExceptionAssigned,
                    WmsAuditEntityTypes.InboundException,
                    exception.ExceptionNumber,
                    exception.WarehouseId,
                    Before: before,
                    After: Snapshot(exception),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
        }
        catch (ArgumentException exceptionError)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                "inbound_exception.assignment_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.BusinessRule(
                "inbound_exception.assignment_blocked",
                exceptionError.Message));
        }
        catch (Exception exceptionError)
        {
            logger.LogError(exceptionError, "Inbound exception assignment failed for {ExceptionId}", exceptionId);
            return Result.Failure<InboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                "inbound_exception.assignment_failed",
                "The inbound exception assignment could not be saved."));
        }
    }

    public async Task<Result<InboundExceptionDto>> StartReviewAsync(
        int exceptionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var exception = await context.InboundExceptions.SingleOrDefaultAsync(
            candidate => candidate.Id == exceptionId,
            cancellationToken);
        if (exception is null)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.NotFound(
                "inbound_exception.not_found",
                "The inbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            exception.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InboundExceptionDto>();
        }

        try
        {
            var before = Snapshot(exception);
            exception.StartReview(userId, clock.UtcNow.UtcDateTime);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InboundExceptionReviewStarted,
                    WmsAuditEntityTypes.InboundException,
                    exception.ExceptionNumber,
                    exception.WarehouseId,
                    Before: before,
                    After: Snapshot(exception),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
        }
        catch (ArgumentException exceptionError)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                "inbound_exception.review_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.BusinessRule(
                "inbound_exception.review_blocked",
                exceptionError.Message));
        }
        catch (Exception exceptionError)
        {
            logger.LogError(exceptionError, "Inbound exception review failed for {ExceptionId}", exceptionId);
            return Result.Failure<InboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                "inbound_exception.review_failed",
                "The inbound exception review could not be recorded."));
        }
    }

    public async Task<Result<InboundExceptionDto>> ResolveAsync(
        int exceptionId,
        InboundExceptionResolutionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var exception = await context.InboundExceptions.SingleOrDefaultAsync(
            candidate => candidate.Id == exceptionId,
            cancellationToken);
        if (exception is null)
        {
            return Result.Failure<InboundExceptionDto>(WmsErrors.NotFound(
                "inbound_exception.not_found",
                "The inbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AdvanceShippingNoticesManage,
            exception.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<InboundExceptionDto>();
        }

        if (RequiresSupervisor(input.Resolution) || input.SupervisorOverride)
        {
            if (!input.SupervisorOverride || string.IsNullOrWhiteSpace(input.OverrideReason))
            {
                return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                    "inbound_exception.supervisor_reason_required",
                    "This resolution requires a supervisor override and an explicit reason."));
            }

            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ReceivingOverride,
                exception.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<InboundExceptionDto>();
            }
        }

        IDbContextTransaction? transaction = null;
        try
        {
            var receipt = exception.ReceiptId.HasValue
                ? await context.Receipts.SingleOrDefaultAsync(
                    candidate => candidate.Id == exception.ReceiptId.Value,
                    cancellationToken)
                : null;
            var notice = exception.AdvanceShippingNoticeId.HasValue
                ? await context.AdvanceShippingNotices.SingleOrDefaultAsync(
                    candidate => candidate.Id == exception.AdvanceShippingNoticeId.Value,
                    cancellationToken)
                : null;
            var work = exception.WarehouseWorkId.HasValue
                ? await context.WarehouseWorks.SingleOrDefaultAsync(
                    candidate => candidate.Id == exception.WarehouseWorkId.Value,
                    cancellationToken)
                : null;

            if (context.Database.IsRelational())
            {
                transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            }

            var before = Snapshot(exception);
            var now = NormalizeUtc(clock.UtcNow.UtcDateTime);
            if (input.Resolution == InboundExceptionResolution.SupervisorReview)
            {
                exception.StartReview(userId, now);
            }

            if (input.Resolution == InboundExceptionResolution.Cancel)
            {
                if (receipt?.Status == ReceiptStatus.Exception)
                {
                    receipt.Cancel(userId, now);
                }

                if (notice?.Status == AdvanceShippingNoticeStatus.Exception)
                {
                    notice.Cancel(userId, now);
                }

                if (work is not null && !work.IsTerminal)
                {
                    work.Cancel(userId, input.Reason, now);
                }
            }
            else if (input.Resolution is InboundExceptionResolution.CorrectSourceData or
                     InboundExceptionResolution.BlindReceiptApproval or
                     InboundExceptionResolution.RepackRelabel)
            {
                if (receipt?.Status == ReceiptStatus.Exception)
                {
                    receipt.ResumeException(userId, now);
                }

                if (notice?.Status == AdvanceShippingNoticeStatus.Exception)
                {
                    notice.ResumeException(now);
                }

                if (work?.Status == WarehouseWorkStatus.Exception)
                {
                    work.ResolveException(userId, input.Reason, now);
                }
            }
            else if (input.Resolution is InboundExceptionResolution.ReturnToVendor or
                     InboundExceptionResolution.Quarantine)
            {
                if (work is not null && !work.IsTerminal)
                {
                    work.Cancel(userId, input.Reason, now);
                }
            }

            exception.ApplyResolution(input.Resolution, input.Reason, userId, now);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.InboundExceptionResolved,
                    WmsAuditEntityTypes.InboundException,
                    exception.ExceptionNumber,
                    exception.WarehouseId,
                    Before: before,
                    After: new Dictionary<string, object?>(Snapshot(exception))
                    {
                        ["supervisorOverride"] = input.SupervisorOverride,
                        ["overrideReason"] = input.OverrideReason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return Result.Success(Map(exception, now));
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(transaction);
            throw;
        }
        catch (ArgumentException exceptionError)
        {
            await RollbackAsync(transaction);
            return Result.Failure<InboundExceptionDto>(WmsErrors.Validation(
                "inbound_exception.resolution_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            await RollbackAsync(transaction);
            return Result.Failure<InboundExceptionDto>(WmsErrors.BusinessRule(
                "inbound_exception.resolution_blocked",
                exceptionError.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollbackAsync(transaction);
            return Result.Failure<InboundExceptionDto>(WmsErrors.Concurrency(
                "inbound_exception.concurrency",
                "The inbound exception changed while it was being resolved. Reload and try again."));
        }
        catch (Exception exceptionError)
        {
            await RollbackAsync(transaction);
            logger.LogError(exceptionError, "Inbound exception resolution failed for {ExceptionId}", exceptionId);
            return Result.Failure<InboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                "inbound_exception.resolution_failed",
                "The inbound exception resolution could not be saved."));
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<Result<InboundExceptionReferences>> LoadReferencesAsync(
        InboundExceptionInput input,
        CancellationToken cancellationToken)
    {
        if (input.AdvanceShippingNoticeLineId.HasValue && !input.AdvanceShippingNoticeId.HasValue)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.Validation(
                "inbound_exception.asn_required",
                "An ASN is required when an ASN line is supplied."));
        }

        if (input.ReceiptLineId.HasValue && !input.ReceiptId.HasValue)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.Validation(
                "inbound_exception.receipt_required",
                "A receipt is required when a receipt line is supplied."));
        }

        var notice = input.AdvanceShippingNoticeId.HasValue
            ? await context.AdvanceShippingNotices
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.Id == input.AdvanceShippingNoticeId.Value, cancellationToken)
            : null;
        var noticeLine = input.AdvanceShippingNoticeLineId.HasValue
            ? notice?.Lines.SingleOrDefault(line => line.Id == input.AdvanceShippingNoticeLineId.Value)
            : null;
        var receipt = input.ReceiptId.HasValue
            ? await context.Receipts
                .Include(candidate => candidate.Lines)
                .SingleOrDefaultAsync(candidate => candidate.Id == input.ReceiptId.Value, cancellationToken)
            : null;
        var receiptLine = input.ReceiptLineId.HasValue
            ? receipt?.Lines.SingleOrDefault(line => line.Id == input.ReceiptLineId.Value)
            : null;
        var session = input.ReceivingSessionId.HasValue
            ? await context.ReceivingSessions.SingleOrDefaultAsync(
                candidate => candidate.Id == input.ReceivingSessionId.Value,
                cancellationToken)
            : null;
        var licensePlate = input.LicensePlateId.HasValue
            ? await context.LicensePlates.SingleOrDefaultAsync(
                candidate => candidate.Id == input.LicensePlateId.Value,
                cancellationToken)
            : null;
        var work = input.WarehouseWorkId.HasValue
            ? await context.WarehouseWorks.SingleOrDefaultAsync(
                candidate => candidate.Id == input.WarehouseWorkId.Value,
                cancellationToken)
            : null;
        var item = input.ItemId.HasValue
            ? await context.Items.SingleOrDefaultAsync(candidate => candidate.Id == input.ItemId.Value, cancellationToken)
            : null;
        var staging = input.StagingLocationId.HasValue
            ? await context.Locations.SingleOrDefaultAsync(
                candidate => candidate.Id == input.StagingLocationId.Value,
                cancellationToken)
            : null;

        if (input.AdvanceShippingNoticeId.HasValue && notice is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.asn_not_found",
                "The linked ASN was not found."));
        }

        if (input.AdvanceShippingNoticeLineId.HasValue && noticeLine is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.asn_line_not_found",
                "The linked ASN line was not found."));
        }

        if (input.ReceiptId.HasValue && receipt is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.receipt_not_found",
                "The linked receipt was not found."));
        }

        if (input.ReceiptLineId.HasValue && receiptLine is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.receipt_line_not_found",
                "The linked receipt line was not found."));
        }

        if (input.ReceivingSessionId.HasValue && session is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.session_not_found",
                "The linked receiving session was not found."));
        }

        if (input.LicensePlateId.HasValue && licensePlate is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.license_plate_not_found",
                "The linked license plate was not found."));
        }

        if (input.WarehouseWorkId.HasValue && work is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.work_not_found",
                "The linked warehouse work was not found."));
        }

        if (input.ItemId.HasValue && item is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.item_not_found",
                "The linked item was not found."));
        }

        if (input.StagingLocationId.HasValue && staging is null)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.NotFound(
                "inbound_exception.staging_not_found",
                "The linked staging location was not found."));
        }

        var warehouseIds = new[]
        {
            notice?.WarehouseId,
            receipt?.WarehouseId,
            session?.WarehouseId,
            licensePlate?.WarehouseId,
            work?.WarehouseId,
            staging?.WarehouseId,
            item is null ? null : input.WarehouseId
        }.Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        if (warehouseIds.Any(warehouseId => warehouseId != input.WarehouseId))
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.Validation(
                "inbound_exception.warehouse_mismatch",
                "All linked inbound exception records must belong to the selected warehouse."));
        }

        if (staging is not null && (staging.Type != LocationType.Staging || !staging.IsActive))
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.Validation(
                "inbound_exception.staging_invalid",
                "The selected staging location must be an active staging location."));
        }

        var referencedItemId = receiptLine?.ItemId ?? noticeLine?.ItemId;
        if (input.ItemId.HasValue && referencedItemId.HasValue && input.ItemId.Value != referencedItemId.Value)
        {
            return Result.Failure<InboundExceptionReferences>(WmsErrors.Validation(
                "inbound_exception.item_mismatch",
                "The exception item does not match the linked receipt or ASN line."));
        }

        return Result.Success(new InboundExceptionReferences(
            notice,
            noticeLine,
            receipt,
            receiptLine,
            session,
            licensePlate,
            work,
            item,
            staging));
    }

    private async Task<Result> EnsureExistingReceiptStockIsNotAvailableAsync(
        ReceiptLine? receiptLine,
        CancellationToken cancellationToken)
    {
        if (receiptLine is null)
        {
            return Result.Success();
        }

        var movements = await context.Movements.AsNoTracking()
            .Where(movement => movement.ReceiptLineId == receiptLine.Id && movement.Type == MovementType.Receipt)
            .Select(movement => new StockIdentity(
                movement.ItemId,
                movement.ToLocationId,
                movement.LotId,
                movement.SerialNumberId,
                movement.SerialNumber,
                movement.LicensePlateId))
            .ToListAsync(cancellationToken);

        foreach (var movement in movements)
        {
            if (!movement.LocationId.HasValue)
            {
                continue;
            }

            var matchingStock = await context.Stock.AsNoTracking().Where(
                stock => stock.ItemId == movement.ItemId &&
                         stock.LocationId == movement.LocationId.Value &&
                         stock.InventoryStatusId == InventoryStatusSystemIds.Available)
                .ToListAsync(cancellationToken);
            if (matchingStock.Any(stock =>
                    stock.QuantityAvailable.Value > 0m &&
                    stock.LotId == movement.LotId &&
                    stock.SerialNumberId == movement.SerialNumberId &&
                    string.Equals(stock.SerialNumber, movement.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
                    stock.LicensePlateId == movement.LicensePlateId))
            {
                return Result.Failure(WmsErrors.BusinessRule(
                    "inbound_exception.stock_must_be_held",
                    "Move received stock to HOLD, QUARANTINE, DAMAGED, or another non-available status before recording an inbound exception."));
            }
        }

        return Result.Success();
    }

    private static bool RequiresSupervisor(InboundExceptionResolution resolution) =>
        resolution is InboundExceptionResolution.SupervisorReview or
            InboundExceptionResolution.BlindReceiptApproval or
            InboundExceptionResolution.ReturnToVendor or
            InboundExceptionResolution.Quarantine or
            InboundExceptionResolution.RepackRelabel or
            InboundExceptionResolution.Cancel;

    private static WarehouseWorkExceptionType ToWorkExceptionType(InboundExceptionCode code) =>
        code switch
        {
            InboundExceptionCode.DamagedPackage => WarehouseWorkExceptionType.Damaged,
            InboundExceptionCode.UnknownItem or InboundExceptionCode.InvalidLot or
                InboundExceptionCode.InvalidExpiry or InboundExceptionCode.InvalidSerial =>
                WarehouseWorkExceptionType.NotFound,
            InboundExceptionCode.CapacityNoLocation => WarehouseWorkExceptionType.Blocked,
            _ => WarehouseWorkExceptionType.Other
        };

    private static Dictionary<string, object?> Snapshot(InboundException exception) =>
        new(StringComparer.Ordinal)
        {
            ["exceptionNumber"] = exception.ExceptionNumber,
            ["code"] = exception.Code.ToString(),
            ["severity"] = exception.Severity.ToString(),
            ["status"] = exception.Status.ToString(),
            ["queueCode"] = exception.QueueCode,
            ["ownerUserId"] = exception.OwnerUserId,
            ["assignedTeamCode"] = exception.AssignedTeamCode,
            ["resolution"] = exception.Resolution?.ToString(),
            ["receiptId"] = exception.ReceiptId,
            ["receiptLineId"] = exception.ReceiptLineId,
            ["advanceShippingNoticeId"] = exception.AdvanceShippingNoticeId,
            ["advanceShippingNoticeLineId"] = exception.AdvanceShippingNoticeLineId,
            ["receivingSessionId"] = exception.ReceivingSessionId,
            ["licensePlateId"] = exception.LicensePlateId,
            ["stagingLocationId"] = exception.StagingLocationId
        };

    private static InboundExceptionDto Map(InboundException exception, DateTime asOfUtc) =>
        new(
            exception.Id,
            exception.WarehouseId,
            exception.ExceptionNumber,
            exception.IdempotencyKey,
            exception.Code,
            exception.Severity,
            exception.Status,
            exception.Reason,
            exception.QueueCode,
            exception.DueAtUtc,
            exception.AdvanceShippingNoticeId,
            exception.AdvanceShippingNoticeLineId,
            exception.ReceiptId,
            exception.ReceiptLineId,
            exception.ReceivingSessionId,
            exception.LicensePlateId,
            exception.WarehouseWorkId,
            exception.ItemId,
            exception.StagingLocationId,
            exception.ExpectedBaseQuantity,
            exception.ActualBaseQuantity,
            exception.VarianceBaseQuantity,
            exception.ItemSkuSnapshot,
            exception.Notes,
            exception.AttachmentReferences,
            exception.CreatedByUserId,
            exception.OwnerUserId,
            exception.AssignedTeamCode,
            exception.AssignedByUserId,
            exception.AssignedAtUtc,
            exception.ReviewStartedByUserId,
            exception.ReviewStartedAtUtc,
            exception.Resolution,
            exception.ResolutionReason,
            exception.ResolvedByUserId,
            exception.ResolvedAtUtc,
            exception.CreatedAt,
            exception.UpdatedAt,
            exception.Revision,
            exception.IsTerminal,
            exception.IsOverdue(asOfUtc));

    private static DateTime NormalizeUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original failure; the request is already being rejected.
            }
        }
    }

    private sealed record InboundExceptionReferences(
        AdvanceShippingNotice? AdvanceShippingNotice,
        AdvanceShippingNoticeLine? AdvanceShippingNoticeLine,
        Receipt? Receipt,
        ReceiptLine? ReceiptLine,
        ReceivingSession? ReceivingSession,
        LicensePlate? LicensePlate,
        WarehouseWorkEntity? WarehouseWork,
        Item? Item,
        Location? StagingLocation);

    private sealed record StockIdentity(
        int ItemId,
        int? LocationId,
        int? LotId,
        int? SerialNumberId,
        string? SerialNumber,
        int? LicensePlateId);
}
