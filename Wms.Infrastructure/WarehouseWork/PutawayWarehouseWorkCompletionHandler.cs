using Wms.Application.Common;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.WarehouseWork;

public sealed class PutawayWarehouseWorkCompletionHandler(
    IStockMovementService stockMovementService,
    IUnitOfWork unitOfWork) : IWarehouseWorkCompletionHandler
{
    public WarehouseWorkType WorkType => WarehouseWorkType.Putaway;

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
                "work.scans_required",
                "Putaway completion requires a scan for every work line."));
        }

        if (scans.GroupBy(scan => scan.LineId).Any(group => group.Count() != 1))
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.scan_lines_duplicate",
                "Putaway completion cannot contain duplicate line scans."));
        }

        if (scans.Count != work.Lines.Count)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "work.scan_lines_incomplete",
                "Putaway completion must scan every work line exactly once."));
        }

        var actualLines = new List<WarehouseWorkLineActualInput>(work.Lines.Count);
        var movementIds = new List<int>(work.Lines.Count);
        foreach (var line in work.Lines.OrderBy(value => value.Sequence))
        {
            var scan = scans.SingleOrDefault(value => value.LineId == line.Id);
            if (scan is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.scan_line_missing",
                    $"Putaway completion is missing a scan for line {line.Id}."));
            }

            if (line.SourceLocationId is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.source_location_missing",
                    $"Putaway line {line.Id} has no source location."));
            }

            if (scan.ItemId != line.ItemId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.scan_item_mismatch",
                    $"The scanned item does not match putaway line {line.Id}."));
            }

            if (scan.SourceLocationId != line.SourceLocationId.Value)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.scan_source_mismatch",
                    $"The scanned source location does not match putaway line {line.Id}."));
            }

            if (scan.LicensePlateId != line.LicensePlateId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.scan_license_plate_mismatch",
                    $"The scanned license plate does not match putaway line {line.Id}."));
            }

            if (scan.ActualQuantity <= 0 || scan.ActualQuantity > line.PlannedQuantity)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.scan_quantity_invalid",
                    $"The scanned quantity for putaway line {line.Id} must be greater than zero and no more than the planned quantity."));
            }

            if (line.LicensePlateId.HasValue && scan.ActualQuantity != line.PlannedQuantity)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.BusinessRule(
                    "work.partial_lpn_not_supported",
                    "A license plate putaway must move the complete license plate in this execution slice."));
            }

            if (line.DestinationLocationId.HasValue &&
                scan.DestinationLocationId != line.DestinationLocationId.Value)
            {
                if (!input.SupervisorOverride || !scan.DestinationOverride)
                {
                    return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Forbidden(
                        "work.destination_override_required",
                        $"Changing the suggested destination for line {line.Id} requires a supervisor override and an explicit override scan."));
                }
            }

            if (scan.SourceLocationId == scan.DestinationLocationId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.destination_same_as_source",
                    $"Putaway line {line.Id} must move to a different destination location."));
            }

            var sourceLocation = await unitOfWork.Locations.GetByIdAsync(
                scan.SourceLocationId,
                cancellationToken);
            var destinationLocation = await unitOfWork.Locations.GetByIdAsync(
                scan.DestinationLocationId,
                cancellationToken);
            if (sourceLocation is null || destinationLocation is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.NotFound(
                    "work.location_not_found",
                    $"The source or destination location for putaway line {line.Id} was not found."));
            }

            if (sourceLocation.WarehouseId != work.WarehouseId ||
                destinationLocation.WarehouseId != work.WarehouseId)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "work.location_warehouse_mismatch",
                    $"The source and destination for putaway line {line.Id} must belong to the work warehouse."));
            }

            var movement = await stockMovementService.PutawayAsync(
                line.ItemId,
                sourceLocation.Id,
                destinationLocation.Id,
                new Quantity(scan.ActualQuantity),
                userId,
                line.LotId,
                line.SerialNumber,
                work.WorkNumber,
                input.CompletionReference,
                cancellationToken,
                line.LicensePlateId);
            if (movement.Id > 0)
            {
                movementIds.Add(movement.Id);
            }

            actualLines.Add(new WarehouseWorkLineActualInput(line.Id, scan.ActualQuantity));
        }

        return Result.Success(new WarehouseWorkHandlerResult(
            actualLines,
            "Putaway movement completed.",
            movementIds));
    }
}
