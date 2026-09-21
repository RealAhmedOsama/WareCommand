using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.WarehouseWork;

public sealed class WarehouseWorkService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    IEnumerable<IWarehouseWorkCompletionHandler> completionHandlers,
    ILogger<WarehouseWorkService> logger) : IWarehouseWorkService
{
    private readonly IReadOnlyList<IWarehouseWorkCompletionHandler> _completionHandlers =
        completionHandlers.ToArray();

    public async Task<Result<WarehouseWorkDto>> CreateAsync(
        WarehouseWorkInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseWorkDto>();
        }

        try
        {
            var existing = await context.WarehouseWorks
                .Include(work => work.Lines)
                .SingleOrDefaultAsync(
                    work => work.WarehouseId == input.WarehouseId &&
                            work.CreationKey == input.CreationKey.Trim(),
                    cancellationToken);
            if (existing is not null)
            {
                return Result.Success(Map(existing));
            }

            var lines = input.Lines?.Where(line => line is not null).ToArray() ?? [];
            if (lines.Length == 0)
            {
                return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                    "work.lines_required",
                    "Warehouse work requires at least one line."));
            }

            if (lines.Any(line => line.WarehouseId != input.WarehouseId))
            {
                return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                    "work.warehouse_mismatch",
                    "All work lines must belong to the work warehouse."));
            }

            var work = new WarehouseWorkEntity(
                $"WORK-{input.WarehouseId}-{Guid.NewGuid():N}",
                input.CreationKey,
                input.Type,
                input.WarehouseId,
                input.SourceEntityType,
                input.SourceEntityId,
                input.Priority,
                input.SourceLineReference,
                input.QueueCode,
                input.DueAtUtc,
                input.TeamCode,
                input.Notes);
            foreach (var line in lines)
            {
                work.AddLine(new WarehouseWorkLine(
                    line.Sequence,
                    line.WarehouseId,
                    line.ItemId,
                    line.PlannedQuantity,
                    line.BaseUnitOfMeasure,
                    line.SourceLocationId,
                    line.DestinationLocationId,
                    line.LotId,
                    line.SerialNumberId,
                    line.SerialNumber,
                    line.LicensePlateId,
                    line.InventoryStatusId,
                    line.SourceReference,
                    line.DimensionsSnapshot));
            }

            if (input.MakeAvailable)
            {
                work.MakeAvailable(clock.UtcNow.UtcDateTime);
            }

            context.WarehouseWorks.Add(work);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseWorkCreated,
                    WmsAuditEntityTypes.WarehouseWork,
                    work.WorkNumber,
                    work.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["type"] = work.Type.ToString(),
                        ["status"] = work.Status.ToString(),
                        ["lineCount"] = work.Lines.Count,
                        ["creationKey"] = work.CreationKey
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(work));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                "work.invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.BusinessRule(
                "work.invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Warehouse work creation failed for {CreationKey}", input.CreationKey);
            return Result.Failure<WarehouseWorkDto>(WmsErrors.FromException(
                exception,
                "work.create_failed",
                "Warehouse work could not be created."));
        }
    }

    public async Task<Result<WarehouseWorkPageDto>> ListAsync(
        WarehouseWorkQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WarehouseWorkPageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var workQuery = context.WarehouseWorks
            .AsNoTracking()
            .Include(work => work.Lines)
            .AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            workQuery = workQuery.Where(work => scope.WarehouseIds.Contains(work.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            workQuery = workQuery.Where(work => work.WarehouseId == query.WarehouseId.Value);
        }

        if (query.Type.HasValue)
        {
            workQuery = workQuery.Where(work => work.Type == query.Type.Value);
        }

        if (query.Status.HasValue)
        {
            workQuery = workQuery.Where(work => work.Status == query.Status.Value);
        }
        else if (!query.IncludeTerminal)
        {
            workQuery = workQuery.Where(work => work.Status != WarehouseWorkStatus.Completed &&
                                                 work.Status != WarehouseWorkStatus.Cancelled);
        }

        if (!string.IsNullOrWhiteSpace(query.AssignedUserId))
        {
            var assignedUserId = query.AssignedUserId.Trim();
            workQuery = workQuery.Where(work => work.AssignedUserId == assignedUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.QueueCode))
        {
            var queueCode = query.QueueCode.Trim().ToUpperInvariant();
            workQuery = workQuery.Where(work => work.QueueCode == queueCode);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var totalCount = await workQuery.CountAsync(cancellationToken);
        var work = await workQuery
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.DueAtUtc)
            .ThenBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Result.Success(new WarehouseWorkPageDto(
            work.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<WarehouseWorkDto>> GetAsync(
        int workId,
        CancellationToken cancellationToken = default)
    {
        var work = await LoadAsync(workId, cancellationToken);
        if (work is null)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.NotFound(
                "work.not_found",
                "The warehouse work item was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            work.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<WarehouseWorkDto>()
            : Result.Success(Map(work));
    }

    public Task<Result<WarehouseWorkDto>> AssignAsync(
        int workId,
        WarehouseWorkAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkManage,
            "assign",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkAssigned,
            work =>
            {
                if (work.Status == WarehouseWorkStatus.Assigned &&
                    !string.Equals(work.AssignedUserId, input.UserId?.Trim(), StringComparison.Ordinal) &&
                    !input.SupervisorOverride)
                {
                    return Result.Failure(WmsErrors.Forbidden(
                        "work.reassignment_forbidden",
                        "Reassigning assigned work requires supervisor override permission."));
                }

                if (work.Status == WarehouseWorkStatus.Assigned)
                {
                    work.Release(userId, clock.UtcNow.UtcDateTime, input.OverrideReason);
                }

                work.Assign(
                    input.UserId,
                    input.TeamCode,
                    userId,
                    clock.UtcNow.UtcDateTime,
                    input.SupervisorOverride,
                    input.OverrideReason);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> ReleaseAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkManage,
            "release",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkReleased,
            work =>
            {
                work.Release(userId, clock.UtcNow.UtcDateTime, input.Reason);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> StartAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkExecute,
            "start",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkStarted,
            work =>
            {
                work.Start(userId, clock.UtcNow.UtcDateTime);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> PauseAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkExecute,
            "pause",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkPaused,
            work =>
            {
                work.Pause(userId, clock.UtcNow.UtcDateTime, input.Reason);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> ResumeAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkExecute,
            "resume",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkResumed,
            work =>
            {
                work.Resume(userId, clock.UtcNow.UtcDateTime);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> RecordExceptionAsync(
        int workId,
        WarehouseWorkExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkExecute,
            "exception",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkException,
            work =>
            {
                work.RecordException(
                    input.ExceptionType,
                    input.Reason,
                    userId,
                    clock.UtcNow.UtcDateTime);
                return Result.Success();
            },
            cancellationToken);

    public Task<Result<WarehouseWorkDto>> CancelAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            workId,
            WmsPermissions.WorkManage,
            "cancel",
            input.IdempotencyKey,
            input,
            userId,
            WmsAuditActions.WarehouseWorkCancelled,
            work =>
            {
                work.Cancel(userId, input.Reason ?? "Cancelled by operator", clock.UtcNow.UtcDateTime);
                return Result.Success();
            },
            cancellationToken);

    public async Task<Result<WarehouseWorkDto>> CompleteAsync(
        int workId,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadForMutationAsync(workId, WmsPermissions.WorkExecute, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<WarehouseWorkDto>();
        }

        if (input.SupervisorOverride)
        {
            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.WorkOverride,
                loaded.Value.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<WarehouseWorkDto>();
            }
        }

        var requestHash = Hash(input);
        var replay = await TryReplayAsync(
            loaded.Value,
            "complete",
            input.IdempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var handler = _completionHandlers.SingleOrDefault(value => value.WorkType == loaded.Value.Type);
        if (handler is null)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Dependency(
                "work.handler_missing",
                $"No completion handler is registered for {loaded.Value.Type} work.",
                isRetryable: false));
        }

        try
        {
            var execution = await handler.ExecuteAsync(
                loaded.Value,
                input,
                userId,
                cancellationToken);
            if (execution.IsFailure)
            {
                return execution.ToFailure<WarehouseWorkDto>();
            }

            var actualLines = execution.Value.ActualLines;
            var actualById = actualLines
                .GroupBy(line => line.LineId)
                .ToDictionary(group => group.Key, group => group.Single().ActualQuantity);
            foreach (var line in loaded.Value.Lines)
            {
                if (!actualById.TryGetValue(line.Id, out var actualQuantity))
                {
                    return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                        "work.actual_lines_incomplete",
                        $"Completion did not return actual quantity for line {line.Id}."));
                }

                line.RecordActualQuantity(actualQuantity);
            }

            loaded.Value.Complete(
                userId,
                clock.UtcNow.UtcDateTime,
                input.SupervisorOverride,
                input.OverrideReason);
            context.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
                loaded.Value.Id,
                "complete",
                input.IdempotencyKey,
                requestHash,
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseWorkCompleted,
                    WmsAuditEntityTypes.WarehouseWork,
                    loaded.Value.WorkNumber,
                    loaded.Value.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = loaded.Value.Status.ToString(),
                        ["summary"] = execution.Value.Summary,
                        ["movementIds"] = execution.Value.MovementIds,
                        ["supervisorOverride"] = input.SupervisorOverride
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(loaded.Value));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Concurrency(
                "work.concurrency_conflict",
                "The work item changed while it was being completed. Reload it and retry."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                "work.completion_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.BusinessRule(
                "work.completion_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Warehouse work completion failed for {WorkId}", workId);
            return Result.Failure<WarehouseWorkDto>(WmsErrors.FromException(
                exception,
                "work.complete_failed",
                "Warehouse work could not be completed."));
        }
    }

    private async Task<Result<WarehouseWorkDto>> MutateAsync<TInput>(
        int workId,
        string permission,
        string operation,
        string idempotencyKey,
        TInput input,
        string userId,
        string auditAction,
        Func<WarehouseWorkEntity, Result> mutation,
        CancellationToken cancellationToken = default)
    {
        var loaded = await LoadForMutationAsync(workId, permission, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.ToFailure<WarehouseWorkDto>();
        }

        if (typeof(TInput) == typeof(WarehouseWorkAssignmentInput) &&
            ((WarehouseWorkAssignmentInput)(object)input!).SupervisorOverride)
        {
            var overrideAuthorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.WorkOverride,
                loaded.Value.WarehouseId,
                cancellationToken);
            if (overrideAuthorization.IsFailure)
            {
                return overrideAuthorization.ToFailure<WarehouseWorkDto>();
            }
        }

        var requestHash = Hash(input);
        var replay = await TryReplayAsync(
            loaded.Value,
            operation,
            idempotencyKey,
            requestHash,
            cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        try
        {
            var result = mutation(loaded.Value);
            if (result.IsFailure)
            {
                return result.ToFailure<WarehouseWorkDto>();
            }

            context.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
                loaded.Value.Id,
                operation,
                idempotencyKey,
                requestHash,
                userId,
                clock.UtcNow.UtcDateTime));
            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.WarehouseWork,
                    loaded.Value.WorkNumber,
                    loaded.Value.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["operation"] = operation,
                        ["status"] = loaded.Value.Status.ToString(),
                        ["idempotencyKey"] = idempotencyKey
                    },
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(loaded.Value));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Concurrency(
                "work.concurrency_conflict",
                "The work item changed while the command was executing. Reload it and retry."));
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.FromException(
                exception,
                "work.command_conflict",
                "The work command conflicts with another execution."));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                "work.command_invalid",
                exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.BusinessRule(
                "work.command_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Warehouse work {Operation} failed for {WorkId}", operation, workId);
            return Result.Failure<WarehouseWorkDto>(WmsErrors.FromException(
                exception,
                "work.command_failed",
                "The warehouse work command could not be completed."));
        }
    }

    private async Task<Result<WarehouseWorkEntity>> LoadForMutationAsync(
        int workId,
        string permission,
        CancellationToken cancellationToken)
    {
        var work = await LoadAsync(workId, cancellationToken);
        if (work is null)
        {
            return Result.Failure<WarehouseWorkEntity>(WmsErrors.NotFound(
                "work.not_found",
                "The warehouse work item was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            work.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<WarehouseWorkEntity>()
            : Result.Success(work);
    }

    private async Task<WarehouseWorkEntity?> LoadAsync(int workId, CancellationToken cancellationToken) =>
        await context.WarehouseWorks
            .Include(work => work.Lines)
            .SingleOrDefaultAsync(work => work.Id == workId, cancellationToken);

    private async Task<Result<WarehouseWorkDto>?> TryReplayAsync(
        WarehouseWorkEntity work,
        string operation,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result.Failure<WarehouseWorkDto>(WmsErrors.Validation(
                "work.idempotency_required",
                "Every warehouse work command requires an idempotency key."));
        }

        var command = await context.WarehouseWorkCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.WarehouseWorkId == work.Id &&
                                           value.Operation == operation &&
                                           value.IdempotencyKey == idempotencyKey.Trim(),
                cancellationToken);
        if (command is null)
        {
            return null;
        }

        return command.RequestHash == requestHash
            ? Result.Success(Map(work))
            : Result.Failure<WarehouseWorkDto>(WmsErrors.Conflict(
                "work.idempotency_reuse",
                "The idempotency key was already used with a different request."));
    }

    private static string Hash<T>(T value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static WarehouseWorkDto Map(WarehouseWorkEntity work) => new(
        work.Id,
        work.WorkNumber,
        work.CreationKey,
        work.Type,
        work.WarehouseId,
        work.SourceEntityType,
        work.SourceEntityId,
        work.SourceLineReference,
        work.QueueCode,
        work.Priority,
        work.DueAtUtc,
        work.TeamCode,
        work.Notes,
        work.Status,
        work.AssignedUserId,
        work.AssignedTeamCode,
        work.AssignedByUserId,
        work.AssignedAtUtc,
        work.StartedAtUtc,
        work.PausedAtUtc,
        work.CompletedAtUtc,
        work.CancelledAtUtc,
        work.CompletedByUserId,
        work.CancelledByUserId,
        work.CancellationReason,
        work.ExceptionType,
        work.ExceptionReason,
        work.ExceptionAtUtc,
        work.SupervisorOverride,
        work.OverrideReason,
        work.Revision,
        work.Lines.OrderBy(line => line.Sequence).Select(Map).ToArray(),
        work.IsTerminal,
        work.HasExceptions);

    private static WarehouseWorkLineDto Map(WarehouseWorkLine line) => new(
        line.Id,
        line.Sequence,
        line.WarehouseId,
        line.ItemId,
        line.PlannedQuantity,
        line.ActualQuantity,
        line.BaseUnitOfMeasure,
        line.SourceLocationId,
        line.DestinationLocationId,
        line.LotId,
        line.SerialNumberId,
        line.SerialNumber,
        line.LicensePlateId,
        line.InventoryStatusId,
        line.SourceReference,
        line.DimensionsSnapshot,
        line.Revision);
}
