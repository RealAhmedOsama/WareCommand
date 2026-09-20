using System.Globalization;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Lots;
using Wms.Application.SerialNumbers;
using Wms.Application.Time;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.SerialNumbers;

public sealed class SerialNumberService(
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<SerialNumberService> logger) : ISerialNumberService
{
    public async Task<Result<SerialNumber>> ResolveForReceiptAsync(
        Item item,
        string number,
        int? lotId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!item.RequiresSerial)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Validation(
                    "serial.not_required",
                    $"Item '{item.Sku}' is not serial controlled."));
            }

            var normalized = SerialNumber.NormalizeNumber(number);
            if (item.RequiresLot && !lotId.HasValue)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Validation(
                    "serial.lot_required",
                    $"Serial-controlled item '{item.Sku}' requires a resolved lot."));
            }

            var existing = await unitOfWork.SerialNumbers.GetByItemAndNumberAsync(
                item.Id,
                normalized,
                cancellationToken);
            if (existing is not null)
            {
                if (existing.LotId != lotId)
                {
                    return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                        "serial.lot_mismatch",
                        $"Serial '{normalized}' is already assigned to a different lot."));
                }

                if (existing.HasMigrationConflict)
                {
                    return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                        "serial.migration_conflict",
                        $"Serial '{normalized}' has an unresolved migration conflict."));
                }

                if (existing.Status != SerialStatus.Returned)
                {
                    return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                        "serial.duplicate_active",
                        $"Serial '{normalized}' already exists in status {existing.Status}."));
                }

                return Result.Success(existing);
            }

            var serial = await unitOfWork.SerialNumbers.GetOrCreateAsync(
                item.Id,
                normalized,
                lotId,
                clock.UtcNow.UtcDateTime,
                cancellationToken);
            if (serial.LotId != lotId)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                    "serial.lot_mismatch",
                    $"Serial '{normalized}' is already assigned to a different lot."));
            }

            if (serial.Status != SerialStatus.Available)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                    "serial.duplicate_active",
                    $"Serial '{normalized}' already exists in status {serial.Status}."));
            }

            if (serial.Id == 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SerialCreated,
                    WmsAuditEntityTypes.Serial,
                    serial.Id.ToString(CultureInfo.InvariantCulture),
                    After: ToMetadata(serial)),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(serial);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serial receipt resolution failed for item {ItemId}", item.Id);
            return Result.Failure<SerialNumber>(WmsErrors.FromException(
                exception,
                "serial.resolve_receipt_failed",
                "The serial number could not be resolved for receipt."));
        }
    }

    public async Task<Result<SerialNumber>> ResolveForMovementAsync(
        Item item,
        string number,
        int? lotId,
        int? expectedLocationId,
        bool requireAllocationEligibility,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!item.RequiresSerial)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Validation(
                    "serial.not_required",
                    $"Item '{item.Sku}' is not serial controlled."));
            }

            var normalized = SerialNumber.NormalizeNumber(number);
            var serial = await unitOfWork.SerialNumbers.GetByItemAndNumberAsync(
                item.Id,
                normalized,
                cancellationToken);
            if (serial is null)
            {
                return Result.Failure<SerialNumber>(WmsErrors.NotFound(
                    "serial.not_found",
                    $"Serial '{normalized}' has no persisted identity for item '{item.Sku}'."));
            }

            if (serial.LotId != lotId)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                    "serial.lot_mismatch",
                    $"Serial '{normalized}' does not belong to the requested lot."));
            }

            if (serial.HasMigrationConflict)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                    "serial.migration_conflict",
                    $"Serial '{normalized}' has an unresolved migration conflict."));
            }

            if (expectedLocationId.HasValue && serial.CurrentLocationId != expectedLocationId)
            {
                return Result.Failure<SerialNumber>(WmsErrors.Conflict(
                    "serial.location_conflict",
                    $"Serial '{normalized}' is not currently recorded at the requested location."));
            }

            if (requireAllocationEligibility && !serial.IsAllocationEligible)
            {
                return Result.Failure<SerialNumber>(WmsErrors.BusinessRule(
                    "serial.not_allocatable",
                    $"Serial '{normalized}' is not eligible for allocation in status {serial.Status}."));
            }

            if (serial.Status is SerialStatus.Shipped or SerialStatus.Scrapped or SerialStatus.Corrected)
            {
                return Result.Failure<SerialNumber>(WmsErrors.BusinessRule(
                    "serial.not_movable",
                    $"Serial '{normalized}' cannot be moved while it is {serial.Status}."));
            }

            return Result.Success(serial);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serial movement resolution failed for item {ItemId}", item.Id);
            return Result.Failure<SerialNumber>(WmsErrors.FromException(
                exception,
                "serial.resolve_movement_failed",
                "The serial number could not be resolved for movement."));
        }
    }

    public async Task<Result<SerialTraceabilityDto>> GetTraceabilityAsync(
        int serialId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serial = await unitOfWork.SerialNumbers.GetByIdAsync(serialId, cancellationToken);
            if (serial is null)
            {
                return Result.Failure<SerialTraceabilityDto>(WmsErrors.NotFound(
                    "serial.not_found",
                    $"Serial {serialId} was not found."));
            }

            var stock = (await unitOfWork.Stock.GetBySerialNumberIdAsync(serialId, cancellationToken))
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
            var movements = (await unitOfWork.Movements.GetBySerialNumberIdAsync(
                    serialId,
                    cancellationToken))
                .Select(movement => new SerialMovementDto(
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

            return Result.Success(new SerialTraceabilityDto(
                Map(serial),
                stock,
                movements));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serial traceability lookup failed for {SerialId}", serialId);
            return Result.Failure<SerialTraceabilityDto>(WmsErrors.FromException(
                exception,
                "serial.traceability_failed",
                "The serial traceability could not be loaded."));
        }
    }

    public async Task<Result<IEnumerable<SerialDto>>> SearchAsync(
        SerialSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            int? itemId = null;
            if (!string.IsNullOrWhiteSpace(query.ItemSku))
            {
                var item = await unitOfWork.Items.GetBySkuAsync(query.ItemSku, cancellationToken);
                if (item is null)
                {
                    return Result.Success<IEnumerable<SerialDto>>([]);
                }

                itemId = item.Id;
            }

            var serials = await unitOfWork.SerialNumbers.SearchAsync(
                itemId,
                query.Number,
                query.Status,
                query.IncludeMigrationConflicts,
                cancellationToken);
            return Result.Success<IEnumerable<SerialDto>>(serials.Select(Map).ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serial search failed");
            return Result.Failure<IEnumerable<SerialDto>>(WmsErrors.FromException(
                exception,
                "serial.search_failed",
                "The serial search could not be completed."));
        }
    }

    public async Task<Result<SerialDto>> ChangeStatusAsync(
        int serialId,
        SerialStatus status,
        string reason,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure<SerialDto>(WmsErrors.Validation(
                    "serial.status_reason_required",
                    "A reason is required for every serial status change."));
            }

            var serial = await unitOfWork.SerialNumbers.GetByIdAsync(serialId, cancellationToken);
            if (serial is null)
            {
                return Result.Failure<SerialDto>(WmsErrors.NotFound(
                    "serial.not_found",
                    $"Serial {serialId} was not found."));
            }

            var before = serial.Status;
            serial.SetStatus(status, reason, clock.UtcNow.UtcDateTime);
            await unitOfWork.SerialNumbers.UpdateAsync(serial, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.SerialStatusChanged,
                    WmsAuditEntityTypes.Serial,
                    serial.Id.ToString(CultureInfo.InvariantCulture),
                    Before: new Dictionary<string, object?> { ["status"] = before.ToString() },
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = serial.Status.ToString(),
                        ["reason"] = reason
                    },
                    ActorUserId: userId),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(serial));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serial status change failed for {SerialId}", serialId);
            return Result.Failure<SerialDto>(WmsErrors.FromException(
                exception,
                "serial.status_change_failed",
                "The serial status could not be changed."));
        }
    }

    private static SerialDto Map(SerialNumber serial) => new(
        serial.Id,
        serial.ItemId,
        serial.Item.Sku,
        serial.Item.Name,
        serial.Number,
        serial.LotId,
        serial.Lot?.Number,
        serial.CurrentWarehouseId,
        serial.CurrentWarehouse?.Code,
        serial.CurrentLocationId,
        serial.CurrentLocation?.Code,
        serial.CurrentLicensePlate,
        serial.Status,
        serial.StatusReason,
        serial.ReceiptReference,
        serial.ShipmentReference,
        serial.HasMigrationConflict,
        serial.ConflictReason,
        serial.LastMovedAt,
        serial.CreatedAt,
        serial.UpdatedAt);

    private static Dictionary<string, object?> ToMetadata(SerialNumber serial) => new()
    {
        ["itemId"] = serial.ItemId,
        ["number"] = serial.Number,
        ["lotId"] = serial.LotId,
        ["status"] = serial.Status.ToString()
    };
}
