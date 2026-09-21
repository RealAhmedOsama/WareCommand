using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Outbound;

public sealed class OutboundExceptionService(
    WmsDbContext context,
    IUnitOfWork unitOfWork,
    IWarehouseAccessService warehouseAccessService,
    IInventoryReservationService inventoryReservationService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<OutboundExceptionService> logger) : IOutboundExceptionService
{
    private const int MaximumPageSize = 200;

    public async Task<Result<OutboundExceptionPageDto>> ListAsync(
        OutboundExceptionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<OutboundExceptionPageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var exceptions = context.OutboundExceptions.AsNoTracking().AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            exceptions = exceptions.Where(value => scope.WarehouseIds.Contains(value.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            exceptions = exceptions.Where(value => value.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Code.HasValue)
        {
            exceptions = exceptions.Where(value => value.Code == query.Code.Value);
        }

        if (query.Severity.HasValue)
        {
            exceptions = exceptions.Where(value => value.Severity == query.Severity.Value);
        }

        if (query.Status.HasValue)
        {
            exceptions = exceptions.Where(value => value.Status == query.Status.Value);
        }
        else if (!query.IncludeClosed)
        {
            exceptions = exceptions.Where(value =>
                value.Status != OutboundExceptionStatus.Resolved &&
                value.Status != OutboundExceptionStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(query.QueueCode))
        {
            var queueCode = query.QueueCode.Trim().ToUpperInvariant();
            exceptions = exceptions.Where(value => value.QueueCode == queueCode);
        }

        if (!string.IsNullOrWhiteSpace(query.OwnerUserId))
        {
            var owner = query.OwnerUserId.Trim();
            exceptions = exceptions.Where(value => value.OwnerUserId == owner);
        }

        if (query.SalesOrderId.HasValue)
        {
            exceptions = exceptions.Where(value => value.SalesOrderId == query.SalesOrderId.Value);
        }

        if (query.ShipmentId.HasValue)
        {
            exceptions = exceptions.Where(value => value.ShipmentId == query.ShipmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            exceptions = exceptions.Where(value =>
                value.ExceptionNumber.Contains(term) ||
                value.Reason.Contains(term) ||
                (value.ItemSkuSnapshot ?? string.Empty).Contains(term) ||
                value.IdempotencyKey.Contains(term));
        }

        var asOfUtc = NormalizeUtc(query.AsOfUtc ?? clock.UtcNow.UtcDateTime);
        if (query.OverdueOnly)
        {
            exceptions = exceptions.Where(value =>
                value.DueAtUtc.HasValue &&
                value.DueAtUtc.Value < asOfUtc &&
                value.Status != OutboundExceptionStatus.Resolved &&
                value.Status != OutboundExceptionStatus.Cancelled);
        }

        var totalCount = await exceptions.CountAsync(cancellationToken);
        var openCount = await exceptions.CountAsync(
            value => value.Status != OutboundExceptionStatus.Resolved &&
                     value.Status != OutboundExceptionStatus.Cancelled,
            cancellationToken);
        var overdueCount = await exceptions.CountAsync(
            value => value.DueAtUtc.HasValue &&
                     value.DueAtUtc.Value < asOfUtc &&
                     value.Status != OutboundExceptionStatus.Resolved &&
                     value.Status != OutboundExceptionStatus.Cancelled,
            cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var rows = await exceptions
            .OrderByDescending(value => value.Severity)
            .ThenBy(value => value.DueAtUtc ?? DateTime.MaxValue)
            .ThenBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new OutboundExceptionPageDto(
            rows.Select(value => Map(value, asOfUtc)).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            openCount,
            overdueCount));
    }

    public async Task<Result<OutboundExceptionDto>> GetAsync(
        int exceptionId,
        CancellationToken cancellationToken = default)
    {
        var exception = await context.OutboundExceptions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.NotFound(
                "outbound_exception.not_found",
                "The outbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersRead,
            exception.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<OutboundExceptionDto>()
            : Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
    }

    public async Task<Result<OutboundExceptionDto>> CreateAsync(
        OutboundExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<OutboundExceptionDto>();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                "outbound_exception.actor_required",
                "An authenticated actor is required."));
        }

        var normalizedKey = input.IdempotencyKey?.Trim() ?? string.Empty;
        var existing = await context.OutboundExceptions.AsNoTracking().SingleOrDefaultAsync(
            value => value.WarehouseId == input.WarehouseId && value.IdempotencyKey == normalizedKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Code == input.Code &&
                   existing.SalesOrderId == input.SalesOrderId &&
                   existing.SalesOrderLineId == input.SalesOrderLineId
                ? Result.Success(Map(existing, NormalizeUtc(clock.UtcNow.UtcDateTime)))
                : Result.Failure<OutboundExceptionDto>(WmsErrors.Conflict(
                    "outbound_exception.idempotency_conflict",
                    "The idempotency key was already used for a different outbound exception."));
        }

        var transactionStarted = true;
        try
        {
            var references = await LoadReferencesAsync(input, cancellationToken);
            var exception = new OutboundException(
                input.WarehouseId,
                $"OUT-EX-{input.WarehouseId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}",
                normalizedKey,
                input.Code,
                input.Severity,
                input.Reason,
                input.QueueCode,
                input.DueAtUtc,
                input.SalesOrderId,
                input.SalesOrderLineId,
                input.InventoryReservationId,
                input.WarehouseWorkId,
                input.WarehouseWorkLineId,
                input.ShipmentId,
                input.ShipmentPackageId,
                input.ShipmentLoadId,
                input.ItemId,
                input.LocationId,
                input.LotId,
                input.SerialNumberId,
                input.LicensePlateId,
                input.ExpectedBaseQuantity,
                input.ActualBaseQuantity,
                input.VarianceBaseQuantity,
                input.ItemSkuSnapshot,
                input.Notes,
                input.AttachmentReferences,
                userId);
            context.OutboundExceptions.Add(exception);

            if (references.Work is not null && !references.Work.IsTerminal &&
                references.Work.Status != WarehouseWorkStatus.Exception)
            {
                references.Work.RecordException(
                    ToWorkExceptionType(input.Code),
                    input.Reason,
                    userId,
                    clock.UtcNow.UtcDateTime);
            }

            if (references.Order is not null &&
                references.Order.Status is not (SalesOrderStatus.Draft or
                    SalesOrderStatus.Cancelled or
                    SalesOrderStatus.Closed or
                    SalesOrderStatus.Held or
                    SalesOrderStatus.Exception))
            {
                references.Order.SetAllocationStatus(SalesOrderStatus.Exception);
            }

            await unitOfWork.BeginTransactionAsync(cancellationToken);
            await auditWriter.RecordAsync(
                OutboundAudit(
                    exception,
                    WmsAuditActions.OutboundExceptionCreated,
                    userId,
                    new Dictionary<string, object?>
                    {
                        ["code"] = input.Code.ToString(),
                        ["severity"] = input.Severity.ToString(),
                        ["salesOrderId"] = input.SalesOrderId,
                        ["warehouseWorkId"] = input.WarehouseWorkId
                    }),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exceptionError)
        {
            await RollbackAsync(transactionStarted);
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                "outbound_exception.create_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            await RollbackAsync(transactionStarted);
            return Result.Failure<OutboundExceptionDto>(WmsErrors.BusinessRule(
                "outbound_exception.create_blocked",
                exceptionError.Message));
        }
        catch (Exception exceptionError)
        {
            await RollbackAsync(transactionStarted);
            logger.LogError(exceptionError, "Outbound exception creation failed");
            return Result.Failure<OutboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                "outbound_exception.create_failed",
                "The outbound exception could not be created."));
        }
    }

    public Task<Result<OutboundExceptionDto>> AssignAsync(
        int exceptionId,
        OutboundExceptionAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteSimpleMutationAsync(
            exceptionId,
            WmsAuditActions.OutboundExceptionAssigned,
            "assignment",
            userId,
            exception => exception.Assign(input.OwnerUserId, input.TeamCode, userId, clock.UtcNow.UtcDateTime),
            cancellationToken);

    public Task<Result<OutboundExceptionDto>> StartReviewAsync(
        int exceptionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        ExecuteSimpleMutationAsync(
            exceptionId,
            WmsAuditActions.OutboundExceptionReviewStarted,
            "review",
            userId,
            exception => exception.StartReview(userId, clock.UtcNow.UtcDateTime),
            cancellationToken);

    public async Task<Result<OutboundExceptionDto>> ResolveAsync(
        int exceptionId,
        string idempotencyKey,
        OutboundExceptionResolutionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var exception = await context.OutboundExceptions.SingleOrDefaultAsync(
            value => value.Id == exceptionId,
            cancellationToken);
        if (exception is null)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.NotFound(
                "outbound_exception.not_found",
                "The outbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersManage,
            exception.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<OutboundExceptionDto>();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                "outbound_exception.idempotency_required",
                "Every outbound exception resolution requires an idempotency key."));
        }

        var requestHash = Hash(input);
        var replay = await context.OutboundExceptionCommands.SingleOrDefaultAsync(
            command => command.OutboundExceptionId == exceptionId &&
                       command.Operation == "resolve" &&
                       command.IdempotencyKey == idempotencyKey.Trim(),
            cancellationToken);
        if (replay is not null)
        {
            return replay.RequestHash == requestHash
                ? Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)))
                : Result.Failure<OutboundExceptionDto>(WmsErrors.Conflict(
                    "outbound_exception.idempotency_conflict",
                    "The idempotency key was already used for a different resolution."));
        }

        var supervisorRequired = RequiresSupervisor(input.Resolution) || input.SupervisorOverride;
        if (supervisorRequired)
        {
            if (!input.SupervisorOverride || string.IsNullOrWhiteSpace(input.OverrideReason))
            {
                return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                    "outbound_exception.supervisor_reason_required",
                    "This resolution requires a supervisor override and an explicit reason."));
            }

            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.WorkOverride,
                exception.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<OutboundExceptionDto>();
            }
        }

        var transactionStarted = true;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            var current = await context.OutboundExceptions.SingleAsync(
                value => value.Id == exceptionId,
                cancellationToken);
            await ApplyResolutionAsync(current, input, userId, cancellationToken);
            current.ApplyResolution(
                input.Resolution,
                input.Reason,
                BuildResolutionDetails(input),
                userId,
                clock.UtcNow.UtcDateTime);
            context.OutboundExceptionCommands.Add(new OutboundExceptionCommand(
                current.Id,
                "resolve",
                idempotencyKey,
                requestHash,
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                OutboundAudit(
                    current,
                    WmsAuditActions.OutboundExceptionResolved,
                    userId,
                    new Dictionary<string, object?>
                    {
                        ["resolution"] = input.Resolution.ToString(),
                        ["quantity"] = input.Quantity,
                        ["substituteItemId"] = input.SubstituteItemId,
                        ["override"] = input.SupervisorOverride
                    }),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(current, NormalizeUtc(clock.UtcNow.UtcDateTime)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exceptionError)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Concurrency(
                "outbound_exception.concurrency_conflict",
                exceptionError.Message));
        }
        catch (ArgumentException exceptionError)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                "outbound_exception.resolve_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.BusinessRule(
                "outbound_exception.resolve_blocked",
                exceptionError.Message));
        }
        catch (Exception exceptionError)
        {
            logger.LogError(exceptionError, "Outbound exception resolution failed for {ExceptionId}", exceptionId);
            return Result.Failure<OutboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                "outbound_exception.resolve_failed",
                "The outbound exception resolution could not be saved."));
        }
        finally
        {
            if (transactionStarted)
            {
                await RollbackAsync(true);
            }
        }
    }

    private async Task<Result<OutboundExceptionDto>> ExecuteSimpleMutationAsync(
        int exceptionId,
        string auditAction,
        string operation,
        string userId,
        Action<OutboundException> mutation,
        CancellationToken cancellationToken)
    {
        var exception = await context.OutboundExceptions.SingleOrDefaultAsync(
            value => value.Id == exceptionId,
            cancellationToken);
        if (exception is null)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.NotFound(
                "outbound_exception.not_found",
                "The outbound exception was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.SalesOrdersManage,
            exception.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<OutboundExceptionDto>();
        }

        var transactionStarted = true;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);
            mutation(exception);
            await auditWriter.RecordAsync(
                OutboundAudit(
                    exception,
                    auditAction,
                    userId,
                    new Dictionary<string, object?> { ["operation"] = operation }),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            transactionStarted = false;
            return Result.Success(Map(exception, NormalizeUtc(clock.UtcNow.UtcDateTime)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exceptionError)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.Validation(
                $"outbound_exception.{operation}_invalid",
                exceptionError.Message));
        }
        catch (InvalidOperationException exceptionError)
        {
            return Result.Failure<OutboundExceptionDto>(WmsErrors.BusinessRule(
                $"outbound_exception.{operation}_blocked",
                exceptionError.Message));
        }
        catch (Exception exceptionError)
        {
            logger.LogError(exceptionError, "Outbound exception {Operation} failed for {ExceptionId}", operation, exceptionId);
            return Result.Failure<OutboundExceptionDto>(WmsErrors.FromException(
                exceptionError,
                $"outbound_exception.{operation}_failed",
                "The outbound exception change could not be saved."));
        }
        finally
        {
            if (transactionStarted)
            {
                await RollbackAsync(true);
            }
        }
    }

    private async Task ApplyResolutionAsync(
        OutboundException exception,
        OutboundExceptionResolutionInput input,
        string userId,
        CancellationToken cancellationToken)
    {
        var reason = input.SupervisorOverride && !string.IsNullOrWhiteSpace(input.OverrideReason)
            ? $"{input.Reason}; override: {input.OverrideReason}"
            : input.Reason;

        switch (input.Resolution)
        {
            case OutboundExceptionResolution.ReleaseReservation:
                await ReleaseReservationAsync(exception, reason, userId, cancellationToken);
                break;
            case OutboundExceptionResolution.Reallocate:
                await ReallocateAsync(exception, reason, userId, cancellationToken);
                break;
            case OutboundExceptionResolution.PartialBackorder:
                await ReleaseReservationAsync(exception, reason, userId, cancellationToken);
                await SetOrderReleasedAsync(exception.SalesOrderId, cancellationToken);
                break;
            case OutboundExceptionResolution.CancelLine:
                await ReleaseReservationAsync(exception, reason, userId, cancellationToken);
                await CancelLineAsync(exception, input.Quantity, cancellationToken);
                break;
            case OutboundExceptionResolution.Substitute:
                await SubstituteAsync(exception, input, reason, cancellationToken);
                break;
            case OutboundExceptionResolution.Repick:
            case OutboundExceptionResolution.Repack:
                await ResolveWorkAsync(exception, reason, userId, cancellationToken);
                break;
            case OutboundExceptionResolution.HoldOrder:
                await HoldOrderAsync(exception, reason, userId, cancellationToken);
                break;
            case OutboundExceptionResolution.ChangeCarrier:
                await ChangeCarrierAsync(exception, input, reason, cancellationToken);
                break;
            case OutboundExceptionResolution.Unload:
                await UnloadAsync(exception, cancellationToken);
                break;
            case OutboundExceptionResolution.CancelOrder:
                await ReleaseReservationAsync(exception, reason, userId, cancellationToken);
                await CancelOrderAsync(exception, userId, cancellationToken);
                break;
            case OutboundExceptionResolution.SupervisorOverride:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input));
        }
    }

    private async Task ReleaseReservationAsync(
        OutboundException exception,
        string reason,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!exception.InventoryReservationId.HasValue)
        {
            throw new InvalidOperationException("This resolution requires a linked inventory reservation.");
        }

        await inventoryReservationService.ReleaseAsync(
            new InventoryReservationMutationRequest(
                exception.InventoryReservationId.Value,
                null,
                userId,
                $"outbound-exception:{exception.Id}",
                reason),
            cancellationToken);
    }

    private async Task ReallocateAsync(
        OutboundException exception,
        string reason,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!exception.InventoryReservationId.HasValue)
        {
            throw new InvalidOperationException("Reallocation requires a linked inventory reservation.");
        }

        await inventoryReservationService.ReallocateAsync(
            new InventoryReservationMutationRequest(
                exception.InventoryReservationId.Value,
                null,
                userId,
                $"outbound-exception:{exception.Id}",
                reason),
            cancellationToken);
    }

    private async Task CancelLineAsync(
        OutboundException exception,
        decimal? requestedQuantity,
        CancellationToken cancellationToken)
    {
        if (!exception.SalesOrderLineId.HasValue)
        {
            throw new InvalidOperationException("Line cancellation requires a linked sales-order line.");
        }

        var line = await context.SalesOrderLines.SingleAsync(
            value => value.Id == exception.SalesOrderLineId.Value,
            cancellationToken);
        var availableToCancel = line.OrderedBaseQuantity - line.ShippedBaseQuantity - line.CancelledBaseQuantity;
        var quantity = requestedQuantity ?? availableToCancel;
        if (quantity <= 0m || quantity > availableToCancel)
        {
            throw new InvalidOperationException("The requested cancellation quantity is outside the line remainder.");
        }

        var unpickedAllocation = line.AllocatedBaseQuantity - line.PickedBaseQuantity;
        if (unpickedAllocation > 0m)
        {
            line.ReleaseAllocation(Math.Min(unpickedAllocation, quantity));
        }

        line.CancelQuantity(quantity);
    }

    private async Task SubstituteAsync(
        OutboundException exception,
        OutboundExceptionResolutionInput input,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!exception.SalesOrderLineId.HasValue || !input.SubstituteItemId.HasValue)
        {
            throw new InvalidOperationException("Substitution requires a linked order line and substitute item.");
        }

        var line = await context.SalesOrderLines.SingleAsync(
            value => value.Id == exception.SalesOrderLineId.Value,
            cancellationToken);
        var substitute = await context.Items.SingleOrDefaultAsync(
            value => value.Id == input.SubstituteItemId.Value,
            cancellationToken)
            ?? throw new InvalidOperationException("The substitute item was not found.");
        line.Substitute(substitute, reason);
    }

    private async Task ResolveWorkAsync(
        OutboundException exception,
        string reason,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!exception.WarehouseWorkId.HasValue)
        {
            throw new InvalidOperationException("Pick or pack recovery requires linked warehouse work.");
        }

        var work = await context.WarehouseWorks.SingleAsync(
            value => value.Id == exception.WarehouseWorkId.Value,
            cancellationToken);
        work.ResolveException(userId, reason, clock.UtcNow.UtcDateTime);
    }

    private async Task HoldOrderAsync(
        OutboundException exception,
        string reason,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!exception.SalesOrderId.HasValue)
        {
            throw new InvalidOperationException("Holding an order requires a linked sales order.");
        }

        var order = await context.SalesOrders.SingleAsync(
            value => value.Id == exception.SalesOrderId.Value,
            cancellationToken);
        order.Hold(userId, reason, clock.UtcNow.UtcDateTime);
    }

    private async Task ChangeCarrierAsync(
        OutboundException exception,
        OutboundExceptionResolutionInput input,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!exception.ShipmentId.HasValue || !input.NewCarrierId.HasValue || !input.NewCarrierServiceId.HasValue)
        {
            throw new InvalidOperationException(
                "Carrier correction requires a linked shipment, carrier, and carrier service.");
        }

        var service = await context.CarrierServices.SingleOrDefaultAsync(
            value => value.Id == input.NewCarrierServiceId.Value &&
                     value.CarrierId == input.NewCarrierId.Value &&
                     value.IsActive,
            cancellationToken);
        if (service is null)
        {
            throw new InvalidOperationException(
                "The replacement carrier service is not active or does not belong to the carrier.");
        }

        var shipment = await context.Shipments.SingleAsync(
            value => value.Id == exception.ShipmentId.Value,
            cancellationToken);
        shipment.ChangeCarrier(input.NewCarrierId.Value, input.NewCarrierServiceId.Value, reason);
    }

    private async Task UnloadAsync(
        OutboundException exception,
        CancellationToken cancellationToken)
    {
        if (!exception.ShipmentId.HasValue ||
            !exception.ShipmentPackageId.HasValue ||
            !exception.ShipmentLoadId.HasValue)
        {
            throw new InvalidOperationException(
                "Unload recovery requires linked shipment, package, and load references.");
        }

        var shipment = await context.Shipments
            .Include(value => value.Packages)
            .SingleAsync(value => value.Id == exception.ShipmentId.Value, cancellationToken);
        var link = shipment.Packages.SingleOrDefault(value =>
            value.ShipmentPackageId == exception.ShipmentPackageId.Value &&
            value.ShipmentLoadId == exception.ShipmentLoadId.Value)
            ?? throw new InvalidOperationException("The shipment package is not loaded on the referenced load.");
        if (shipment.Status == ShipmentStatus.Loaded)
        {
            shipment.ReopenLoading();
        }

        link.Unload();
    }

    private async Task CancelOrderAsync(
        OutboundException exception,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!exception.SalesOrderId.HasValue)
        {
            throw new InvalidOperationException("Order cancellation requires a linked sales order.");
        }

        var order = await context.SalesOrders.SingleAsync(
            value => value.Id == exception.SalesOrderId.Value,
            cancellationToken);
        order.Cancel(userId, clock.UtcNow.UtcDateTime);
    }

    private async Task SetOrderReleasedAsync(int? salesOrderId, CancellationToken cancellationToken)
    {
        if (!salesOrderId.HasValue)
        {
            return;
        }

        var order = await context.SalesOrders.SingleAsync(
            value => value.Id == salesOrderId.Value,
            cancellationToken);
        if (order.Status is SalesOrderStatus.Allocating or
            SalesOrderStatus.PartiallyAllocated or
            SalesOrderStatus.Exception)
        {
            order.SetAllocationStatus(SalesOrderStatus.Released);
        }
    }

    private async Task<ReferenceSet> LoadReferencesAsync(
        OutboundExceptionInput input,
        CancellationToken cancellationToken)
    {
        var warehouseExists = await context.Warehouses.AnyAsync(
            value => value.Id == input.WarehouseId,
            cancellationToken);
        if (!warehouseExists)
        {
            throw new InvalidOperationException("The outbound exception warehouse was not found.");
        }

        var order = input.SalesOrderId.HasValue
            ? await context.SalesOrders.SingleOrDefaultAsync(
                value => value.Id == input.SalesOrderId.Value,
                cancellationToken)
            : null;
        if (input.SalesOrderId.HasValue && order is null)
        {
            throw new InvalidOperationException("The linked sales order was not found.");
        }
        EnsureReference(order is null ? null : order.WarehouseId, input.WarehouseId, "sales order");

        var line = input.SalesOrderLineId.HasValue
            ? await context.SalesOrderLines.SingleOrDefaultAsync(
                value => value.Id == input.SalesOrderLineId.Value,
                cancellationToken)
            : null;
        if (input.SalesOrderLineId.HasValue && line is null)
        {
            throw new InvalidOperationException("The linked sales-order line was not found.");
        }
        if (line is not null &&
            input.SalesOrderId.HasValue &&
            line.SalesOrderId != input.SalesOrderId.Value)
        {
            throw new InvalidOperationException("The order line does not belong to the linked sales order.");
        }

        var reservation = input.InventoryReservationId.HasValue
            ? await context.InventoryReservations.SingleOrDefaultAsync(
                value => value.Id == input.InventoryReservationId.Value,
                cancellationToken)
            : null;
        if (input.InventoryReservationId.HasValue && reservation is null)
        {
            throw new InvalidOperationException("The linked inventory reservation was not found.");
        }
        EnsureReference(
            reservation is null ? null : reservation.WarehouseId,
            input.WarehouseId,
            "inventory reservation");

        var work = input.WarehouseWorkId.HasValue
            ? await context.WarehouseWorks.SingleOrDefaultAsync(
                value => value.Id == input.WarehouseWorkId.Value,
                cancellationToken)
            : null;
        if (input.WarehouseWorkId.HasValue && work is null)
        {
            throw new InvalidOperationException("The linked warehouse work was not found.");
        }
        EnsureReference(work is null ? null : work.WarehouseId, input.WarehouseId, "warehouse work");

        var workLine = input.WarehouseWorkLineId.HasValue
            ? await context.WarehouseWorkLines.SingleOrDefaultAsync(
                value => value.Id == input.WarehouseWorkLineId.Value,
                cancellationToken)
            : null;
        if (input.WarehouseWorkLineId.HasValue && workLine is null)
        {
            throw new InvalidOperationException("The linked warehouse work line was not found.");
        }
        if (workLine is not null &&
            input.WarehouseWorkId.HasValue &&
            workLine.WarehouseWorkId != input.WarehouseWorkId.Value)
        {
            throw new InvalidOperationException("The work line does not belong to the linked warehouse work.");
        }

        var shipment = input.ShipmentId.HasValue
            ? await context.Shipments.SingleOrDefaultAsync(
                value => value.Id == input.ShipmentId.Value,
                cancellationToken)
            : null;
        if (input.ShipmentId.HasValue && shipment is null)
        {
            throw new InvalidOperationException("The linked shipment was not found.");
        }
        EnsureReference(shipment is null ? null : shipment.WarehouseId, input.WarehouseId, "shipment");

        var package = input.ShipmentPackageId.HasValue
            ? await context.ShipmentPackages.SingleOrDefaultAsync(
                value => value.Id == input.ShipmentPackageId.Value,
                cancellationToken)
            : null;
        if (input.ShipmentPackageId.HasValue && package is null)
        {
            throw new InvalidOperationException("The linked shipment package was not found.");
        }
        EnsureReference(package is null ? null : package.WarehouseId, input.WarehouseId, "shipment package");

        var load = input.ShipmentLoadId.HasValue
            ? await context.ShipmentLoads.SingleOrDefaultAsync(
                value => value.Id == input.ShipmentLoadId.Value,
                cancellationToken)
            : null;
        if (input.ShipmentLoadId.HasValue && load is null)
        {
            throw new InvalidOperationException("The linked shipment load was not found.");
        }
        if (load is not null &&
            input.ShipmentId.HasValue &&
            load.ShipmentId != input.ShipmentId.Value)
        {
            throw new InvalidOperationException("The shipment load does not belong to the linked shipment.");
        }

        return new ReferenceSet(order, reservation, work);
    }

    private static void EnsureReference(int? referenceWarehouseId, int warehouseId, string referenceName)
    {
        if (referenceWarehouseId.HasValue && referenceWarehouseId.Value != warehouseId)
        {
            throw new InvalidOperationException($"The linked {referenceName} belongs to another warehouse.");
        }
    }

    private async Task RollbackAsync(bool transactionStarted)
    {
        if (!transactionStarted)
        {
            return;
        }

        try
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(rollbackException, "Outbound exception transaction rollback failed");
        }
    }

    private static bool RequiresSupervisor(OutboundExceptionResolution resolution) =>
        resolution is OutboundExceptionResolution.Substitute or
            OutboundExceptionResolution.CancelLine or
            OutboundExceptionResolution.CancelOrder or
            OutboundExceptionResolution.ChangeCarrier or
            OutboundExceptionResolution.Unload or
            OutboundExceptionResolution.SupervisorOverride;

    private static WarehouseWorkExceptionType ToWorkExceptionType(OutboundExceptionCode code) => code switch
    {
        OutboundExceptionCode.DamagedStock => WarehouseWorkExceptionType.Damaged,
        OutboundExceptionCode.LocationBlocked => WarehouseWorkExceptionType.Blocked,
        OutboundExceptionCode.StockNotFound => WarehouseWorkExceptionType.NotFound,
        OutboundExceptionCode.AllocationShortage => WarehouseWorkExceptionType.Shortage,
        _ => WarehouseWorkExceptionType.Other
    };

    private static string BuildResolutionDetails(OutboundExceptionResolutionInput input) =>
        JsonSerializer.Serialize(new
        {
            input.Quantity,
            input.SubstituteItemId,
            input.NewCarrierId,
            input.NewCarrierServiceId,
            input.SupervisorOverride,
            input.OverrideReason
        });

    private static AuditRecord OutboundAudit(
        OutboundException exception,
        string action,
        string userId,
        IReadOnlyDictionary<string, object?> details) =>
        new(
            action,
            WmsAuditEntityTypes.OutboundException,
            exception.ExceptionNumber,
            exception.WarehouseId,
            After: new Dictionary<string, object?>(details)
            {
                ["status"] = exception.Status.ToString(),
                ["revision"] = exception.Revision
            },
            ActorUserId: userId);

    private static string Hash<T>(T value)
    {
        var payload = JsonSerializer.Serialize(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static OutboundExceptionDto Map(OutboundException exception, DateTime asOfUtc) => new(
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
        exception.SalesOrderId,
        exception.SalesOrderLineId,
        exception.InventoryReservationId,
        exception.WarehouseWorkId,
        exception.WarehouseWorkLineId,
        exception.ShipmentId,
        exception.ShipmentPackageId,
        exception.ShipmentLoadId,
        exception.ItemId,
        exception.LocationId,
        exception.LotId,
        exception.SerialNumberId,
        exception.LicensePlateId,
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
        exception.ResolutionDetails,
        exception.ResolvedByUserId,
        exception.ResolvedAtUtc,
        exception.CreatedAt,
        exception.UpdatedAt,
        exception.Revision,
        exception.IsTerminal,
        exception.IsOverdue(asOfUtc));

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record ReferenceSet(
        SalesOrder? Order,
        InventoryReservation? Reservation,
        WarehouseWorkEntity? Work);
}
