using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Wms.Application.Auditing;
using Wms.Application.Backups;
using Wms.Application.Context;
using Wms.Application.DataGeneration;
using Wms.Application.DependencyInjection;
using Wms.Application.Integrations;
using Wms.Application.Inventory;
using Wms.Application.Jobs;
using Wms.Application.Telemetry;
using Wms.Application.WarehouseWork;
using Wms.ASP.Jobs;
using Wms.Domain.Enums;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Backups;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Integrations;
using Wms.Infrastructure.Tests.Integration;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlRuntimeResilienceTests
{
    private static readonly string[] RemainingGates = [];

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new()
    {
        WriteIndented = true
    };

    [PostgreSqlFact]
    public async Task RuntimeRetryJobRecoversProviderCommitsBoundsFailuresAndPersistsDiagnostics()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        await CreateProviderEvidenceTablesAsync(target);

        var clock = new TestClock(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        await using var receiver = await ResilienceWebhookReceiver.StartAsync(target.TestConnectionString);
        using var httpClient = receiver.CreateHttpClient();
        var options = receiver.TransportOptions;
        var policy = ResilienceWebhookReceiver.CreateDestinationPolicy(options);
        var transport = new HttpWebhookDeliveryTransport(
            httpClient,
            options,
            policy,
            NullLogger<HttpWebhookDeliveryTransport>.Instance);
        using var provider = CreateServiceProvider(target, clock, transport);
        var scenarios = new List<object>();

        var responseLossEventId = await CreateEventAsync(
            provider,
            WmsIntegrationEventTypes.InventoryMovementRecorded,
            "issue-133-response-loss");
        await CreateSubscriptionAsync(
            provider,
            receiver.EndpointUrl,
            WmsIntegrationEventTypes.InventoryMovementRecorded,
            maximumAttempts: 3);

        var firstResponseLossJobKey = "issue-133:response-loss:first";
        await RunIntegrationRetryJobAsync(provider, firstResponseLossJobKey);
        var firstResponseLoss = await ReadOutboxAsync(target, responseLossEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Failed, firstResponseLoss.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Failed, firstResponseLoss.DeliveryStatus);
        Assert.Equal(1, firstResponseLoss.DeliveryAttemptCount);
        Assert.Equal(clock.UtcNow.AddMinutes(1), firstResponseLoss.DeliveryNextAttemptAtUtc);
        Assert.Null(firstResponseLoss.ResponseBody);
        Assert.Equal("webhook_connection_failed", firstResponseLoss.DeliveryError);
        Assert.Equal(1, await CountProviderRequestsAsync(target, firstResponseLoss.DeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, firstResponseLoss.DeliveryId));
        var firstJob = await ReadJobExecutionAsync(target, firstResponseLossJobKey);
        Assert.Equal(WmsJobExecutionStatuses.Succeeded, firstJob.Status);
        Assert.Equal(1, firstJob.AttemptCount);

        clock.UtcNow = firstResponseLoss.DeliveryNextAttemptAtUtc!.Value;
        var resumedResponseLossJobKey = "issue-133:response-loss:resumed";
        await RunIntegrationRetryJobAsync(provider, resumedResponseLossJobKey);
        var resumedResponseLoss = await ReadOutboxAsync(target, responseLossEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Delivered, resumedResponseLoss.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Delivered, resumedResponseLoss.DeliveryStatus);
        Assert.Equal(2, resumedResponseLoss.DeliveryAttemptCount);
        Assert.Equal(firstResponseLoss.DeliveryId, resumedResponseLoss.DeliveryId);
        Assert.Equal(responseLossEventId, resumedResponseLoss.EventId);
        Assert.Equal(2, await CountProviderRequestsAsync(target, resumedResponseLoss.DeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, resumedResponseLoss.DeliveryId));
        await RunIntegrationRetryJobAsync(provider, resumedResponseLossJobKey);
        Assert.Equal(2, await CountProviderRequestsAsync(target, resumedResponseLoss.DeliveryId));
        scenarios.Add(new
        {
            id = "response-loss-after-provider-commit-subsequent-job-recovery",
            eventId = responseLossEventId,
            deliveryId = resumedResponseLoss.DeliveryId,
            correlationId = resumedResponseLoss.CorrelationId,
            outboxStatus = resumedResponseLoss.Status,
            deliveryStatus = resumedResponseLoss.DeliveryStatus,
            deliveryAttempts = resumedResponseLoss.DeliveryAttemptCount,
            providerRequests = 2,
            providerCommits = 1,
            jobAttempts = new[] { firstJob.AttemptCount, (await ReadJobExecutionAsync(target, resumedResponseLossJobKey)).AttemptCount },
            result = "passed"
        });

        var transientEventId = await CreateEventAsync(
            provider,
            WmsIntegrationEventTypes.WarehouseChanged,
            "issue-133-transient-provider");
        await CreateSubscriptionAsync(
            provider,
            receiver.EndpointUrl,
            WmsIntegrationEventTypes.WarehouseChanged,
            maximumAttempts: 2);
        var transientFirstJobKey = "issue-133:transient:first";
        await RunIntegrationRetryJobAsync(provider, transientFirstJobKey);
        var transientFirst = await ReadOutboxAsync(target, transientEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Failed, transientFirst.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Failed, transientFirst.DeliveryStatus);
        Assert.Equal(1, transientFirst.DeliveryAttemptCount);
        Assert.Equal(503, transientFirst.ResponseStatusCode);
        Assert.Null(transientFirst.ResponseBody);
        Assert.Equal("webhook_http_retryable_failure", transientFirst.DeliveryError);
        Assert.Equal(clock.UtcNow.AddMinutes(1), transientFirst.DeliveryNextAttemptAtUtc);

        await RunIntegrationRetryJobAsync(provider, "issue-133:transient:too-early");
        Assert.Equal(1, await CountProviderRequestsAsync(target, transientFirst.DeliveryId));
        clock.UtcNow = transientFirst.DeliveryNextAttemptAtUtc!.Value;
        await RunIntegrationRetryJobAsync(provider, "issue-133:transient:recovered");
        var transientRecovered = await ReadOutboxAsync(target, transientEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Delivered, transientRecovered.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Delivered, transientRecovered.DeliveryStatus);
        Assert.Equal(2, transientRecovered.DeliveryAttemptCount);
        Assert.Equal(2, await CountProviderRequestsAsync(target, transientRecovered.DeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, transientRecovered.DeliveryId));
        scenarios.Add(new
        {
            id = "transient-provider-failure-backoff-and-recovery",
            eventId = transientEventId,
            deliveryId = transientRecovered.DeliveryId,
            correlationId = transientRecovered.CorrelationId,
            outboxStatus = transientRecovered.Status,
            deliveryStatus = transientRecovered.DeliveryStatus,
            deliveryAttempts = transientRecovered.DeliveryAttemptCount,
            firstResponseStatus = transientFirst.ResponseStatusCode,
            prematureRetryRequests = 0,
            providerRequests = 2,
            providerCommits = 1,
            result = "passed"
        });

        var boundedFailureEventId = await CreateEventAsync(
            provider,
            WmsIntegrationEventTypes.WorkLifecycleChanged,
            "issue-133-bounded-provider-failure");
        await CreateSubscriptionAsync(
            provider,
            receiver.EndpointUrl,
            WmsIntegrationEventTypes.WorkLifecycleChanged,
            maximumAttempts: 2);
        await RunIntegrationRetryJobAsync(provider, "issue-133:bounded:first");
        var boundedFailureFirst = await ReadOutboxAsync(target, boundedFailureEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Failed, boundedFailureFirst.Status);
        Assert.Equal(1, boundedFailureFirst.DeliveryAttemptCount);
        Assert.Equal(503, boundedFailureFirst.ResponseStatusCode);
        clock.UtcNow = boundedFailureFirst.DeliveryNextAttemptAtUtc!.Value;
        await RunIntegrationRetryJobAsync(provider, "issue-133:bounded:second");
        var boundedFailure = await ReadOutboxAsync(target, boundedFailureEventId);
        Assert.Equal(WmsIntegrationEventStatuses.DeadLettered, boundedFailure.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.DeadLettered, boundedFailure.DeliveryStatus);
        Assert.Equal(2, boundedFailure.DeliveryAttemptCount);
        Assert.Equal(503, boundedFailure.ResponseStatusCode);
        Assert.Null(boundedFailure.ResponseBody);
        Assert.Equal("webhook_http_retryable_failure", boundedFailure.DeliveryError);
        await RunIntegrationRetryJobAsync(provider, "issue-133:bounded:after-dead-letter");
        Assert.Equal(2, await CountProviderRequestsAsync(target, boundedFailure.DeliveryId));
        scenarios.Add(new
        {
            id = "bounded-transient-provider-failure-dead-letter",
            eventId = boundedFailureEventId,
            deliveryId = boundedFailure.DeliveryId,
            correlationId = boundedFailure.CorrelationId,
            outboxStatus = boundedFailure.Status,
            deliveryStatus = boundedFailure.DeliveryStatus,
            deliveryAttempts = boundedFailure.DeliveryAttemptCount,
            providerRequests = 2,
            providerCommits = 0,
            result = "passed"
        });

        var permanentFailureEventId = await CreateEventAsync(
            provider,
            WmsIntegrationEventTypes.ItemChanged,
            "issue-133-permanent-provider-failure");
        await CreateSubscriptionAsync(
            provider,
            receiver.EndpointUrl,
            WmsIntegrationEventTypes.ItemChanged,
            maximumAttempts: 3);
        await RunIntegrationRetryJobAsync(provider, "issue-133:permanent:first");
        var permanentFailure = await ReadOutboxAsync(target, permanentFailureEventId);
        Assert.Equal(WmsIntegrationEventStatuses.DeadLettered, permanentFailure.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.DeadLettered, permanentFailure.DeliveryStatus);
        Assert.Equal(1, permanentFailure.DeliveryAttemptCount);
        Assert.Equal(422, permanentFailure.ResponseStatusCode);
        Assert.Null(permanentFailure.ResponseBody);
        Assert.Equal("webhook_http_permanent_failure", permanentFailure.DeliveryError);
        Assert.Equal("issue-133-permanent-provider-failure", permanentFailure.CorrelationId);

        const string replayActorId = "issue-133-resilience-operator";
        const string replayReason = "Provider contract was corrected after review.";
        await using (var replayScope = provider.CreateAsyncScope())
        {
            var replayService = replayScope.ServiceProvider
                .GetRequiredService<IIntegrationDeadLetterReplayService>();
            var replay = await replayService.ReplayAsync(
                permanentFailure.Id,
                replayReason,
                replayActorId,
                "Resilience Operator");
            Assert.True(replay.IsSuccess, replay.FirstError?.Message);
            Assert.Equal(permanentFailure.EventId, replay.Value.EventId);
            Assert.Equal(1, replay.Value.DeliveriesQueued);

            var duplicateReplay = await replayService.ReplayAsync(
                permanentFailure.Id,
                replayReason,
                replayActorId,
                "Resilience Operator");
            Assert.True(duplicateReplay.IsFailure);
            Assert.Equal("integration.replay.not_dead_lettered", duplicateReplay.ErrorCode);
        }

        var replayedAudit = await ReadReplayAuditAsync(target, permanentFailure.Id);
        Assert.Equal(replayActorId, replayedAudit.ActorUserId);
        Assert.Equal(WmsAuditActions.IntegrationDeadLetterReplayed, replayedAudit.Action);
        Assert.Contains(replayReason, replayedAudit.AfterJson, StringComparison.Ordinal);
        await RunIntegrationRetryJobAsync(provider, "issue-133:permanent:replayed");
        var replayedPermanentFailure = await ReadOutboxAsync(target, permanentFailureEventId);
        Assert.Equal(WmsIntegrationEventStatuses.Delivered, replayedPermanentFailure.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Delivered, replayedPermanentFailure.DeliveryStatus);
        Assert.Equal(2, replayedPermanentFailure.DeliveryAttemptCount);
        Assert.Equal(permanentFailure.DeliveryId, replayedPermanentFailure.DeliveryId);
        Assert.Equal(2, await CountProviderRequestsAsync(target, replayedPermanentFailure.DeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, replayedPermanentFailure.DeliveryId));
        scenarios.Add(new
        {
            id = "permanent-provider-failure-authorized-replay",
            eventId = permanentFailureEventId,
            deliveryId = replayedPermanentFailure.DeliveryId,
            correlationId = permanentFailure.CorrelationId,
            initialDeadLetterStatus = permanentFailure.Status,
            finalOutboxStatus = replayedPermanentFailure.Status,
            finalDeliveryStatus = replayedPermanentFailure.DeliveryStatus,
            deliveryAttempts = replayedPermanentFailure.DeliveryAttemptCount,
            providerRequests = await CountProviderRequestsAsync(target, replayedPermanentFailure.DeliveryId),
            providerCommits = await CountProviderCommitsAsync(target, replayedPermanentFailure.DeliveryId),
            auditActor = replayedAudit.ActorUserId,
            replayReason = replayReason,
            boundedAndRedactedDiagnostics = permanentFailure.DeliveryError,
            result = "passed"
        });

        var transientDatabaseEventId = await CreateEventAsync(
            provider,
            WmsIntegrationEventTypes.InventoryStockAdjusted,
            "issue-133-transient-database-failure");
        await CreateSubscriptionAsync(
            provider,
            receiver.EndpointUrl,
            WmsIntegrationEventTypes.InventoryStockAdjusted,
            maximumAttempts: 3);
        var databaseFailureTrigger = await CreateTransientOutboxDatabaseFailureTriggerAsync(
            target,
            transientDatabaseEventId);
        const string databaseFailureJobKey = "issue-133:database-failure:dispatch";
        var databaseFailure = await Assert.ThrowsAnyAsync<Exception>(() =>
            RunIntegrationRetryJobAsync(provider, databaseFailureJobKey));
        Assert.Equal("40001", FindPostgresException(databaseFailure)?.SqlState);
        var failedDatabaseOutbox = await ReadOutboxMessageAsync(target, transientDatabaseEventId);
        var failedDatabaseJob = await ReadJobExecutionAsync(target, databaseFailureJobKey);
        Assert.Equal(WmsIntegrationEventStatuses.Pending, failedDatabaseOutbox.Status);
        Assert.Equal(0, failedDatabaseOutbox.MessageAttemptCount);
        Assert.Null(failedDatabaseOutbox.MessageLeaseUntilUtc);
        Assert.Equal(0, await CountWebhookDeliveriesAsync(target, failedDatabaseOutbox.Id));
        Assert.Equal(WmsJobExecutionStatuses.Running, failedDatabaseJob.Status);
        Assert.Equal(1, failedDatabaseJob.AttemptCount);
        await DropTransientOutboxDatabaseFailureTriggerAsync(target, databaseFailureTrigger);

        clock.UtcNow = clock.UtcNow.AddHours(1).AddSeconds(1);
        await RunIntegrationRetryJobAsync(provider, databaseFailureJobKey);
        var recoveredDatabaseEvent = await ReadOutboxAsync(target, transientDatabaseEventId);
        var recoveredDatabaseJob = await ReadJobExecutionAsync(target, databaseFailureJobKey);
        Assert.Equal(WmsIntegrationEventStatuses.Delivered, recoveredDatabaseEvent.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Delivered, recoveredDatabaseEvent.DeliveryStatus);
        Assert.Equal(1, recoveredDatabaseEvent.MessageAttemptCount);
        Assert.Equal(1, recoveredDatabaseEvent.DeliveryAttemptCount);
        Assert.Equal(WmsJobExecutionStatuses.Succeeded, recoveredDatabaseJob.Status);
        Assert.Equal(2, recoveredDatabaseJob.AttemptCount);
        Assert.Equal(1, await CountProviderRequestsAsync(target, recoveredDatabaseEvent.DeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, recoveredDatabaseEvent.DeliveryId));
        scenarios.Add(new
        {
            id = "transient-postgresql-serialization-failure-reclaim-and-recovery",
            eventId = transientDatabaseEventId,
            deliveryId = recoveredDatabaseEvent.DeliveryId,
            injectedSqlState = FindPostgresException(databaseFailure)?.SqlState,
            initialOutboxStatus = failedDatabaseOutbox.Status,
            initialOutboxAttempts = failedDatabaseOutbox.MessageAttemptCount,
            failedJobAttempts = failedDatabaseJob.AttemptCount,
            finalOutboxStatus = recoveredDatabaseEvent.Status,
            finalJobStatus = recoveredDatabaseJob.Status,
            finalJobAttempts = recoveredDatabaseJob.AttemptCount,
            providerRequests = 1,
            providerCommits = 1,
            result = "passed"
        });

        WriteEvidence(new
        {
            schema = "wms-postgresql-runtime-resilience-v1",
            metadata = new
            {
                environment = "Disposable PostgreSQL 17 with loopback HTTP provider",
                applicationRevision = Environment.GetEnvironmentVariable("WARECOMMAND_VERIFICATION_REVISION"),
                targetIdentifier = target.TargetIdentifier,
                runStartedAtUtc = startedAtUtc,
                runCompletedAtUtc = DateTimeOffset.UtcNow,
                faultInjectionMode = "test-only PostgreSQL outbox trigger plus external loopback HTTP provider",
                productionFaultInjectionEnabled = false
            },
            scenarios,
            remainingGates = RemainingGates
        });
    }

    [PostgreSqlFact]
    public async Task WorkerRestartReclaimsExpiredOutboxLeaseAndDoesNotRepeatProviderCommit()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        await CreateProviderEvidenceTablesAsync(target);

        var clock = new TestClock(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        await using var receiver = await ResilienceWebhookReceiver.StartAsync(
            target.TestConnectionString,
            WmsIntegrationEventTypes.InventoryStockAdjusted);
        var transportOptions = receiver.TransportOptions;
        var transportPolicy = ResilienceWebhookReceiver.CreateDestinationPolicy(transportOptions);
        var sharedDataProtectionProvider = new EphemeralDataProtectionProvider();
        Guid eventId;
        const string jobKey = "issue-133:worker-restart:dispatch";
        using (var setupClient = receiver.CreateHttpClient())
        using (var setupProvider = CreateServiceProvider(
                   target,
                   clock,
                   new HttpWebhookDeliveryTransport(
                       setupClient,
                       transportOptions,
                       transportPolicy,
                       NullLogger<HttpWebhookDeliveryTransport>.Instance),
                   sharedDataProtectionProvider))
        {
            eventId = await CreateEventAsync(
                setupProvider,
                WmsIntegrationEventTypes.InventoryStockAdjusted,
                "issue-133-worker-restart");
            await using var setupScope = setupProvider.CreateAsyncScope();
            var subscription = await setupScope.ServiceProvider.GetRequiredService<IWebhookSubscriptionService>()
                .CreateAsync(new WebhookSubscriptionCreateRequest(
                    "Issue 133 worker restart",
                    receiver.EndpointUrl,
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        WmsIntegrationEventTypes.InventoryStockAdjusted
                    },
                    MaximumAttempts: 3));
            Assert.True(subscription.Subscription.Id > 0);
        }

        long committedDeliveryId;
        using (var firstClient = receiver.CreateHttpClient())
        using (var firstWorker = CreateServiceProvider(
                   target,
                   clock,
                   new HttpWebhookDeliveryTransport(
                       firstClient,
                       transportOptions,
                       transportPolicy,
                       NullLogger<HttpWebhookDeliveryTransport>.Instance),
                   sharedDataProtectionProvider))
        using (var shutdown = new CancellationTokenSource())
        {
            var workerTask = RunIntegrationRetryJobAsync(firstWorker, jobKey, shutdown.Token);
            var commitSignal = receiver.WaitForFirstProviderCommitAsync(TimeSpan.FromSeconds(15));
            var firstCompleted = await Task.WhenAny(workerTask, commitSignal);
            if (firstCompleted == workerTask)
            {
                await workerTask;
                await using var diagnosticContext = target.CreateContext();
                var diagnosticMessage = await diagnosticContext.IntegrationOutbox.AsNoTracking()
                    .SingleAsync(value => value.EventId == eventId);
                var diagnosticDelivery = await diagnosticContext.WebhookDeliveries.AsNoTracking()
                    .Where(value => value.OutboxMessageId == diagnosticMessage.Id)
                    .Select(value => new { value.Status, value.LastError, value.AttemptCount })
                    .FirstOrDefaultAsync();
                var diagnosticJob = await ReadJobExecutionAsync(target, jobKey);
                throw new InvalidOperationException(
                    "The integration retry job completed before the provider recorded its durable commit. " +
                    $"Outbox={diagnosticMessage.Status}; " +
                    $"delivery={diagnosticDelivery?.Status ?? "none"}; " +
                    $"deliveryAttempts={diagnosticDelivery?.AttemptCount ?? 0}; " +
                    $"deliveryError={diagnosticDelivery?.LastError ?? "none"}; " +
                    $"job={diagnosticJob.Status}/{diagnosticJob.AttemptCount}; " +
                    $"subscriptions={await diagnosticContext.WebhookSubscriptions.CountAsync()}. ");
            }

            committedDeliveryId = await commitSignal;
            shutdown.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workerTask);
        }

        var interrupted = await ReadOutboxAsync(target, eventId);
        var canceledJob = await ReadJobExecutionAsync(target, jobKey);
        Assert.Equal(WmsIntegrationEventStatuses.Processing, interrupted.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Processing, interrupted.DeliveryStatus);
        Assert.True(interrupted.MessageLeaseUntilUtc > clock.UtcNow);
        Assert.True(interrupted.DeliveryLeaseUntilUtc > clock.UtcNow);
        Assert.Equal(WmsJobExecutionStatuses.Canceled, canceledJob.Status);
        Assert.Equal(1, canceledJob.AttemptCount);
        Assert.Equal(1, await CountProviderRequestsAsync(target, committedDeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, committedDeliveryId));

        clock.UtcNow = interrupted.MessageLeaseUntilUtc!.Value.AddSeconds(1);
        using (var restartedClient = receiver.CreateHttpClient())
        using (var restartedWorker = CreateServiceProvider(
                   target,
                   clock,
                   new HttpWebhookDeliveryTransport(
                       restartedClient,
                       transportOptions,
                       transportPolicy,
                       NullLogger<HttpWebhookDeliveryTransport>.Instance),
                   sharedDataProtectionProvider))
        {
            await RunIntegrationRetryJobAsync(restartedWorker, jobKey);
        }

        var recovered = await ReadOutboxAsync(target, eventId);
        var completedJob = await ReadJobExecutionAsync(target, jobKey);
        Assert.Equal(interrupted.Id, recovered.Id);
        Assert.Equal(interrupted.DeliveryId, recovered.DeliveryId);
        Assert.Equal(WmsIntegrationEventStatuses.Delivered, recovered.Status);
        Assert.Equal(WmsWebhookDeliveryStatuses.Delivered, recovered.DeliveryStatus);
        Assert.Equal(2, recovered.MessageAttemptCount);
        Assert.Equal(2, recovered.DeliveryAttemptCount);
        Assert.Equal(WmsJobExecutionStatuses.Succeeded, completedJob.Status);
        Assert.Equal(2, completedJob.AttemptCount);
        Assert.Equal(2, await CountProviderRequestsAsync(target, committedDeliveryId));
        Assert.Equal(1, await CountProviderCommitsAsync(target, committedDeliveryId));

        WriteEvidence(new
        {
            schema = "wms-postgresql-runtime-resilience-v1",
            metadata = new
            {
                environment = "Disposable PostgreSQL 17 with loopback HTTP provider",
                applicationRevision = Environment.GetEnvironmentVariable("WARECOMMAND_VERIFICATION_REVISION"),
                containerName = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONTAINER"),
                targetIdentifier = target.TargetIdentifier,
                runStartedAtUtc = startedAtUtc,
                runCompletedAtUtc = DateTimeOffset.UtcNow,
                faultInjectionMode = "cancel worker after durable provider commit; recreate worker services after lease expiry",
                productionFaultInjectionEnabled = false
            },
            scenarios = new[]
            {
                new
                {
                    id = "worker-restart-expired-outbox-lease-reclaim",
                    eventId,
                    deliveryId = committedDeliveryId,
                    canceledWorkerJobAttempts = canceledJob.AttemptCount,
                    finalWorkerJobAttempts = completedJob.AttemptCount,
                    messageAttempts = recovered.MessageAttemptCount,
                    deliveryAttempts = recovered.DeliveryAttemptCount,
                    providerRequests = 2,
                    providerCommits = 1,
                    result = "passed"
                }
            },
            remainingGates = RemainingGates
        });
    }

    [PostgreSqlFact]
    public async Task PopulatedPostgreSqlBackupRestoresAndIncompleteRecoveryFailsCleanly()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        await using var source = new PostgreSqlTestDatabase();
        await source.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            source,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-133-populated-restore",
                Environment: "Testing",
                Locale: "en-US"));
        var generated = seeded.Report;
        Assert.True(generated.ReconciliationClean);
        Assert.True(generated.ActualCounts["purchaseOrders"] > 0);
        Assert.True(generated.ActualCounts["receipts"] > 0);
        Assert.True(generated.ActualCounts["salesOrders"] > 0);
        Assert.True(generated.ActualCounts["reservations"] > 0);
        Assert.True(generated.ActualCounts["reservationAllocations"] > 0);
        Assert.True(generated.ActualCounts["shipments"] > 0);
        Assert.True(generated.ActualCounts["cycleCountTasks"] > 0);

        var workReplay = await RehearseWorkCommandResponseLossAsync(source, seeded.ActorCredentials);
        var sourceSnapshot = await ReadDatabaseTableCountsAsync(source.TestConnectionString);
        var sourceReconciliation = await ReconcileDatabaseAsync(
            source.TestConnectionString,
            seeded.ActorCredentials);
        Assert.True(sourceReconciliation.IsClean, string.Join(
            Environment.NewLine,
            sourceReconciliation.Issues.Select(issue => issue.Message)));

        var containerName = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONTAINER");
        Assert.False(string.IsNullOrWhiteSpace(containerName),
            "The PostgreSQL verification runner must provide its disposable container identity.");
        var ownedRoot = Path.Combine(
            Path.GetTempPath(),
            "warecommand-issue-133-backup-" + Guid.NewGuid().ToString("N"));
        var backupRoot = Path.Combine(ownedRoot, "backups");
        var encryptionKeyPath = Path.Combine(ownedRoot, "backup.key");
        var createdDatabases = new List<string>();
        var restoredDatabase = "wms_restore_" + Guid.NewGuid().ToString("N");
        var interruptedDatabase = "wms_restore_" + Guid.NewGuid().ToString("N");
        var restoreBuilder = new NpgsqlConnectionStringBuilder(source.TestConnectionString)
        {
            Database = restoredDatabase,
            SearchPath = source.TargetIdentifier,
            Pooling = false
        };
        var interruptedBuilder = new NpgsqlConnectionStringBuilder(source.TestConnectionString)
        {
            Database = interruptedDatabase,
            SearchPath = source.TargetIdentifier,
            Pooling = false
        };

        try
        {
            Directory.CreateDirectory(ownedRoot);
            await File.WriteAllBytesAsync(encryptionKeyPath, RandomNumberGenerator.GetBytes(32));
            await CreateDatabaseAsync(source.TestConnectionString, interruptedDatabase);
            createdDatabases.Add(interruptedDatabase);
            await CreateDatabaseAsync(source.TestConnectionString, restoredDatabase);
            createdDatabases.Add(restoredDatabase);

            var options = new WmsBackupOptions(
                Enabled: true,
                RootPath: backupRoot,
                OffsitePath: null,
                EncryptionKeyFile: encryptionKeyPath,
                RetentionDays: 30,
                MinimumRetainedBackups: 2,
                MaximumAgeHours: 36,
                DataProtectionKeysPath: null,
                AssetsPath: null,
                PgDumpExecutable: "pg_dump",
                PgRestoreExecutable: "pg_restore");
            var backupService = new WmsPostgreSqlBackupService(
                options,
                source.TestConnectionString,
                new WmsBackupHealthState(true, TimeSpan.FromHours(36)),
                NullLogger<WmsPostgreSqlBackupService>.Instance,
                new DockerPostgreSqlToolProcessRunner(containerName!));

            var backup = await backupService.CreateAsync(Environment.GetEnvironmentVariable(
                "WARECOMMAND_VERIFICATION_REVISION"));
            Assert.True(backup.SizeBytes > 0);
            Assert.True(backup.Manifest.DatabaseSummary.Items > 0);
            var validBackup = await backupService.VerifyAsync(backup.ArtifactPath);
            Assert.True(validBackup.IsValid, validBackup.FailureReason);

            var corruptedPath = Path.Combine(ownedRoot, "corrupted.wcbak");
            File.Copy(backup.ArtifactPath, corruptedPath);
            await CorruptFinalByteAsync(corruptedPath);
            var corruptedVerification = await backupService.VerifyAsync(corruptedPath);
            Assert.False(corruptedVerification.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(corruptedVerification.FailureReason));

            var missingPath = Path.Combine(ownedRoot, "missing.wcbak");
            var missingVerification = await backupService.VerifyAsync(missingPath);
            Assert.False(missingVerification.IsValid);
            await Assert.ThrowsAsync<FileNotFoundException>(() => backupService.RestoreAsync(
                missingPath,
                restoreBuilder.ConnectionString,
                allowNonEmptyTarget: false));

            await CreateRestoreFailureEventTriggerAsync(interruptedBuilder.ConnectionString);
            var failedRecovery = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                backupService.RestoreAsync(
                    backup.ArtifactPath,
                    interruptedBuilder.ConnectionString,
                    allowNonEmptyTarget: false));
            Assert.Contains("pg_restore", failedRecovery.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await DoesSchemaExistAsync(
                interruptedBuilder.ConnectionString,
                source.TargetIdentifier));

            var stopwatch = Stopwatch.StartNew();
            var restored = await backupService.RestoreAsync(
                backup.ArtifactPath,
                restoreBuilder.ConnectionString,
                allowNonEmptyTarget: false);
            stopwatch.Stop();
            Assert.Equal(backup.Manifest.DatabaseSummary, restored.RestoredSummary);
            Assert.Equal(restoredDatabase, restored.TargetDatabase);

            var restoredSnapshot = await ReadDatabaseTableCountsAsync(restoreBuilder.ConnectionString);
            Assert.Equal(
                sourceSnapshot.OrderBy(value => value.Key).ToArray(),
                restoredSnapshot.OrderBy(value => value.Key).ToArray());
            var restoredReconciliation = await ReconcileDatabaseAsync(
                restoreBuilder.ConnectionString,
                seeded.ActorCredentials);
            Assert.True(restoredReconciliation.IsClean, string.Join(
                Environment.NewLine,
                restoredReconciliation.Issues.Select(issue => issue.Message)));
            Assert.True(restoredReconciliation.TransactionsScanned > 0);
            Assert.True(restoredReconciliation.ReservationsScanned > 0);
            Assert.True(restoredReconciliation.AllocationsScanned > 0);

            WriteEvidence(new
            {
                schema = "wms-postgresql-runtime-resilience-v1",
                metadata = new
                {
                    environment = "Disposable PostgreSQL 17 with encrypted Wms.Backup artifact",
                    applicationRevision = Environment.GetEnvironmentVariable("WARECOMMAND_VERIFICATION_REVISION"),
                    containerName,
                    sourceTargetIdentifier = source.TargetIdentifier,
                    sourceDatabase = new NpgsqlConnectionStringBuilder(source.TestConnectionString).Database,
                    restoredDatabase,
                    runStartedAtUtc = startedAtUtc,
                    runCompletedAtUtc = DateTimeOffset.UtcNow,
                    backupCreatedAtUtc = backup.Manifest.CreatedAtUtc,
                    dataCutoffAtUtc = backup.Manifest.CreatedAtUtc,
                    backupSizeBytes = backup.SizeBytes,
                    backupSha256 = backup.Sha256,
                    recoveryDurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
                    productionFaultInjectionEnabled = false
                },
                scenarios = new object[]
                {
                    new
                    {
                        id = workReplay.Scenario,
                        workId = workReplay.WorkId,
                        workNumber = workReplay.WorkNumber,
                        persistedBusinessOutcomes = workReplay.PersistedWorkCount,
                        creationAuditEntries = workReplay.CreationAuditEntryCount,
                        retryReturnedSameWork = true,
                        result = "passed"
                    },
                    new
                    {
                        id = "populated-database-backup-restore-and-reconciliation",
                        generatedCounts = generated.ActualCounts,
                        sourceTableCount = sourceSnapshot.Count,
                        restoredTableCount = restoredSnapshot.Count,
                        inventoryTransactionsScanned = restoredReconciliation.TransactionsScanned,
                        reservationsScanned = restoredReconciliation.ReservationsScanned,
                        allocationsScanned = restoredReconciliation.AllocationsScanned,
                        reconciliationIssues = restoredReconciliation.IssueCount,
                        result = "passed"
                    },
                    new
                    {
                        id = "corrupt-and-unavailable-backups-fail-visibly",
                        corruptVerification = corruptedVerification.FailureReason,
                        missingVerification = missingVerification.FailureReason,
                        missingRestoreThrows = true,
                        result = "passed"
                    },
                    new
                    {
                        id = "incomplete-restore-rolls-back-and-fails-visibly",
                        failureCategory = failedRecovery.GetType().Name,
                        targetSchemaCreated = false,
                        result = "passed"
                    }
                },
                remainingGates = RemainingGates
            });
        }
        finally
        {
            try
            {
                foreach (var databaseName in createdDatabases)
                {
                    await DropDatabaseAsync(source.TestConnectionString, databaseName);
                }
            }
            finally
            {
                if (Directory.Exists(ownedRoot))
                {
                    Directory.Delete(ownedRoot, recursive: true);
                }
            }
        }
    }

    private static ServiceProvider CreateServiceProvider(
        PostgreSqlTestDatabase target,
        TestClock clock,
        IWebhookDeliveryTransport transport,
        IDataProtectionProvider? dataProtectionProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddSingleton<IClock>(clock);
        services.AddWmsInfrastructure(target.TestConnectionString, WmsDatabaseProvider.PostgreSql);
        services.AddWmsDesktopIdentity();
        services.AddWmsApplication();
        services.AddSingleton<IWmsBackupService, WmsDisabledBackupService>();
        services.AddSingleton(new WmsBackupOptions(
            Enabled: false,
            RootPath: string.Empty,
            OffsitePath: null,
            EncryptionKeyFile: string.Empty,
            RetentionDays: 30,
            MinimumRetainedBackups: 2,
            MaximumAgeHours: 36,
            DataProtectionKeysPath: null,
            AssetsPath: null));
        services.AddSingleton<IDataProtectionProvider>(
            dataProtectionProvider ?? new EphemeralDataProtectionProvider());
        services.AddSingleton<IWebhookDeliveryTransport>(transport);
        services.AddScoped<WmsJobRunner>();
        services.AddSingleton(new WmsBackgroundJobHealthState(enabled: true));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<WorkCommandReplayEvidence> RehearseWorkCommandResponseLossAsync(
        PostgreSqlTestDatabase target,
        DataGenerationActorCredentials actorCredentials)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero));
        await using var receiver = await ResilienceWebhookReceiver.StartAsync(target.TestConnectionString);
        using var httpClient = receiver.CreateHttpClient();
        var options = receiver.TransportOptions;
        var transport = new HttpWebhookDeliveryTransport(
            httpClient,
            options,
            ResilienceWebhookReceiver.CreateDestinationPolicy(options),
            NullLogger<HttpWebhookDeliveryTransport>.Instance);
        using var provider = CreateServiceProvider(target, clock, transport);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<WmsUser>>();
        var actor = await userManager.FindByIdAsync(actorCredentials.UserId);
        Assert.NotNull(actor);
        services.GetRequiredService<DesktopUserSession>().SignIn(actor);
        var context = services.GetRequiredService<WmsDbContext>();
        var warehouseId = await context.Warehouses.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var item = await context.Items.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.UnitOfMeasure })
            .FirstAsync();
        var creationKey = "issue-133-response-loss-" + Guid.NewGuid().ToString("N");
        var input = new WarehouseWorkInput(
            creationKey,
            WarehouseWorkType.Other,
            warehouseId,
            "Issue133Recovery",
            creationKey,
            QueueCode: "ISSUE133",
            MakeAvailable: true,
            Lines:
            [
                new WarehouseWorkLineInput(
                    1,
                    warehouseId,
                    item.Id,
                    1m,
                    item.UnitOfMeasure)
            ]);
        var workService = services.GetRequiredService<IWarehouseWorkService>();
        await Assert.ThrowsAsync<WorkCommandResponseLostException>(async () =>
        {
            var created = await workService.CreateAsync(input, actor.Id);
            Assert.True(created.IsSuccess, created.FirstError?.Message);
            throw new WorkCommandResponseLostException();
        });

        await using var verificationContext = target.CreateContext();
        var committed = await verificationContext.WarehouseWorks.AsNoTracking()
            .SingleAsync(value => value.CreationKey == creationKey);
        var replay = await workService.CreateAsync(input, actor.Id);
        Assert.True(replay.IsSuccess, replay.FirstError?.Message);
        Assert.Equal(committed.Id, replay.Value.Id);
        Assert.Equal(committed.WorkNumber, replay.Value.WorkNumber);
        var persistedWorkCount = await verificationContext.WarehouseWorks.AsNoTracking()
            .CountAsync(value => value.CreationKey == creationKey);
        var creationAuditEntryCount = await verificationContext.AuditEntries.AsNoTracking()
            .CountAsync(value =>
                value.Action == WmsAuditActions.WarehouseWorkCreated &&
                value.EntityId == committed.WorkNumber);
        Assert.Equal(1, persistedWorkCount);
        Assert.Equal(1, creationAuditEntryCount);

        return new WorkCommandReplayEvidence(
            "work-command-response-loss-after-commit-idempotent-retry",
            committed.Id,
            committed.WorkNumber,
            persistedWorkCount,
            creationAuditEntryCount);
    }

    private static WmsDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName))
            .Options;
        return new WmsDbContext(options);
    }

    private static async Task<SortedDictionary<string, long>> ReadDatabaseTableCountsAsync(
        string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var tableNames = new List<string>();
        await using (var tableCommand = new NpgsqlCommand(
                         "SELECT table_name FROM information_schema.tables " +
                         "WHERE table_schema = current_schema() AND table_type = 'BASE TABLE' " +
                         "ORDER BY table_name",
                         connection))
        await using (var reader = await tableCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        var quote = new NpgsqlCommandBuilder();
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var tableName in tableNames)
        {
            await using var countCommand = new NpgsqlCommand(
                $"SELECT COUNT(*) FROM {quote.QuoteIdentifier(tableName)}",
                connection);
            counts[tableName] = Convert.ToInt64(
                await countCommand.ExecuteScalarAsync(),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return counts;
    }

    private static async Task<InventoryReconciliationReportDto> ReconcileDatabaseAsync(
        string connectionString,
        DataGenerationActorCredentials actorCredentials)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton<IClock>(new TestClock(
            new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));
        services.AddWmsInfrastructure(connectionString, WmsDatabaseProvider.PostgreSql);
        services.AddWmsDesktopIdentity();
        services.AddWmsApplication();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var actor = await userManager.FindByIdAsync(actorCredentials.UserId);
        Assert.NotNull(actor);
        scope.ServiceProvider.GetRequiredService<DesktopUserSession>().SignIn(actor);

        var result = await scope.ServiceProvider.GetRequiredService<IInventoryReconciliationService>()
            .ReconcileAsync(new InventoryReconciliationQuery(Deep: true));
        Assert.True(result.IsSuccess, result.FirstError?.Message);
        return result.Value;
    }

    private static async Task<TransientOutboxFailureTrigger> CreateTransientOutboxDatabaseFailureTriggerAsync(
        PostgreSqlTestDatabase target,
        Guid eventId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var triggerName = "issue_133_fail_claim_" + suffix;
        var functionName = "issue_133_fail_claim_" + suffix;
        var quote = new NpgsqlCommandBuilder();
        await using var connection = new NpgsqlConnection(target.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            CREATE FUNCTION {quote.QuoteIdentifier(functionName)}() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."EventId" = '{eventId:D}'::uuid AND NEW."Status" = 'Processing' THEN
                    RAISE EXCEPTION 'issue_133_transient_database_failure' USING ERRCODE = '40001';
                END IF;
                RETURN NEW;
            END
            $$;
            CREATE TRIGGER {quote.QuoteIdentifier(triggerName)}
            BEFORE UPDATE OF "Status" ON "WmsIntegrationOutbox"
            FOR EACH ROW EXECUTE FUNCTION {quote.QuoteIdentifier(functionName)}();
            """,
            connection);
        await command.ExecuteNonQueryAsync();
        return new TransientOutboxFailureTrigger(triggerName, functionName);
    }

    private static async Task DropTransientOutboxDatabaseFailureTriggerAsync(
        PostgreSqlTestDatabase target,
        TransientOutboxFailureTrigger trigger)
    {
        if (!trigger.TriggerName.StartsWith("issue_133_fail_claim_", StringComparison.Ordinal) ||
            !trigger.FunctionName.StartsWith("issue_133_fail_claim_", StringComparison.Ordinal) ||
            !Guid.TryParseExact(trigger.TriggerName["issue_133_fail_claim_".Length..], "N", out _) ||
            !Guid.TryParseExact(trigger.FunctionName["issue_133_fail_claim_".Length..], "N", out _))
        {
            throw new ArgumentException("Only this test's generated database failure hook may be removed.", nameof(trigger));
        }

        var quote = new NpgsqlCommandBuilder();
        await using var connection = new NpgsqlConnection(target.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP TRIGGER {quote.QuoteIdentifier(trigger.TriggerName)} ON \"WmsIntegrationOutbox\"; " +
            $"DROP FUNCTION {quote.QuoteIdentifier(trigger.FunctionName)}()",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    private static async Task CreateDatabaseAsync(string sourceConnectionString, string databaseName)
    {
        ValidateRestoreDatabaseName(databaseName);
        var connectionString = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            SearchPath = "public",
            Pooling = false,
            ApplicationName = "WareCommand.Issue133.CreateRestoreDatabase"
        };
        await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(databaseName)}",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string sourceConnectionString, string databaseName)
    {
        ValidateRestoreDatabaseName(databaseName);
        var connectionString = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            SearchPath = "public",
            Pooling = false,
            ApplicationName = "WareCommand.Issue133.DropRestoreDatabase"
        };
        await using var connection = new NpgsqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        await using (var terminateCommand = new NpgsqlCommand(
                         "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                         "WHERE datname = @databaseName AND pid <> pg_backend_pid()",
                         connection))
        {
            terminateCommand.Parameters.AddWithValue("databaseName", databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(databaseName)}",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateRestoreDatabaseName(string databaseName)
    {
        if (!databaseName.StartsWith("wms_restore_", StringComparison.Ordinal) ||
            !Guid.TryParseExact(databaseName["wms_restore_".Length..], "N", out _))
        {
            throw new ArgumentException("Only this test's generated restore databases may be changed.", nameof(databaseName));
        }
    }

    private static async Task CorruptFinalByteAsync(string artifactPath)
    {
        await using var stream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            useAsync: true);
        Assert.True(stream.Length > 0, "A backup artifact must contain bytes before corruption is injected.");
        stream.Position = stream.Length - 1;
        var original = stream.ReadByte();
        Assert.InRange(original, 0, 255);
        stream.Position = stream.Length - 1;
        stream.WriteByte((byte)(original ^ 0x01));
        await stream.FlushAsync();
    }

    private static async Task CreateRestoreFailureEventTriggerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            CREATE FUNCTION public.issue_133_fail_restore_table() RETURNS event_trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'issue_133_restore_injected_failure';
            END
            $$;
            CREATE EVENT TRIGGER issue_133_fail_restore
            ON ddl_command_end WHEN TAG IN ('CREATE TABLE')
            EXECUTE FUNCTION public.issue_133_fail_restore_table();
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> DoesSchemaExistAsync(string connectionString, string schemaName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schemaName)",
            connection);
        command.Parameters.AddWithValue("schemaName", schemaName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task CreateProviderEvidenceTablesAsync(PostgreSqlTestDatabase target)
    {
        await using var context = target.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "ResilienceProviderRequests" (
                "DeliveryId" bigint PRIMARY KEY,
                "EventId" uuid NOT NULL,
                "EventType" text NOT NULL,
                "AttemptCount" integer NOT NULL);
            CREATE TABLE "ResilienceProviderCommits" (
                "DeliveryId" bigint PRIMARY KEY,
                "EventId" uuid NOT NULL,
                "EventType" text NOT NULL,
                "AppliedAtUtc" timestamp with time zone NOT NULL);
            """);
    }

    private static async Task CreateSubscriptionAsync(
        IServiceProvider root,
        string endpointUrl,
        string eventType,
        int maximumAttempts)
    {
        await using var scope = root.CreateAsyncScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionService>();
        var result = await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            $"Issue 133 {eventType}",
            endpointUrl,
            new HashSet<string>(StringComparer.Ordinal) { eventType },
            MaximumAttempts: maximumAttempts));
        Assert.True(result.Subscription.Id > 0);
    }

    private static async Task<Guid> CreateEventAsync(
        IServiceProvider root,
        string eventType,
        string correlationId)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var writer = services.GetRequiredService<IIntegrationEventWriter>();
        var context = services.GetRequiredService<WmsDbContext>();
        var eventId = Guid.NewGuid();
        await writer.EnqueueAsync(new IntegrationEventDraft(
            eventType,
            "ResilienceProbe",
            eventId.ToString("D"),
            new { scenario = correlationId, quantity = 1 },
            CorrelationId: correlationId,
            EventId: eventId));
        await context.SaveChangesAsync();
        return eventId;
    }

    private static async Task RunIntegrationRetryJobAsync(
        IServiceProvider root,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        await using var scope = root.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<WmsJobRunner>();
        await runner.ExecuteAsync(WmsJobEnvelope.Create(
            WmsJobNames.IntegrationRetries,
            idempotencyKey,
            correlationId: $"corr-{idempotencyKey}"),
            cancellationToken);
    }

    private static async Task<OutboxSnapshot> ReadOutboxAsync(
        PostgreSqlTestDatabase target,
        Guid eventId)
    {
        await using var context = target.CreateContext();
        var message = await context.IntegrationOutbox.AsNoTracking()
            .SingleAsync(value => value.EventId == eventId);
        var delivery = await context.WebhookDeliveries.AsNoTracking()
            .SingleAsync(value => value.OutboxMessageId == message.Id);
        return new OutboxSnapshot(
            message.Id,
            message.EventId,
            message.CorrelationId,
            message.Status,
            message.AttemptCount,
            message.LeaseUntilUtc,
            delivery.Id,
            delivery.Status,
            delivery.AttemptCount,
            delivery.NextAttemptAtUtc,
            delivery.LeaseUntilUtc,
            delivery.ResponseStatusCode,
            delivery.ResponseBody,
            delivery.LastError);
    }

    private static async Task<OutboxMessageSnapshot> ReadOutboxMessageAsync(
        PostgreSqlTestDatabase target,
        Guid eventId)
    {
        await using var context = target.CreateContext();
        var message = await context.IntegrationOutbox.AsNoTracking()
            .SingleAsync(value => value.EventId == eventId);
        return new OutboxMessageSnapshot(
            message.Id,
            message.Status,
            message.AttemptCount,
            message.LeaseUntilUtc);
    }

    private static async Task<int> CountWebhookDeliveriesAsync(
        PostgreSqlTestDatabase target,
        long outboxMessageId)
    {
        await using var context = target.CreateContext();
        return await context.WebhookDeliveries.AsNoTracking()
            .CountAsync(value => value.OutboxMessageId == outboxMessageId);
    }

    private static async Task<AuditEntry> ReadReplayAuditAsync(
        PostgreSqlTestDatabase target,
        long outboxMessageId)
    {
        await using var context = target.CreateContext();
        return await context.AuditEntries.AsNoTracking()
            .SingleAsync(value =>
                value.Action == WmsAuditActions.IntegrationDeadLetterReplayed &&
                value.EntityId == outboxMessageId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<JobExecutionSnapshot> ReadJobExecutionAsync(
        PostgreSqlTestDatabase target,
        string idempotencyKey)
    {
        await using var context = target.CreateContext();
        var execution = await context.JobExecutions.AsNoTracking()
            .SingleAsync(value => value.JobName == WmsJobNames.IntegrationRetries &&
                                  value.IdempotencyKey == idempotencyKey);
        return new JobExecutionSnapshot(execution.Status, execution.AttemptCount);
    }

    private static async Task<int> CountProviderRequestsAsync(
        PostgreSqlTestDatabase target,
        long deliveryId)
    {
        await using var connection = new NpgsqlConnection(target.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(\"AttemptCount\"), 0) FROM \"ResilienceProviderRequests\" WHERE \"DeliveryId\" = @deliveryId",
            connection);
        command.Parameters.AddWithValue("deliveryId", deliveryId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task<int> CountProviderCommitsAsync(
        PostgreSqlTestDatabase target,
        long deliveryId) =>
        CountProviderRowsAsync(target, "ResilienceProviderCommits", deliveryId);

    private static async Task<int> CountProviderRowsAsync(
        PostgreSqlTestDatabase target,
        string table,
        long deliveryId)
    {
        await using var connection = new NpgsqlConnection(target.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM \"{table}\" WHERE \"DeliveryId\" = @deliveryId",
            connection);
        command.Parameters.AddWithValue("deliveryId", deliveryId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static readonly object EvidenceGate = new();

    private static void WriteEvidence(object evidence)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        lock (EvidenceGate)
        {
            var reports = new List<JsonElement>();
            if (File.Exists(fullPath))
            {
                using var existing = JsonDocument.Parse(File.ReadAllText(fullPath));
                if (existing.RootElement.ValueKind == JsonValueKind.Array)
                {
                    reports.AddRange(existing.RootElement.EnumerateArray().Select(value => value.Clone()));
                }
                else if (existing.RootElement.ValueKind == JsonValueKind.Object)
                {
                    reports.Add(existing.RootElement.Clone());
                }
            }

            reports.Add(JsonSerializer.SerializeToElement(evidence));
            File.WriteAllText(fullPath, JsonSerializer.Serialize(reports, EvidenceJsonOptions));
        }
    }

    private sealed record OutboxSnapshot(
        long Id,
        Guid EventId,
        string CorrelationId,
        string Status,
        int MessageAttemptCount,
        DateTimeOffset? MessageLeaseUntilUtc,
        long DeliveryId,
        string DeliveryStatus,
        int DeliveryAttemptCount,
        DateTimeOffset? DeliveryNextAttemptAtUtc,
        DateTimeOffset? DeliveryLeaseUntilUtc,
        int? ResponseStatusCode,
        string? ResponseBody,
        string? DeliveryError);

    private sealed record OutboxMessageSnapshot(
        long Id,
        string Status,
        int MessageAttemptCount,
        DateTimeOffset? MessageLeaseUntilUtc);

    private sealed record WorkCommandReplayEvidence(
        string Scenario,
        int WorkId,
        string WorkNumber,
        int PersistedWorkCount,
        int CreationAuditEntryCount);

    private sealed class WorkCommandResponseLostException : Exception;

    private sealed record JobExecutionSnapshot(string Status, int AttemptCount);

    private sealed record TransientOutboxFailureTrigger(string TriggerName, string FunctionName);

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Wms.ASP.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ResilienceWebhookReceiver(
        WebApplication application,
        Uri baseAddress,
        TaskCompletionSource<long> firstProviderCommit) : IAsyncDisposable
    {
        public string EndpointUrl => new Uri(baseAddress, "/events").ToString();

        public IOptions<WebhookDeliveryTransportOptions> TransportOptions => OptionsFor(baseAddress);

        public static async Task<ResilienceWebhookReceiver> StartAsync(
            string connectionString,
            string? holdAfterCommitEventType = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            var firstCommit = new TaskCompletionSource<long>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            app.Run(context => HandleRequestAsync(
                context,
                connectionString,
                firstCommit,
                holdAfterCommitEventType));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
                ?? throw new InvalidOperationException("The disposable resilience receiver did not bind an address.");
            return new ResilienceWebhookReceiver(app, new Uri(address), firstCommit);
        }

        public Task<long> WaitForFirstProviderCommitAsync(TimeSpan timeout) =>
            firstProviderCommit.Task.WaitAsync(timeout);

        public HttpClient CreateHttpClient()
        {
            var options = TransportOptions;
            var policy = CreateDestinationPolicy(options);
            return new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = policy.ConnectAsync,
                ConnectTimeout = TimeSpan.FromSeconds(2),
                UseCookies = false,
                UseProxy = false
            })
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        public static WebhookDestinationPolicy CreateDestinationPolicy(
            IOptions<WebhookDeliveryTransportOptions> options) =>
            new(options, new TestHostEnvironment(), new SystemWebhookDnsResolver());

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }

        private static IOptions<WebhookDeliveryTransportOptions> OptionsFor(Uri baseAddress) =>
            Microsoft.Extensions.Options.Options.Create(new WebhookDeliveryTransportOptions
            {
                Enabled = true,
                AllowedHosts = [baseAddress.Host],
                AllowedSchemes = [Uri.UriSchemeHttp],
                AllowedPorts = [baseAddress.Port],
                AllowLocalHttpForDevelopment = true,
                RequestTimeoutSeconds = 5,
                ConnectTimeoutSeconds = 2
            });

        private static async Task HandleRequestAsync(
            HttpContext context,
            string connectionString,
            TaskCompletionSource<long> firstCommit,
            string? holdAfterCommitEventType)
        {
            var deliveryId = long.Parse(
                context.Request.Headers[WebhookSignature.DeliveryIdHeaderName].ToString(),
                System.Globalization.CultureInfo.InvariantCulture);
            var eventId = Guid.Parse(context.Request.Headers[WebhookSignature.EventIdHeaderName].ToString());
            var eventType = context.Request.Headers[WebhookSignature.EventTypeHeaderName].ToString();
            await using var requestConnection = new NpgsqlConnection(connectionString);
            await requestConnection.OpenAsync(context.RequestAborted);
            await using var requestCommand = new NpgsqlCommand(
                """
                INSERT INTO "ResilienceProviderRequests" ("DeliveryId", "EventId", "EventType", "AttemptCount")
                VALUES (@deliveryId, @eventId, @eventType, 1)
                ON CONFLICT ("DeliveryId") DO UPDATE
                SET "AttemptCount" = "ResilienceProviderRequests"."AttemptCount" + 1
                WHERE "ResilienceProviderRequests"."EventId" = EXCLUDED."EventId"
                  AND "ResilienceProviderRequests"."EventType" = EXCLUDED."EventType"
                RETURNING "AttemptCount";
                """,
                requestConnection);
            requestCommand.Parameters.AddWithValue("deliveryId", deliveryId);
            requestCommand.Parameters.AddWithValue("eventId", eventId);
            requestCommand.Parameters.AddWithValue("eventType", eventType);
            var attempt = Convert.ToInt32(
                await requestCommand.ExecuteScalarAsync(context.RequestAborted),
                System.Globalization.CultureInfo.InvariantCulture);

            if (eventType == WmsIntegrationEventTypes.ItemChanged && attempt == 1)
            {
                context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                await context.Response.WriteAsync("receiver-private-response-must-not-be-stored", context.RequestAborted);
                return;
            }

            if ((eventType == WmsIntegrationEventTypes.WarehouseChanged && attempt == 1) ||
                eventType == WmsIntegrationEventTypes.WorkLifecycleChanged)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("receiver-private-response-must-not-be-stored", context.RequestAborted);
                return;
            }

            await using (var commitCommand = new NpgsqlCommand(
                """
                INSERT INTO "ResilienceProviderCommits" ("DeliveryId", "EventId", "EventType", "AppliedAtUtc")
                VALUES (@deliveryId, @eventId, @eventType, @appliedAtUtc)
                ON CONFLICT ("DeliveryId") DO NOTHING;
                """,
                requestConnection))
            {
                commitCommand.Parameters.AddWithValue("deliveryId", deliveryId);
                commitCommand.Parameters.AddWithValue("eventId", eventId);
                commitCommand.Parameters.AddWithValue("eventType", eventType);
                commitCommand.Parameters.AddWithValue("appliedAtUtc", DateTimeOffset.UtcNow);
                await commitCommand.ExecuteNonQueryAsync(context.RequestAborted);
            }

            firstCommit.TrySetResult(deliveryId);

            if (eventType == holdAfterCommitEventType && attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
                return;
            }

            if (eventType == WmsIntegrationEventTypes.InventoryMovementRecorded && attempt == 1)
            {
                context.Abort();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("accepted", context.RequestAborted);
        }
    }
}
