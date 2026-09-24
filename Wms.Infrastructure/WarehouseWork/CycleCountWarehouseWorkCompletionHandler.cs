using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.WarehouseWork;

public sealed class CycleCountWarehouseWorkCompletionHandler(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    ICycleCountService cycleCountService) : IWarehouseWorkCompletionHandler
{
    public WarehouseWorkType WorkType => WarehouseWorkType.Count;

    public async Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
        WarehouseWorkEntity work,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(work.SourceEntityType, "CycleCountTask", StringComparison.Ordinal) ||
            !int.TryParse(work.SourceEntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var taskId))
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "cycle_count.work_source_invalid",
                "Count work must reference a cycle-count task."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.CountingExecute,
            work.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseWorkHandlerResult>();
        }

        var task = await context.CycleCountTasks
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.Id == taskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.NotFound(
                "cycle_count.task_not_found",
                "The requested cycle-count task was not found."));
        }

        if (task.WarehouseWorkId != work.Id || task.WarehouseId != work.WarehouseId)
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Conflict(
                "cycle_count.work_link_mismatch",
                "The count work item does not match its linked cycle-count task."));
        }

        if (input.Scans is null || input.Scans.Count != work.Lines.Count ||
            input.Scans.GroupBy(scan => scan.LineId).Any(group => group.Count() != 1))
        {
            return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                "cycle_count.scans_incomplete",
                "Count completion requires exactly one scan for every work line."));
        }

        var taskLinesById = task.Lines.ToDictionary(line => line.Id);
        var counts = new List<CycleCountLineCountInput>(work.Lines.Count);
        var actualLines = new List<WarehouseWorkLineActualInput>(work.Lines.Count);
        foreach (var workLine in work.Lines.OrderBy(line => line.Sequence))
        {
            var scan = input.Scans.SingleOrDefault(value => value.LineId == workLine.Id);
            if (scan is null)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "cycle_count.scan_missing",
                    $"Count completion is missing a scan for work line {workLine.Id}."));
            }

            if (!TryGetCountLineId(workLine.SourceReference, out var countLineId) ||
                !taskLinesById.TryGetValue(countLineId, out var countLine) ||
                countLine.Sequence != workLine.Sequence)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Conflict(
                    "cycle_count.work_line_link_mismatch",
                    $"Work line {workLine.Id} does not map to a count line on this task."));
            }

            if (workLine.SourceLocationId is null || workLine.DestinationLocationId is null ||
                workLine.SourceLocationId != workLine.DestinationLocationId ||
                scan.ItemId != workLine.ItemId ||
                scan.SourceLocationId != workLine.SourceLocationId.Value ||
                scan.DestinationLocationId != workLine.DestinationLocationId.Value ||
                scan.LotId != workLine.LotId ||
                scan.SerialNumberId != workLine.SerialNumberId ||
                !string.Equals(scan.SerialNumber, workLine.SerialNumber, StringComparison.Ordinal) ||
                scan.LicensePlateId != workLine.LicensePlateId ||
                workLine.ItemId != countLine.ItemId ||
                workLine.SourceLocationId != countLine.LocationId ||
                workLine.LotId != countLine.LotId ||
                workLine.SerialNumberId != countLine.SerialNumberId ||
                !string.Equals(workLine.SerialNumber, countLine.SerialNumber, StringComparison.Ordinal) ||
                workLine.LicensePlateId != countLine.LicensePlateId ||
                workLine.InventoryStatusId != countLine.InventoryStatusId ||
                workLine.OwnerKind != countLine.OwnerKind ||
                workLine.InventoryOwnerId != countLine.InventoryOwnerId ||
                !string.Equals(workLine.OwnerCodeSnapshot, countLine.OwnerCodeSnapshot, StringComparison.Ordinal))
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "cycle_count.scan_dimension_mismatch",
                    $"The scanned item or inventory dimension does not match count line {countLine.Sequence}."));
            }

            if (scan.ActualQuantity < 0m)
            {
                return Result.Failure<WarehouseWorkHandlerResult>(WmsErrors.Validation(
                    "cycle_count.quantity_negative",
                    $"The physical quantity for count line {countLine.Sequence} cannot be negative."));
            }

            counts.Add(new CycleCountLineCountInput(countLine.Id, scan.ActualQuantity));
            actualLines.Add(new WarehouseWorkLineActualInput(workLine.Id, scan.ActualQuantity));
        }

        var recordResult = await cycleCountService.RecordCountsAsync(
            taskId,
            counts,
            userId,
            cancellationToken);
        if (recordResult.IsFailure)
        {
            return recordResult.ToFailure<WarehouseWorkHandlerResult>();
        }

        return Result.Success(new WarehouseWorkHandlerResult(
            actualLines,
            recordResult.Value.Status == CycleCountTaskStatus.AwaitingApproval
                ? "Count submitted for variance approval."
                : "Cycle count completed with no variance."));
    }

    private static bool TryGetCountLineId(string? sourceReference, out int lineId)
    {
        const string Prefix = "cycle-count-line:";
        lineId = 0;
        return sourceReference is not null &&
               sourceReference.StartsWith(Prefix, StringComparison.Ordinal) &&
               int.TryParse(sourceReference.AsSpan(Prefix.Length), NumberStyles.None,
                   CultureInfo.InvariantCulture, out lineId) &&
               lineId > 0;
    }
}
