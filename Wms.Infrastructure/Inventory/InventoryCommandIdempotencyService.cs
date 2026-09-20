using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Idempotency;
using Wms.Application.Logging;
using Wms.Application.Telemetry;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Inventory;

public sealed class InventoryCommandIdempotencyService(
    IUnitOfWork unitOfWork,
    WmsDbContext context,
    IClock clock,
    ILogger<InventoryCommandIdempotencyService> logger)
    : IInventoryCommandIdempotencyService
{
    public static readonly TimeSpan DefaultRetentionWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaximumRetentionWindow = TimeSpan.FromDays(365);

    public async Task<Result<InventoryCommandIdempotencyDecision>> BeginAsync(
        InventoryCommandIdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (validation is not null)
        {
            return Result.Failure<InventoryCommandIdempotencyDecision>(validation);
        }

        var nowUtc = clock.UtcNow;
        var retentionWindow = request.RetentionWindow ?? DefaultRetentionWindow;
        var expiresAtUtc = nowUtc.Add(retentionWindow);
        var command = await unitOfWork.InventoryCommandIdempotencies.GetAsync(
            request.CallerScope,
            request.CommandKey,
            cancellationToken);

        if (command is null)
        {
            command = new InventoryCommandIdempotency(
                request.CommandKey,
                request.OperationType,
                request.CallerScope,
                request.RequestHash,
                request.CorrelationId,
                request.ActorUserId,
                request.WarehouseId,
                nowUtc,
                expiresAtUtc);
            await unitOfWork.InventoryCommandIdempotencies.AddAsync(command, cancellationToken);

            try
            {
                // This insert is intentionally durable before the caller performs
                // work. When the caller has an explicit transaction it remains
                // atomic with the inventory mutation; otherwise an in-progress
                // record expires and can be safely reclaimed.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Success(ToExecuteDecision(command, retentionWindow));
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                context.Entry(command).State = EntityState.Detached;
                command = await unitOfWork.InventoryCommandIdempotencies.GetAsync(
                    request.CallerScope,
                    request.CommandKey,
                    cancellationToken);
                if (command is null)
                {
                    throw;
                }

                logger.LogInformation(
                    WmsLogEvents.InventoryCommandDuplicate,
                    "Concurrent inventory command claim resolved against the durable record for operation {OperationType}",
                    request.OperationType);
                WmsTelemetry.RecordInventoryCommandDuplicate(request.OperationType, "concurrent");
            }
        }

        if (!string.Equals(command.OperationType, request.OperationType, StringComparison.Ordinal) ||
            !string.Equals(command.RequestHash, request.RequestHash, StringComparison.Ordinal))
        {
            logger.LogWarning(
                WmsLogEvents.InventoryCommandPayloadMismatch,
                "Inventory command idempotency key was reused with a different payload for operation {OperationType}",
                request.OperationType);
            WmsTelemetry.RecordInventoryCommandDuplicate(request.OperationType, "payload_mismatch");
            return Result.Failure<InventoryCommandIdempotencyDecision>(new ResultError(
                "idempotency.payload_mismatch",
                ErrorType.Conflict,
                "The idempotency key was already used for a different inventory command."));
        }

        if (command.Status == InventoryCommandIdempotencyStatus.Succeeded)
        {
            if (!command.IsReplayable)
            {
                return Result.Failure<InventoryCommandIdempotencyDecision>(WmsErrors.Unexpected(
                    "idempotency.result_unavailable",
                    "The original command result is not available for replay."));
            }

            logger.LogInformation(
                WmsLogEvents.InventoryCommandReplayed,
                "Returning the durable result for a repeated inventory command {OperationType}",
                request.OperationType);
            WmsTelemetry.RecordInventoryCommandDuplicate(request.OperationType, "replay");
            return Result.Success(new InventoryCommandIdempotencyDecision(
                ShouldExecute: false,
                ResultType: command.ResultType,
                ResultPayloadJson: command.ResultPayloadJson,
                ResultReference: command.ResultReference));
        }

        if (command.Status == InventoryCommandIdempotencyStatus.InProgress &&
            !command.IsExpired(nowUtc))
        {
            logger.LogInformation(
                WmsLogEvents.InventoryCommandInProgress,
                "An inventory command with the same idempotency key is already in progress for operation {OperationType}",
                request.OperationType);
            WmsTelemetry.RecordInventoryCommandDuplicate(request.OperationType, "in_progress");
            return Result.Failure<InventoryCommandIdempotencyDecision>(new ResultError(
                "idempotency.in_progress",
                ErrorType.Conflict,
                "The same inventory command is already in progress. Retry after it completes.",
                IsRetryable: true));
        }

        if (command.Status == InventoryCommandIdempotencyStatus.InProgress)
        {
            command.MarkExpired(nowUtc);
        }

        command.Reclaim(
            request.CorrelationId,
            request.ActorUserId,
            request.WarehouseId,
            nowUtc,
            expiresAtUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            WmsLogEvents.InventoryCommandReclaimed,
            "Reclaimed an expired or failed inventory command for operation {OperationType}",
            request.OperationType);
        return Result.Success(ToExecuteDecision(command, retentionWindow));
    }

    public async Task CompleteAsync(
        InventoryCommandIdempotencyLease lease,
        InventoryCommandIdempotencyCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(completion);
        var command = await GetLeaseAsync(lease, cancellationToken);
        var nowUtc = clock.UtcNow;
        command.Complete(
            completion.ResultType,
            completion.ResultPayloadJson,
            completion.ResultReference,
            nowUtc,
            nowUtc.Add(lease.RetentionWindow));
    }

    public async Task MarkFailedAsync(
        InventoryCommandIdempotencyLease lease,
        string failureCode,
        string? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var command = await GetLeaseAsync(lease, cancellationToken);
        var nowUtc = clock.UtcNow;
        command.MarkFailed(
            failureCode,
            failureMessage,
            nowUtc,
            nowUtc.Add(lease.RetentionWindow));
    }

    public async Task<int> PruneAsync(
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default)
    {
        var inProgress = (int)InventoryCommandIdempotencyStatus.InProgress;
        var staleCommands = await context.InventoryCommandIdempotencies
            .FromSqlInterpolated(
                $"SELECT * FROM \"InventoryCommandIdempotencies\" WHERE \"Status\" = {inProgress} AND \"ExpiresAtUtc\" <= {beforeUtc}")
            .ToListAsync(cancellationToken);
        foreach (var command in staleCommands)
        {
            command.MarkExpired(beforeUtc);
        }

        if (staleCommands.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await unitOfWork.InventoryCommandIdempotencies.PruneAsync(
            beforeUtc,
            cancellationToken);
    }

    private async Task<InventoryCommandIdempotency> GetLeaseAsync(
        InventoryCommandIdempotencyLease lease,
        CancellationToken cancellationToken)
    {
        var command = await unitOfWork.InventoryCommandIdempotencies.GetByIdAsync(
            lease.RecordId,
            cancellationToken);
        if (command is null ||
            !string.Equals(command.CommandKey, lease.CommandKey, StringComparison.Ordinal) ||
            !string.Equals(command.CallerScope, lease.CallerScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The inventory idempotency lease is no longer valid.");
        }

        return command;
    }

    private static InventoryCommandIdempotencyDecision ToExecuteDecision(
        InventoryCommandIdempotency command,
        TimeSpan retentionWindow) =>
        new(
            ShouldExecute: true,
            Lease: new InventoryCommandIdempotencyLease(
                command.Id,
                command.OperationType,
                command.CommandKey,
                command.CallerScope,
                retentionWindow));

    private static ResultError? Validate(InventoryCommandIdempotencyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OperationType) ||
            request.OperationType.Trim().Length > 100)
        {
            return WmsErrors.Validation(
                "idempotency.operation_invalid",
                "The inventory command operation type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.CommandKey) ||
            request.CommandKey.Trim().Length > 250)
        {
            return WmsErrors.Validation(
                "idempotency.key_invalid",
                "The idempotency key is required and cannot exceed 250 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.CallerScope) ||
            request.CallerScope.Trim().Length > 250)
        {
            return WmsErrors.Validation(
                "idempotency.scope_invalid",
                "The idempotency caller scope is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestHash) || request.RequestHash.Length != 64)
        {
            return WmsErrors.Validation(
                "idempotency.hash_invalid",
                "The idempotency request hash is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId) ||
            request.CorrelationId.Trim().Length > 100)
        {
            return WmsErrors.Validation(
                "idempotency.correlation_invalid",
                "The idempotency correlation ID is invalid.");
        }

        if (request.RetentionWindow.HasValue &&
            (request.RetentionWindow.Value <= TimeSpan.Zero ||
             request.RetentionWindow.Value > MaximumRetentionWindow))
        {
            return WmsErrors.Validation(
                "idempotency.retention_invalid",
                "The idempotency retention window is invalid.");
        }

        return null;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is DbException dbException &&
                dbException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (string.Equals(sqlState, "23505", StringComparison.Ordinal))
            {
                return true;
            }

            if (current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
