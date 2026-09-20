using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Telemetry;

namespace Wms.Infrastructure.Telemetry;

/// <summary>
/// Emits bounded database-latency metrics without recording SQL text or parameter values.
/// EF/OpenTelemetry spans provide the detailed trace; this interceptor provides an
/// inexpensive aggregate latency/error signal for alerting.
/// </summary>
public sealed class WmsDbCommandMetricsInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, (long StartedAt, string Kind)> _starts = new();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Start(eventData.CommandId, "reader");
        return base.ReaderExecuting(command, eventData, result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Start(eventData.CommandId, "scalar");
        return base.ScalarExecuting(command, eventData, result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Start(eventData.CommandId, "non_query");
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Start(eventData.CommandId, "reader");
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Start(eventData.CommandId, "scalar");
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Start(eventData.CommandId, "non_query");
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Complete(eventData.CommandId, succeeded: true);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Complete(eventData.CommandId, succeeded: false);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Complete(eventData.CommandId, succeeded: false);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData)
    {
        Complete(eventData.CommandId, succeeded: false);
        base.CommandCanceled(command, eventData);
    }

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Complete(eventData.CommandId, succeeded: false);
        return base.CommandCanceledAsync(command, eventData, cancellationToken);
    }

    private void Start(Guid commandId, string kind) =>
        _starts[commandId] = (Stopwatch.GetTimestamp(), kind);

    private void Complete(Guid commandId, bool succeeded)
    {
        if (!_starts.TryRemove(commandId, out var start))
        {
            return;
        }

        WmsTelemetry.RecordDatabaseCommand(
            start.Kind,
            Stopwatch.GetElapsedTime(start.StartedAt).TotalMilliseconds,
            succeeded);
    }
}
