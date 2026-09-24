using Wms.Application.Common;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.WarehouseWork;

/// <summary>
/// Executes generated replenishment work as a scanner-confirmed putaway from
/// an eligible reserve/source location into the planned pick face. The work
/// service owns the outer transaction and idempotency command.
/// </summary>
public sealed class ReplenishmentWarehouseWorkCompletionHandler(
    IStockMovementService stockMovementService,
    IUnitOfWork unitOfWork) : IWarehouseWorkCompletionHandler
{
    public WarehouseWorkType WorkType => WarehouseWorkType.Replenishment;

    public async Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
        WarehouseWorkEntity work,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var scans = input.Scans;
        if (scans is null || scans.Count == 0)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.replenishment_scans_required",
                "Replenishment completion requires one scan for every work line."));
        }

        if (scans.Count != work.Lines.Count ||
            scans.GroupBy(scan => scan.LineId).Any(group => group.Count() != 1))
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.replenishment_scan_lines_invalid",
                "Replenishment completion must scan every work line exactly once."));
        }

        var actualLines = new List<WarehouseWorkLineActualInput>(work.Lines.Count);
        var movementIds = new List<int>(work.Lines.Count);
        foreach (var line in work.Lines.OrderBy(value => value.Sequence))
        {
            var scan = scans.SingleOrDefault(value => value.LineId == line.Id);
            if (scan is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_scan_missing",
                    $"Replenishment completion is missing a scan for line {line.Id}."));
            }

            if (line.SourceLocationId is null || line.DestinationLocationId is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_locations_missing",
                    $"Replenishment line {line.Id} must have source and destination locations."));
            }

            if (scan.ItemId != line.ItemId ||
                scan.SourceLocationId != line.SourceLocationId.Value ||
                scan.LicensePlateId != line.LicensePlateId ||
                scan.LotId != line.LotId ||
                scan.SerialNumberId != line.SerialNumberId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_scan_identity_mismatch",
                    $"The scan identity does not match replenishment line {line.Id}."));
            }

            if (!string.Equals(
                    Normalize(line.SerialNumber),
                    Normalize(scan.SerialNumber),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_scan_serial_mismatch",
                    $"The scanned serial does not match replenishment line {line.Id}."));
            }

            if (scan.ActualQuantity <= 0m || scan.ActualQuantity > line.PlannedQuantity)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_quantity_invalid",
                    $"The scanned quantity for replenishment line {line.Id} must be positive and no more than planned."));
            }

            if (line.LicensePlateId.HasValue && scan.ActualQuantity != line.PlannedQuantity)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                    "work.replenishment_partial_lpn_not_supported",
                    "A license plate replenishment must move its complete planned license plate quantity."));
            }

            if (scan.DestinationLocationId != line.DestinationLocationId.Value)
            {
                if (!input.SupervisorOverride || !scan.DestinationOverride)
                {
                    return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Forbidden(
                        "work.replenishment_destination_override_required",
                        $"Changing the planned replenishment destination for line {line.Id} requires a supervisor override."));
                }
            }

            if (scan.SourceLocationId == scan.DestinationLocationId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.replenishment_destination_same_as_source",
                    $"Replenishment line {line.Id} must move to a different destination."));
            }

            var source = await unitOfWork.Locations.GetByIdAsync(
                scan.SourceLocationId,
                cancellationToken);
            var destination = await unitOfWork.Locations.GetByIdAsync(
                scan.DestinationLocationId,
                cancellationToken);
            if (source is null || destination is null ||
                !source.IsActive || !destination.IsActive ||
                source.WarehouseId != work.WarehouseId ||
                destination.WarehouseId != work.WarehouseId ||
                !destination.IsPickable)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                    "work.replenishment_location_blocked",
                    $"The source or destination for replenishment line {line.Id} is inactive, out of scope, or blocked."));
            }

            var movement = await stockMovementService.PutawayAsync(
                line.ItemId,
                scan.SourceLocationId,
                scan.DestinationLocationId,
                new Quantity(scan.ActualQuantity),
                userId,
                line.LotId,
                line.SerialNumber,
                work.WorkNumber,
                input.CompletionReference ?? "replenishment work",
                cancellationToken,
                line.LicensePlateId,
                line.OwnerKind,
                line.InventoryOwnerId,
                line.OwnerCodeSnapshot);
            if (movement.Id > 0)
            {
                movementIds.Add(movement.Id);
            }

            actualLines.Add(new WarehouseWorkLineActualInput(line.Id, scan.ActualQuantity));
        }

        return Result.Success(new WarehouseWorkHandlerResult(
            actualLines,
            "Replenishment movement completed.",
            movementIds));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
