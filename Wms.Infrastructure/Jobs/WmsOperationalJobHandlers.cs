using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Backups;
using Wms.Application.Context;
using Wms.Application.Jobs;
using Wms.Application.Settings;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Jobs;

public sealed class WmsJobHandlerCatalog(
    WmsExpiryAlertJob expiryAlertJob,
    WmsLowStockAlertJob lowStockAlertJob,
    WmsReportGenerationJob reportGenerationJob,
    WmsIntegrationRetryJob integrationRetryJob,
    WmsCleanupJob cleanupJob,
    WmsDatabaseBackupJob databaseBackupJob,
    WmsCycleCountGenerationJob cycleCountGenerationJob,
    WmsReplenishmentGenerationJob replenishmentGenerationJob)
{
    private readonly Dictionary<string, IWmsJobHandler> _handlers =
        new IWmsJobHandler[]
        {
            expiryAlertJob,
            lowStockAlertJob,
            reportGenerationJob,
            integrationRetryJob,
            cleanupJob,
            databaseBackupJob,
            cycleCountGenerationJob,
            replenishmentGenerationJob
        }.ToDictionary(handler => handler.JobName, StringComparer.Ordinal);

    public IWmsJobHandler Resolve(string jobName) =>
        _handlers.TryGetValue(jobName, out var handler)
            ? handler
            : throw new WmsPermanentJobException($"No handler is registered for job '{jobName}'.");
}

public sealed class WmsExpiryAlertJob(
    WmsDbContext dbContext,
    IWmsSettingsService settingsService,
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsExpiryAlertJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.ExpiryAlerts;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
        var values = settings.IsSuccess
            ? settings.Value.Values
            : WmsSettingsDefaults.Create();
        var today = clock.UtcNow.UtcDateTime.Date;
        var warningDate = today.AddDays(values.Expiry.WarningDays);
        var lots = await dbContext.Lots
            .AsNoTracking()
            .Include(lot => lot.Item)
            .Where(lot => lot.IsActive &&
                lot.ExpiryDate.HasValue &&
                lot.ExpiryDate.Value.Date <= warningDate)
            .OrderBy(lot => lot.ExpiryDate)
            .ToListAsync(cancellationToken);

        foreach (var lot in lots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expiryDate = lot.ExpiryDate!.Value.Date;
            var expired = expiryDate < today;
            var kind = expired ? "expiry.expired" : "expiry.warning";
            await executionStore.UpsertNotificationAsync(
                new WmsJobNotification(
                    $"{kind}:{lot.Id}:{today:yyyyMMdd}",
                    kind,
                    expired ? "critical" : "warning",
                    expired ? "Expired lot" : "Lot expiry warning",
                    $"Item {lot.Item.Sku} lot {lot.Number} expires on {expiryDate:yyyy-MM-dd}.",
                    JobName,
                    context.Envelope.IdempotencyKey,
                    context.Envelope.CorrelationId,
                    ExpiresAtUtc: clock.UtcNow.AddDays(7)),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Expiry alert job evaluated {LotCount} lots and persisted idempotent notifications",
            lots.Count);
        return new WmsJobExecutionResult(lots.Count, lots.Count, $"Evaluated {lots.Count} lots.");
    }
}

public sealed class WmsLowStockAlertJob(
    WmsDbContext dbContext,
    IWmsSettingsService settingsService,
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsLowStockAlertJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.LowStockAlerts;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
        var dashboard = settings.IsSuccess
            ? settings.Value.Values.Dashboard
            : WmsSettingsDefaults.Create().Dashboard;
        var stocks = await dbContext.Stock
            .AsNoTracking()
            .Include(stock => stock.Item)
            .Include(stock => stock.Location)
            .Where(stock => stock.QuantityAvailable.Value - stock.QuantityReserved.Value <
                dashboard.LowStockThreshold)
            .OrderBy(stock => stock.QuantityAvailable.Value - stock.QuantityReserved.Value)
            .Take(dashboard.LowStockAlertLimit)
            .ToListAsync(cancellationToken);

        var today = clock.UtcNow.UtcDateTime.Date;
        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var available = stock.QuantityAvailable.Value - stock.QuantityReserved.Value;
            await executionStore.UpsertNotificationAsync(
                new WmsJobNotification(
                    $"low-stock:{stock.Id}:{today:yyyyMMdd}",
                    "inventory.low-stock",
                    "warning",
                    "Low stock alert",
                    $"Item {stock.Item.Sku} at {stock.Location.Code} has {available:0.####} available units.",
                    JobName,
                    context.Envelope.IdempotencyKey,
                    context.Envelope.CorrelationId,
                    stock.Location.WarehouseId,
                    clock.UtcNow.AddDays(1)),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Low-stock alert job evaluated {StockCount} stock rows",
            stocks.Count);
        return new WmsJobExecutionResult(stocks.Count, stocks.Count, $"Evaluated {stocks.Count} stock rows.");
    }
}

public sealed class WmsReportGenerationJob(
    WmsDbContext dbContext,
    IWmsSettingsService settingsService,
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsReportGenerationJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.ReportGeneration;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
        var reportSettings = settings.IsSuccess
            ? settings.Value.Values.Reports
            : WmsSettingsDefaults.Create().Reports;
        var now = clock.UtcNow;
        var from = now.UtcDateTime.AddDays(-reportSettings.DefaultPeriodDays);
        var movements = await dbContext.Movements
            .AsNoTracking()
            .Where(movement => movement.Timestamp >= from && movement.Timestamp <= now.UtcDateTime)
            .Take(reportSettings.MaximumRows)
            .ToListAsync(cancellationToken);
        var quantity = movements.Sum(movement => movement.Quantity.Value);
        await executionStore.UpsertNotificationAsync(
            new WmsJobNotification(
                $"report.ready:{context.Envelope.IdempotencyKey}",
                "report.ready",
                "info",
                "Scheduled movement report ready",
                $"The scheduled movement report contains {movements.Count} movements and {quantity:0.####} total units.",
                JobName,
                context.Envelope.IdempotencyKey,
                context.Envelope.CorrelationId,
                ExpiresAtUtc: now.AddDays(30)),
            now,
            cancellationToken);

        logger.LogInformation(
            "Scheduled movement report generated with {MovementCount} rows",
            movements.Count);
        return new WmsJobExecutionResult(
            movements.Count,
            1,
            $"Generated a report with {movements.Count} movement rows.");
    }
}

public sealed class WmsIntegrationRetryJob(
    ILogger<WmsIntegrationRetryJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.IntegrationRetries;

    public Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Integration retry job completed with no registered integration outbox adapter");
        return Task.FromResult(new WmsJobExecutionResult(
            Summary: "No integration outbox adapter is registered yet; the durable queue is ready for future adapters."));
    }
}

public sealed class WmsCleanupJob(
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsCleanupJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.Cleanup;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var now = clock.UtcNow;
        var removed = await executionStore.PruneAsync(
            now.AddDays(-90),
            now.AddDays(-90),
            cancellationToken);
        logger.LogInformation("Background-job cleanup pruned {RecordCount} records", removed);
        return new WmsJobExecutionResult(removed, removed, $"Pruned {removed} records.");
    }
}

public sealed class WmsDatabaseBackupJob(
    IWmsBackupService backupService,
    WmsBackupOptions options,
    ILogger<WmsDatabaseBackupJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.DatabaseBackup;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!options.Enabled)
        {
            return new WmsJobExecutionResult(Summary: "Automated backups are disabled for this host.");
        }

        var artifact = await backupService.CreateAsync(
            Environment.GetEnvironmentVariable("WARECOMMAND_BUILD_REVISION"),
            cancellationToken);
        logger.LogInformation(
            "Database backup created at {ArtifactPath} with {SizeBytes} bytes",
            artifact.ArtifactPath,
            artifact.SizeBytes);
        return new WmsJobExecutionResult(
            Summary: $"Created and replicated backup {Path.GetFileName(artifact.ArtifactPath)}.");
    }
}

public sealed class WmsCycleCountGenerationJob(ILogger<WmsCycleCountGenerationJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.CycleCountGeneration;

    public Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Cycle-count generation job found no cycle-count policy model in the current domain");
        return Task.FromResult(new WmsJobExecutionResult(
            Summary: "No cycle-count policy model is registered; no work was generated."));
    }
}

public sealed class WmsReplenishmentGenerationJob(ILogger<WmsReplenishmentGenerationJob> logger)
    : IWmsJobHandler
{
    public string JobName => WmsJobNames.ReplenishmentGeneration;

    public Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation(
            "Replenishment generation job found no replenishment policy model in the current domain");
        return Task.FromResult(new WmsJobExecutionResult(
            Summary: "No replenishment policy model is registered; no work was generated."));
    }
}
