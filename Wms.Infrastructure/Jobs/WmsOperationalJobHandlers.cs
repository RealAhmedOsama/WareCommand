using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Backups;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Idempotency;
using Wms.Application.Integrations;
using Wms.Application.Inventory;
using Wms.Application.Jobs;
using Wms.Application.Lots;
using Wms.Application.Notifications;
using Wms.Application.Outbound;
using Wms.Application.Retention;
using Wms.Application.Settings;
using Wms.Application.Time;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Forecasting;

namespace Wms.Infrastructure.Jobs;

public sealed class WmsJobHandlerCatalog(
    WmsExpiryAlertJob expiryAlertJob,
    WmsLowStockAlertJob lowStockAlertJob,
    WmsReportGenerationJob reportGenerationJob,
    WmsIntegrationRetryJob integrationRetryJob,
    WmsCleanupJob cleanupJob,
    WmsDatabaseBackupJob databaseBackupJob,
    WmsInventoryClassificationRecalculationJob inventoryClassificationRecalculationJob,
    WmsSlottingAnalysisJob slottingAnalysisJob,
    WmsCycleCountGenerationJob cycleCountGenerationJob,
    WmsReplenishmentGenerationJob replenishmentGenerationJob,
    WmsWavePlanningJob wavePlanningJob,
    WmsInventoryHealthCheckJob inventoryHealthCheckJob,
    WmsInventoryReconciliationJob inventoryReconciliationJob,
    WmsForecastRecalculationJob forecastRecalculationJob)
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
            inventoryClassificationRecalculationJob,
            slottingAnalysisJob,
            cycleCountGenerationJob,
            replenishmentGenerationJob,
            wavePlanningJob,
            inventoryHealthCheckJob,
            inventoryReconciliationJob,
            forecastRecalculationJob
        }.ToDictionary(handler => handler.JobName, StringComparer.Ordinal);

    public IWmsJobHandler Resolve(string jobName) =>
        _handlers.TryGetValue(jobName, out var handler)
            ? handler
            : throw new WmsPermanentJobException($"No handler is registered for job '{jobName}'.");
}

public sealed class WmsForecastRecalculationJob(
    IForecastingService forecastingService) : IWmsJobHandler
{
    public string JobName => WmsJobNames.ForecastRecalculation;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var granularity = ForecastGranularity.Weekly;
        var horizonPeriods = 26;
        int? itemId = null;
        if (!string.IsNullOrWhiteSpace(context.Envelope.ReferenceId))
        {
            var parts = context.Envelope.ReferenceId.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 ||
                !Enum.TryParse(parts[0], ignoreCase: false, out granularity) ||
                !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out horizonPeriods))
            {
                throw new WmsPermanentJobException("The forecast recalculation request is invalid.");
            }

            if (parts[2] != "*")
            {
                if (!int.TryParse(parts[2], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedItemId) ||
                    parsedItemId <= 0)
                {
                    throw new WmsPermanentJobException("The forecast recalculation request is invalid.");
                }

                itemId = parsedItemId;
            }
        }

        var result = await forecastingService.RecalculateAsync(
            new ForecastingRecalculationQuery(
                context.Envelope.WarehouseId,
                itemId,
                granularity,
                horizonPeriods),
            cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException("The forecast recalculation request could not be completed.");
        }

        var outcome = result.Value;
        return new WmsJobExecutionResult(
            outcome.ItemsExamined,
            outcome.RunsCreated,
            $"Examined {outcome.ItemsExamined} item and warehouse pairs; created {outcome.RunsCreated} runs and reused {outcome.RunsReused}.");
    }
}

public sealed class WmsExpiryAlertJob(
    ILotService lotService,
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
        var today = WmsBusinessTime.GetBusinessDate(
            clock.UtcNow,
            values.Localization.TimeZone);
        var alerts = await lotService.GetExpiryAlertsAsync(
            today,
            values.Expiry.WarningDays,
            cancellationToken);
        if (alerts.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Lot expiry evaluation failed: {alerts.Error}");
        }

        foreach (var alert in alerts.Value.OrderBy(alert => alert.ExpiryDate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expiryDate = alert.ExpiryDate;
            var expired = alert.IsExpired;
            var lot = alert.Lot;
            var kind = expired ? "expiry.expired" : "expiry.warning";
            await executionStore.UpsertNotificationAsync(
                new WmsJobNotification(
                    $"{kind}:{lot.Id}:{today:yyyyMMdd}",
                    kind,
                    expired ? "critical" : "warning",
                    expired ? "Expired lot" : "Lot expiry warning",
                    $"Item {lot.ItemSku} lot {lot.Number} expires on {expiryDate:yyyy-MM-dd}.",
                    JobName,
                    context.Envelope.IdempotencyKey,
                    context.Envelope.CorrelationId,
                    ExpiresAtUtc: clock.UtcNow.AddDays(7)),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Expiry alert job evaluated {LotCount} lots and persisted idempotent notifications",
            alerts.Value.Count());
        var alertCount = alerts.Value.Count();
        return new WmsJobExecutionResult(alertCount, alertCount, $"Evaluated {alertCount} lots.");
    }
}

public sealed class WmsLowStockAlertJob(
    IInventoryReplenishmentPolicyService replenishmentPolicyService,
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
        var values = settings.IsSuccess
            ? settings.Value.Values
            : WmsSettingsDefaults.Create();
        var dashboard = values.Dashboard;
        var signals = await replenishmentPolicyService.GetSignalsAsync(
            new InventoryReplenishmentSignalQuery(Limit: dashboard.LowStockAlertLimit),
            cancellationToken);
        if (signals.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Replenishment signal evaluation failed: {signals.Error}");
        }

        var today = WmsBusinessTime.GetBusinessDate(
            clock.UtcNow,
            values.Localization.TimeZone);
        foreach (var signal in signals.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = signal.SignalKind switch
            {
                Wms.Domain.Enums.InventoryReplenishmentSignalKind.OutOfStock => "out-of-stock",
                Wms.Domain.Enums.InventoryReplenishmentSignalKind.Overstock => "overstock",
                _ => "low-stock"
            };
            var severity = signal.SignalKind ==
                Wms.Domain.Enums.InventoryReplenishmentSignalKind.OutOfStock
                ? "critical"
                : "warning";
            await executionStore.UpsertNotificationAsync(
                new WmsJobNotification(
                    $"replenishment:{signal.PolicyId}:{kind}:{today:yyyyMMdd}",
                    $"inventory.{kind}",
                    severity,
                    signal.SignalKind == Wms.Domain.Enums.InventoryReplenishmentSignalKind.Overstock
                        ? "Overstock alert"
                        : signal.SignalKind == Wms.Domain.Enums.InventoryReplenishmentSignalKind.OutOfStock
                            ? "Out-of-stock alert"
                            : "Low-stock alert",
                    $"Item {signal.ItemSku} at {signal.LocationCode ?? signal.WarehouseCode} has {signal.EvaluatedQuantity:0.####} units by {signal.QuantityBasis}; target is {signal.TargetQuantity:0.####}.",
                    JobName,
                    context.Envelope.IdempotencyKey,
                    context.Envelope.CorrelationId,
                    signal.WarehouseId,
                    clock.UtcNow.AddDays(1)),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Replenishment alert job evaluated {SignalCount} policy signals",
            signals.Value.Count);
        return new WmsJobExecutionResult(
            signals.Value.Count,
            signals.Value.Count,
            $"Evaluated {signals.Value.Count} replenishment policy signals.");
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
            .Where(movement => movement.Timestamp >= from && movement.Timestamp < now.UtcDateTime)
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
    INotificationDeliveryService notificationDeliveryService,
    IIntegrationOutboxDispatcher integrationOutboxDispatcher,
    ILogger<WmsIntegrationRetryJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.IntegrationRetries;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var notificationCount = await notificationDeliveryService.DispatchPendingAsync(
            cancellationToken: cancellationToken);
        var integrationResult = await integrationOutboxDispatcher.DispatchAsync(
            cancellationToken: cancellationToken);
        logger.LogInformation(
            "Integration retry job dispatched {NotificationCount} notifications, {DeliveryCount} webhook deliveries, and claimed {EventCount} outbox events",
            notificationCount,
            integrationResult.DeliveriesAttempted,
            integrationResult.EventsClaimed);
        return new WmsJobExecutionResult(
            ItemsExamined: notificationCount + integrationResult.EventsClaimed,
            ItemsCreated: integrationResult.DeliveriesSucceeded,
            Summary: $"Dispatched {notificationCount} notifications and {integrationResult.DeliveriesSucceeded}/{integrationResult.DeliveriesAttempted} webhook deliveries; {integrationResult.EventsDeadLettered} event dead-lettered.");
    }
}

public sealed class WmsCleanupJob(
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsCleanupJob> logger,
    IInventoryCommandIdempotencyService? idempotencyService = null,
    IRetentionService? retentionService = null) : IWmsJobHandler
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
        var idempotencyRemoved = idempotencyService is null
            ? 0
            : await idempotencyService.PruneAsync(
                now.AddDays(-90),
                cancellationToken);
        var totalRemoved = removed + idempotencyRemoved;
        var retentionPreview = retentionService is null
            ? null
            : await retentionService.PreviewAsync(
                new RetentionPreviewInput(AsOfUtc: now, BatchSize: 100),
                cancellationToken);
        var retentionExamined = retentionPreview?.IsSuccess == true
            ? retentionPreview.Value.ItemsExamined
            : 0;
        if (retentionPreview?.IsFailure == true)
        {
            logger.LogWarning(
                "Retention dry-run could not be recorded during cleanup: {ErrorCode}",
                retentionPreview.ErrorCode);
        }

        logger.LogInformation(
            "Background-job cleanup pruned {RecordCount} records, including {InventoryCommandRecordCount} inventory command records, and examined {RetentionRecordCount} retention candidates",
            totalRemoved,
            idempotencyRemoved,
            retentionExamined);
        return new WmsJobExecutionResult(
            totalRemoved + (int)Math.Min(retentionExamined, int.MaxValue),
            totalRemoved,
            $"Pruned {totalRemoved} records; retention dry-run examined {retentionExamined} candidates.");
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

public sealed class WmsInventoryClassificationRecalculationJob(
    IInventoryClassificationService classificationService,
    ILogger<WmsInventoryClassificationRecalculationJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.InventoryClassificationRecalculation;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await classificationService.RecalculateAsync(
            new InventoryClassificationRecalculationQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Limit: 1_000),
            context.Envelope.ActorUserId ?? "system",
            internalExecution: true,
            cancellationToken: cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Inventory classification recalculation failed: {result.Error}");
        }

        logger.LogInformation(
            "Inventory classification recalculation examined {PolicyCount} policies and {ItemCount} items; changed {ChangedCount} classifications",
            result.Value.PoliciesExamined,
            result.Value.ItemsExamined,
            result.Value.ClassificationsChanged);
        return new WmsJobExecutionResult(
            result.Value.ItemsExamined,
            result.Value.ClassificationsChanged,
            $"Examined {result.Value.ItemsExamined} items across {result.Value.PoliciesExamined} policies; changed {result.Value.ClassificationsChanged}, skipped {result.Value.ManualOverridesSkipped} active overrides.");
    }
}

public sealed class WmsSlottingAnalysisJob(
    ISlottingService slottingService,
    ILogger<WmsSlottingAnalysisJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.SlottingAnalysis;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await slottingService.AnalyzeAsync(
            new SlottingAnalysisQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Limit: 1_000),
            context.Envelope.ActorUserId ?? "system",
            internalExecution: true,
            cancellationToken: cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Slotting analysis failed: {result.Error}");
        }

        logger.LogInformation(
            "Slotting analysis examined {ItemCount} items and created {RecommendationCount} recommendations; {BlockedCount} were blocked",
            result.Value.ItemsExamined,
            result.Value.RecommendationsCreated,
            result.Value.Blocked);
        return new WmsJobExecutionResult(
            result.Value.ItemsExamined,
            result.Value.RecommendationsCreated,
            $"Examined {result.Value.ItemsExamined} items across {result.Value.PoliciesExamined} policies; created {result.Value.RecommendationsCreated}, reused {result.Value.RecommendationsReused}, blocked {result.Value.Blocked}.");
    }
}

public sealed class WmsCycleCountGenerationJob(
    ICycleCountService cycleCountService,
    ILogger<WmsCycleCountGenerationJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.CycleCountGeneration;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await cycleCountService.GenerateAsync(
            new CycleCountGenerationQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Limit: 1_000),
            context.Envelope.ActorUserId ?? "system",
            cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Cycle-count work generation failed: {result.Error}");
        }

        logger.LogInformation(
            "Cycle-count generation examined {PlanCount} plans and created {TaskCount} tasks",
            result.Value.PlansExamined,
            result.Value.TasksCreated);
        return new WmsJobExecutionResult(
            result.Value.PlansExamined,
            result.Value.TasksCreated,
            $"Examined {result.Value.PlansExamined} plans; created {result.Value.TasksCreated}, reused {result.Value.TasksReused}, and captured {result.Value.LinesCreated} lines.");
    }
}

public sealed class WmsReplenishmentGenerationJob(
    IReplenishmentExecutionService replenishmentExecutionService,
    ILogger<WmsReplenishmentGenerationJob> logger)
    : IWmsJobHandler
{
    public string JobName => WmsJobNames.ReplenishmentGeneration;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Limit: 1_000),
            context.Envelope.ActorUserId ?? "system",
            cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Replenishment work generation failed: {result.Error}");
        }

        logger.LogInformation(
            "Replenishment generation examined {SignalCount} signals and created {WorkCreated} work items; {Blocked} were blocked",
            result.Value.SignalsExamined,
            result.Value.WorkCreated,
            result.Value.Blocked);
        return new WmsJobExecutionResult(
            result.Value.SignalsExamined,
            result.Value.WorkCreated,
            $"Examined {result.Value.SignalsExamined} signals; created {result.Value.WorkCreated}, reused {result.Value.WorkReused}, blocked {result.Value.Blocked}.");
    }
}

public sealed class WmsWavePlanningJob(
    IWaveService waveService,
    ILogger<WmsWavePlanningJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.WavePlanning;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = await waveService.RunScheduledAsync(
            context.Envelope.WarehouseId,
            context.Envelope.ActorUserId ?? "system",
            cancellationToken);
        if (result.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Outbound wave planning failed: {result.Error}");
        }

        logger.LogInformation(
            "Outbound wave planning examined {TemplateCount} templates and created {WaveCount} waves with {LineCount} selected lines",
            result.Value.TemplatesExamined,
            result.Value.WavesCreated,
            result.Value.LinesSelected);
        return new WmsJobExecutionResult(
            result.Value.TemplatesExamined,
            result.Value.WavesCreated,
            $"Examined {result.Value.TemplatesExamined} templates; created {result.Value.WavesCreated} waves and selected {result.Value.LinesSelected} lines.");
    }
}

public sealed class WmsInventoryHealthCheckJob(
    IInventoryReconciliationService reconciliationService,
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsInventoryHealthCheckJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.InventoryHealthCheck;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var report = await reconciliationService.ReconcileAsync(
            new InventoryReconciliationQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Deep: false,
                BatchSize: 250,
                MaxIssues: 100),
            cancellationToken);
        if (report.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Inventory health check failed: {report.Error}");
        }

        if (!report.Value.IsClean)
        {
            await executionStore.UpsertNotificationAsync(
                CreateNotification(
                    report.Value,
                    "light",
                    clock.UtcNow,
                    context),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Inventory health check scanned {BalanceCount} balances and {TransactionCount} transactions; {IssueCount} issues detected",
            report.Value.BalancesScanned,
            report.Value.TransactionsScanned,
            report.Value.IssueCount);
        return new WmsJobExecutionResult(
            ToInt(report.Value.BalancesScanned + report.Value.TransactionsScanned),
            report.Value.IsClean ? 0 : 1,
            $"Lightweight inventory check detected {report.Value.IssueCount} issues.");
    }

    private static WmsJobNotification CreateNotification(
        InventoryReconciliationReportDto report,
        string mode,
        DateTimeOffset nowUtc,
        WmsJobContext context) =>
        new(
            $"inventory-reconciliation:{mode}:{context.Envelope.WarehouseId?.ToString(CultureInfo.InvariantCulture) ?? "global"}:{nowUtc:yyyyMMddHHmm}",
            "inventory.reconciliation",
            report.CriticalIssueCount > 0 ? "critical" : "warning",
            "Inventory reconciliation issue detected",
            $"The {mode} inventory check detected {report.IssueCount} issues " +
            $"({report.CriticalIssueCount} critical, {report.WarningIssueCount} warning).",
            context.Envelope.JobName,
            context.Envelope.IdempotencyKey,
            context.Envelope.CorrelationId,
            context.Envelope.WarehouseId,
            nowUtc.AddHours(2));

    public static WmsJobNotification CreateNotificationForDeepReport(
        InventoryReconciliationReportDto report,
        DateTimeOffset nowUtc,
        WmsJobContext context) =>
        CreateNotification(report, "deep", nowUtc, context);

    private static int ToInt(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;
}

public sealed class WmsInventoryReconciliationJob(
    IInventoryReconciliationService reconciliationService,
    IWmsJobExecutionStore executionStore,
    IClock clock,
    ILogger<WmsInventoryReconciliationJob> logger) : IWmsJobHandler
{
    public string JobName => WmsJobNames.InventoryReconciliation;

    public async Task<WmsJobExecutionResult> ExecuteAsync(
        WmsJobContext context,
        CancellationToken cancellationToken = default)
    {
        var report = await reconciliationService.ReconcileAsync(
            new InventoryReconciliationQuery(
                WarehouseId: context.Envelope.WarehouseId,
                Deep: true,
                BatchSize: 500,
                MaxIssues: 2_000),
            cancellationToken);
        if (report.IsFailure)
        {
            throw new WmsPermanentJobException(
                $"Deep inventory reconciliation failed: {report.Error}");
        }

        if (!report.Value.IsClean)
        {
            await executionStore.UpsertNotificationAsync(
                WmsInventoryHealthCheckJob.CreateNotificationForDeepReport(
                    report.Value,
                    clock.UtcNow,
                    context),
                clock.UtcNow,
                cancellationToken);
        }

        logger.LogInformation(
            "Deep inventory reconciliation scanned {BalanceCount} balances, {TransactionCount} transactions, {ReservationCount} reservations, and {IssueCount} issues",
            report.Value.BalancesScanned,
            report.Value.TransactionsScanned,
            report.Value.ReservationsScanned,
            report.Value.IssueCount);
        return new WmsJobExecutionResult(
            ToInt(report.Value.BalancesScanned + report.Value.TransactionsScanned +
                report.Value.ReservationsScanned + report.Value.AllocationsScanned),
            report.Value.IsClean ? 0 : 1,
            $"Deep inventory reconciliation detected {report.Value.IssueCount} issues.");
    }

    private static int ToInt(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;
}
