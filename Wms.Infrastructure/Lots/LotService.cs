using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Lots;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;

namespace Wms.Infrastructure.Lots;

public sealed class LotService(
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<LotService> logger) : ILotService
{
    public async Task<Result<Lot>> ResolveForReceiptAsync(
        Item item,
        string lotNumber,
        LotDetailsRequest details,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!item.RequiresLot)
            {
                return Result.Failure<Lot>(WmsErrors.Validation(
                    "lot.not_required",
                    $"Item '{item.Sku}' is not lot controlled."));
            }

            if (string.IsNullOrWhiteSpace(lotNumber))
            {
                return Result.Failure<Lot>(WmsErrors.Validation(
                    "lot.number_required",
                    $"Item '{item.Sku}' requires a lot number."));
            }

            if (item.RequiresExpiry && !details.ExpiryDate.HasValue)
            {
                return Result.Failure<Lot>(WmsErrors.Validation(
                    "lot.expiry_required",
                    $"Item '{item.Sku}' requires an expiry date."));
            }

            var normalizedNumber = Lot.NormalizeNumber(lotNumber);
            var existing = await unitOfWork.Lots.GetByItemAndNumberAsync(
                item.Id,
                normalizedNumber,
                cancellationToken);
            var status = item.QualityInspectionRequired
                ? LotStatus.Quarantine
                : LotStatus.Active;
            var lot = await unitOfWork.Lots.GetOrCreateAsync(
                item.Id,
                normalizedNumber,
                details.ExpiryDate,
                details.ManufacturedDate,
                details.RetestDate,
                details.HoldUntil,
                details.SupplierLotNumber,
                details.Notes,
                status,
                clock.UtcNow.UtcDateTime,
                cancellationToken);

            // Non-relational test stores do not assign generated keys until their
            // first save. Persist only the new lot here so the movement can carry
            // a real immutable foreign-key value; production providers use the
            // repository's atomic upsert and already return the generated key.
            if (lot.Id == 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (existing is null)
            {
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.LotCreated,
                        WmsAuditEntityTypes.Lot,
                        lot.Id.ToString(CultureInfo.InvariantCulture),
                        After: ToAuditMetadata(lot),
                        ActorUserId: userId),
                    cancellationToken);
            }

            if (lot.ItemId != item.Id)
            {
                return Result.Failure<Lot>(WmsErrors.Conflict(
                    "lot.item_mismatch",
                    $"Lot '{normalizedNumber}' belongs to a different item."));
            }

            var today = DateOnly.FromDateTime(clock.UtcNow.DateTime);
            if (!lot.IsReceivingAllowed(today, blockExpired: true))
            {
                return Result.Failure<Lot>(WmsErrors.BusinessRule(
                    "lot.receipt_blocked",
                    $"Lot '{lot.Number}' is not eligible for receipt in status {lot.Status}."));
            }

            if (existing is not null)
            {
                var history = await HasHistoryAsync(lot.Id, cancellationToken);
                var datesChanged = HasSuppliedChanges(lot, details);
                if (datesChanged && history)
                {
                    return Result.Failure<Lot>(WmsErrors.Conflict(
                        "lot.metadata_locked",
                        $"Lot '{lot.Number}' has inventory history; date and supplier metadata changes require a controlled lot edit."));
                }

                if (datesChanged)
                {
                    lot.UpdateDetails(
                        details.ExpiryDate ?? lot.ExpiryDate,
                        details.ManufacturedDate ?? lot.ManufacturedDate,
                        details.RetestDate ?? lot.RetestDate,
                        details.HoldUntil ?? lot.HoldUntil,
                        details.SupplierLotNumber ?? lot.SupplierLotNumber,
                        details.Notes ?? lot.Notes);
                    await unitOfWork.Lots.UpdateAsync(lot, cancellationToken);
                    await auditWriter.RecordAsync(
                        new AuditRecord(
                            WmsAuditActions.LotUpdated,
                            WmsAuditEntityTypes.Lot,
                            lot.Id.ToString(CultureInfo.InvariantCulture),
                            After: ToAuditMetadata(lot),
                            ActorUserId: userId),
                        cancellationToken);
                }
            }

            return Result.Success(lot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Lot resolution failed for item {ItemId} and lot {LotNumber}",
                item.Id,
                lotNumber);
            return Result.Failure<Lot>(WmsErrors.FromException(
                exception,
                "lot.resolve_failed",
                "The lot could not be resolved."));
        }
    }

    public async Task<Result<Lot?>> ResolveForMovementAsync(
        Item item,
        string? lotNumber,
        bool requireAllocationEligibility,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(lotNumber))
            {
                if (item.RequiresLot)
                {
                    return Result.Failure<Lot?>(WmsErrors.Validation(
                        "lot.number_required",
                        $"Item '{item.Sku}' requires a lot number for this operation."));
                }

                return Result.Success<Lot?>(null);
            }

            var lot = await unitOfWork.Lots.GetByItemAndNumberAsync(
                item.Id,
                lotNumber,
                cancellationToken);
            if (lot is null)
            {
                return Result.Failure<Lot?>(WmsErrors.NotFound(
                    "lot.not_found",
                    $"Lot '{lotNumber.Trim()}' was not found for item '{item.Sku}'."));
            }

            if (lot.ItemId != item.Id)
            {
                return Result.Failure<Lot?>(WmsErrors.Conflict(
                    "lot.item_mismatch",
                    $"Lot '{lot.Number}' belongs to a different item."));
            }

            if (requireAllocationEligibility && !lot.IsAllocationEligible(businessDate))
            {
                return Result.Failure<Lot?>(WmsErrors.BusinessRule(
                    "lot.allocation_blocked",
                    $"Lot '{lot.Number}' is not eligible for allocation in status {lot.Status} or at the current expiry boundary."));
            }

            return Result.Success<Lot?>(lot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot movement resolution failed for item {ItemId}", item.Id);
            return Result.Failure<Lot?>(WmsErrors.FromException(
                exception,
                "lot.resolve_movement_failed",
                "The lot could not be resolved for this movement."));
        }
    }

    public async Task<Result<LotTraceabilityDto>> GetTraceabilityAsync(
        int lotId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lot = await unitOfWork.Lots.GetByIdAsync(lotId, cancellationToken);
            if (lot is null)
            {
                return Result.Failure<LotTraceabilityDto>(WmsErrors.NotFound(
                    "lot.not_found",
                    $"Lot {lotId} was not found."));
            }

            var stock = (await unitOfWork.Stock.GetByLotIdAsync(lotId, cancellationToken))
                .Select(row => new LotStockDto(
                    row.Id,
                    row.LocationId,
                    row.Location.Code,
                    row.Location.Name,
                    row.QuantityAvailable.Value,
                    row.QuantityReserved.Value,
                    row.GetAvailableQuantity().Value,
                    row.SerialNumber))
                .ToArray();
            var movements = (await unitOfWork.Movements.GetByLotIdAsync(lotId, cancellationToken))
                .Select(movement => new LotMovementDto(
                    movement.Id,
                    movement.Type,
                    movement.Quantity.Value,
                    movement.FromLocationId,
                    movement.ToLocationId,
                    movement.FromLocation?.Code,
                    movement.ToLocation?.Code,
                    movement.UserId,
                    movement.ReferenceNumber,
                    movement.Notes,
                    movement.Timestamp))
                .ToArray();

            return Result.Success(new LotTraceabilityDto(Map(lot), stock, movements));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot traceability lookup failed for lot {LotId}", lotId);
            return Result.Failure<LotTraceabilityDto>(WmsErrors.FromException(
                exception,
                "lot.traceability_failed",
                "The lot traceability could not be loaded."));
        }
    }

    public async Task<Result<IEnumerable<LotDto>>> SearchAsync(
        LotSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lots = await unitOfWork.Lots.GetAllAsync(cancellationToken);
            var filtered = lots.Where(lot =>
                    (query.Status is null || lot.Status == query.Status) &&
                    (query.IncludeClosed || lot.Status != LotStatus.Closed) &&
                    (string.IsNullOrWhiteSpace(query.ItemSku) ||
                     lot.Item.Sku.Equals(query.ItemSku.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(query.Number) ||
                     lot.Number.Contains(query.Number.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderBy(lot => lot.Item.Sku)
                .ThenBy(lot => lot.Number)
                .Select(Map)
                .ToArray();
            return Result.Success<IEnumerable<LotDto>>(filtered);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot search failed");
            return Result.Failure<IEnumerable<LotDto>>(WmsErrors.FromException(
                exception,
                "lot.search_failed",
                "The lot search could not be completed."));
        }
    }

    public async Task<Result<IEnumerable<LotExpiryAlertDto>>> GetExpiryAlertsAsync(
        DateOnly businessDate,
        int warningDays,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (warningDays < 0)
            {
                return Result.Failure<IEnumerable<LotExpiryAlertDto>>(WmsErrors.Validation(
                    "lot.warning_days_invalid",
                    "Expiry warning days cannot be negative."));
            }

            var throughDate = businessDate.AddDays(warningDays);
            var lots = await unitOfWork.Lots.GetExpiringAsync(throughDate, cancellationToken);
            var expiredLots = new List<Lot>();
            foreach (var lot in lots)
            {
                if (lot.MarkExpired(businessDate))
                {
                    expiredLots.Add(lot);
                    await unitOfWork.Lots.UpdateAsync(lot, cancellationToken);
                    await auditWriter.RecordAsync(
                        new AuditRecord(
                            WmsAuditActions.LotStatusChanged,
                            WmsAuditEntityTypes.Lot,
                            lot.Id.ToString(CultureInfo.InvariantCulture),
                            Before: new Dictionary<string, object?> { ["status"] = "ActiveOrReleased" },
                            After: new Dictionary<string, object?>
                            {
                                ["status"] = LotStatus.Expired.ToString(),
                                ["reason"] = "expiry boundary reached"
                            }),
                        cancellationToken);
                }
            }

            if (expiredLots.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var alerts = lots
                .Where(lot => lot.ExpiryDate.HasValue)
                .Select(lot =>
                {
                    var expiryDate = DateOnly.FromDateTime(lot.ExpiryDate!.Value);
                    return new LotExpiryAlertDto(
                        Map(lot),
                        expiryDate,
                        expiryDate < businessDate,
                        expiryDate.DayNumber - businessDate.DayNumber);
                })
                .ToArray();
            return Result.Success<IEnumerable<LotExpiryAlertDto>>(alerts);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot expiry alert lookup failed");
            return Result.Failure<IEnumerable<LotExpiryAlertDto>>(WmsErrors.FromException(
                exception,
                "lot.expiry_alerts_failed",
                "The lot expiry alerts could not be loaded."));
        }
    }

    public async Task<Result<LotDto>> UpdateAsync(
        int lotId,
        LotDetailsRequest details,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lot = await unitOfWork.Lots.GetByIdAsync(lotId, cancellationToken);
            if (lot is null)
            {
                return Result.Failure<LotDto>(WmsErrors.NotFound(
                    "lot.not_found",
                    $"Lot {lotId} was not found."));
            }

            if (await HasHistoryAsync(lotId, cancellationToken) &&
                HasDateOrTextChanges(lot, details))
            {
                return Result.Failure<LotDto>(WmsErrors.Conflict(
                    "lot.metadata_locked",
                    $"Lot '{lot.Number}' has inventory history; date and supplier metadata changes are restricted."));
            }

            lot.UpdateDetails(
                details.ExpiryDate,
                details.ManufacturedDate,
                details.RetestDate,
                details.HoldUntil,
                details.SupplierLotNumber,
                details.Notes);
            await unitOfWork.Lots.UpdateAsync(lot, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LotUpdated,
                    WmsAuditEntityTypes.Lot,
                    lot.Id.ToString(CultureInfo.InvariantCulture),
                    After: ToAuditMetadata(lot),
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(lot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot update failed for lot {LotId}", lotId);
            return Result.Failure<LotDto>(WmsErrors.FromException(
                exception,
                "lot.update_failed",
                "The lot could not be updated."));
        }
    }

    public async Task<Result<LotDto>> ChangeStatusAsync(
        int lotId,
        LotStatus status,
        string reason,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure<LotDto>(WmsErrors.Validation(
                    "lot.status_reason_required",
                    "A reason is required for every lot status change."));
            }

            var lot = await unitOfWork.Lots.GetByIdAsync(lotId, cancellationToken);
            if (lot is null)
            {
                return Result.Failure<LotDto>(WmsErrors.NotFound(
                    "lot.not_found",
                    $"Lot {lotId} was not found."));
            }

            var before = lot.Status;
            lot.SetStatus(status, reason);
            await unitOfWork.Lots.UpdateAsync(lot, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.LotStatusChanged,
                    WmsAuditEntityTypes.Lot,
                    lot.Id.ToString(CultureInfo.InvariantCulture),
                    Before: new Dictionary<string, object?> { ["status"] = before.ToString() },
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = lot.Status.ToString(),
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(lot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Lot status change failed for lot {LotId}", lotId);
            return Result.Failure<LotDto>(WmsErrors.FromException(
                exception,
                "lot.status_change_failed",
                "The lot status could not be changed."));
        }
    }

    private async Task<bool> HasHistoryAsync(int lotId, CancellationToken cancellationToken)
    {
        var stock = await unitOfWork.Stock.GetByLotIdAsync(lotId, cancellationToken);
        if (stock.Any(row => row.QuantityAvailable.Value > 0 || row.QuantityReserved.Value > 0))
        {
            return true;
        }

        return (await unitOfWork.Movements.GetByLotIdAsync(lotId, cancellationToken)).Any();
    }

    private static bool HasDateOrTextChanges(Lot lot, LotDetailsRequest details) =>
        NormalizeDate(details.ExpiryDate) != lot.ExpiryDate ||
        NormalizeDate(details.ManufacturedDate) != lot.ManufacturedDate ||
        NormalizeDate(details.RetestDate) != lot.RetestDate ||
        NormalizeDate(details.HoldUntil) != lot.HoldUntil ||
        NormalizeOptional(details.SupplierLotNumber) != lot.SupplierLotNumber ||
        NormalizeOptional(details.Notes) != lot.Notes;

    private static bool HasSuppliedChanges(Lot lot, LotDetailsRequest details) =>
        (details.ExpiryDate.HasValue && NormalizeDate(details.ExpiryDate) != lot.ExpiryDate) ||
        (details.ManufacturedDate.HasValue &&
         NormalizeDate(details.ManufacturedDate) != lot.ManufacturedDate) ||
        (details.RetestDate.HasValue && NormalizeDate(details.RetestDate) != lot.RetestDate) ||
        (details.HoldUntil.HasValue && NormalizeDate(details.HoldUntil) != lot.HoldUntil) ||
        (!string.IsNullOrWhiteSpace(details.SupplierLotNumber) &&
         NormalizeOptional(details.SupplierLotNumber) != lot.SupplierLotNumber) ||
        (!string.IsNullOrWhiteSpace(details.Notes) &&
         NormalizeOptional(details.Notes) != lot.Notes);

    private static LotDto Map(Lot lot) =>
        new(
            lot.Id,
            lot.ItemId,
            lot.Item.Sku,
            lot.Item.Name,
            lot.Number,
            lot.ExpiryDate,
            lot.ManufacturedDate,
            lot.RetestDate,
            lot.HoldUntil,
            lot.SupplierLotNumber,
            lot.Notes,
            lot.Status,
            lot.IsActive,
            lot.RecallReason,
            lot.RecalledAt,
            lot.CreatedAt,
            lot.UpdatedAt);

    private static Dictionary<string, object?> ToAuditMetadata(Lot lot) =>
        new Dictionary<string, object?>
        {
            ["itemId"] = lot.ItemId,
            ["number"] = lot.Number,
            ["expiryDate"] = lot.ExpiryDate,
            ["manufacturedDate"] = lot.ManufacturedDate,
            ["retestDate"] = lot.RetestDate,
            ["holdUntil"] = lot.HoldUntil,
            ["supplierLotNumber"] = lot.SupplierLotNumber,
            ["status"] = lot.Status.ToString()
        };

    private static DateTime? NormalizeDate(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified)
            : null;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
