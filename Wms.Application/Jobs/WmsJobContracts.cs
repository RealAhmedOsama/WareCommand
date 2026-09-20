using System.Globalization;
using Wms.Application.Context;

namespace Wms.Application.Jobs;

public static class WmsJobQueues
{
    public const string Default = "default";
    public const string Reports = "reports";
    public const string Integrations = "integrations";
    public const string Alerts = "alerts";
    public const string Maintenance = "maintenance";

    public static IReadOnlyList<string> All { get; } =
    [
        Default,
        Reports,
        Integrations,
        Alerts,
        Maintenance
    ];
}

public static class WmsJobNames
{
    public const string ExpiryAlerts = "wms.expiry-alerts";
    public const string LowStockAlerts = "wms.low-stock-alerts";
    public const string ReportGeneration = "wms.report-generation";
    public const string IntegrationRetries = "wms.integration-retries";
    public const string Cleanup = "wms.cleanup";
    public const string DatabaseBackup = "wms.database-backup";
    public const string CycleCountGeneration = "wms.cycle-count-generation";
    public const string ReplenishmentGeneration = "wms.replenishment-generation";
    public const string InventoryHealthCheck = "wms.inventory-health-check";
    public const string InventoryReconciliation = "wms.inventory-reconciliation";
}

public static class WmsJobScheduleTimeZones
{
    /// <summary>
    /// Hangfire cron expressions are registered in UTC. Warehouse-local
    /// business dates are calculated inside handlers and never inferred from
    /// the host's local clock.
    /// </summary>
    public const string Utc = "UTC";
}

public sealed record WmsJobDefinition(
    string Name,
    string Queue,
    string Cron,
    TimeSpan IdempotencyWindow,
    TimeSpan Timeout,
    string Description,
    string ScheduleTimeZone = WmsJobScheduleTimeZones.Utc);

public static class WmsJobCatalog
{
    private static readonly IReadOnlyList<WmsJobDefinition> Definitions =
    [
        new(
            WmsJobNames.ExpiryAlerts,
            WmsJobQueues.Alerts,
            "0 * * * *",
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(5),
            "Generate idempotent expiry and near-expiry alerts."),
        new(
            WmsJobNames.LowStockAlerts,
            WmsJobQueues.Alerts,
            "*/15 * * * *",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(5),
            "Generate idempotent low-stock alerts."),
        new(
            WmsJobNames.ReportGeneration,
            WmsJobQueues.Reports,
            "0 2 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(10),
            "Generate the scheduled movement report summary."),
        new(
            WmsJobNames.IntegrationRetries,
            WmsJobQueues.Integrations,
            "*/5 * * * *",
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
            "Retry pending integration work when an adapter is available."),
        new(
            WmsJobNames.Cleanup,
            WmsJobQueues.Maintenance,
            "30 3 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(10),
            "Prune retained job execution and notification records."),
        new(
            WmsJobNames.DatabaseBackup,
            WmsJobQueues.Maintenance,
            "0 1 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(30),
            "Create, verify, and replicate the scheduled encrypted PostgreSQL backup."),
        new(
            WmsJobNames.CycleCountGeneration,
            WmsJobQueues.Maintenance,
            "0 4 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(10),
            "Generate future cycle-count work from inventory policy."),
        new(
            WmsJobNames.ReplenishmentGeneration,
            WmsJobQueues.Maintenance,
            "30 4 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(10),
            "Generate future replenishment work from inventory policy."),
        new(
            WmsJobNames.InventoryHealthCheck,
            WmsJobQueues.Maintenance,
            "*/10 * * * *",
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5),
            "Run a lightweight read-only inventory invariant check."),
        new(
            WmsJobNames.InventoryReconciliation,
            WmsJobQueues.Maintenance,
            "0 5 * * *",
            TimeSpan.FromDays(1),
            TimeSpan.FromMinutes(30),
            "Run a deep read-only inventory reconciliation report.")
    ];

    private static readonly Dictionary<string, WmsJobDefinition> DefinitionMap =
        Definitions.ToDictionary(definition => definition.Name, StringComparer.Ordinal);

    public static IReadOnlyList<WmsJobDefinition> All => Definitions;

    public static bool TryGet(string jobName, out WmsJobDefinition definition) =>
        DefinitionMap.TryGetValue(jobName, out definition!);

    public static WmsJobDefinition Get(string jobName) =>
        TryGet(jobName, out var definition)
            ? definition
            : throw new ArgumentException($"Unknown WMS job '{jobName}'.", nameof(jobName));

    public static string GetRecurringId(string jobName) =>
        Get(jobName).Name;

    public static string GetExecutionKey(string jobName, DateTimeOffset utcNow)
    {
        var definition = Get(jobName);
        var bucketSeconds = (long)definition.IdempotencyWindow.TotalSeconds;
        var epochSeconds = utcNow.ToUnixTimeSeconds();
        var bucketStart = epochSeconds - epochSeconds % bucketSeconds;
        var bucketText = DateTimeOffset
            .FromUnixTimeSeconds(bucketStart)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return $"{definition.Name}:{bucketText}";
    }
}

public sealed record WmsJobEnvelope(
    string JobName,
    string IdempotencyKey,
    string CorrelationId,
    string? ActorUserId = null,
    string? ActorUserName = null,
    int? WarehouseId = null,
    string? ReferenceId = null)
{
    public static WmsJobEnvelope Create(
        string jobName,
        string idempotencyKey,
        string? correlationId = null,
        string? actorUserId = null,
        string? actorUserName = null,
        int? warehouseId = null,
        string? referenceId = null)
    {
        WmsJobCatalog.Get(jobName);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("A durable job idempotency key is required.", nameof(idempotencyKey));
        }

        return new WmsJobEnvelope(
            jobName,
            idempotencyKey.Trim(),
            WmsExecutionIdentifiers.Normalize(correlationId),
            NormalizeOptional(actorUserId),
            NormalizeOptional(actorUserName),
            warehouseId,
            WmsExecutionIdentifiers.NormalizeOptional(referenceId));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record WmsJobContext(
    WmsJobEnvelope Envelope,
    int Attempt,
    DateTimeOffset StartedAtUtc);

public sealed record WmsJobExecutionResult(
    int ItemsExamined = 0,
    int ItemsCreated = 0,
    string? Summary = null);

public sealed record WmsJobExecutionLease(
    long ExecutionId,
    WmsJobEnvelope Envelope,
    int Attempt,
    bool ShouldExecute,
    string? SkipReason = null);

public static class WmsJobExecutionStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
    public const string DeadLettered = "DeadLettered";
}

public sealed record WmsJobNotification(
    string DeduplicationKey,
    string Kind,
    string Severity,
    string Title,
    string Message,
    string JobName,
    string JobIdempotencyKey,
    string CorrelationId,
    int? WarehouseId = null,
    DateTimeOffset? ExpiresAtUtc = null);

public interface IWmsJobHandler
{
    string JobName { get; }

    Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default);
}

public interface IWmsJobExecutionStore
{
    Task<WmsJobExecutionLease> TryStartAsync(
        WmsJobEnvelope envelope,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        WmsJobExecutionLease lease,
        WmsJobExecutionResult result,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        WmsJobExecutionLease lease,
        Exception exception,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkCanceledAsync(
        WmsJobExecutionLease lease,
        DateTimeOffset canceledAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkDeadLetteredAsync(
        WmsJobExecutionLease lease,
        Exception exception,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default);

    Task UpsertNotificationAsync(
        WmsJobNotification notification,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<int> PruneAsync(
        DateTimeOffset completedBeforeUtc,
        DateTimeOffset notificationBeforeUtc,
        CancellationToken cancellationToken = default);
}

public sealed class WmsPermanentJobException(string message, Exception? innerException = null)
    : Exception(message, innerException);
