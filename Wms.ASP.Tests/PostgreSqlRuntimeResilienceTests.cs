using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Wms.Application.Backups;
using Wms.Application.Context;
using Wms.Application.DependencyInjection;
using Wms.Application.Integrations;
using Wms.Application.Jobs;
using Wms.Application.Telemetry;
using Wms.ASP.Jobs;
using Wms.Infrastructure.Backups;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;
using Wms.Infrastructure.Integrations;
using Wms.Infrastructure.Tests.Integration;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlRuntimeResilienceTests
{
    private static readonly string[] RemainingGates =
    [
        "worker process interruption with an outbox processing lease and lease-expiry reclaim",
        "authorized dead-letter replay path and audit",
        "transient PostgreSQL/network dependency failure during work execution",
        "populated PostgreSQL backup restore into a fresh isolated database with full reconciliation",
        "corrupted and unavailable backup failure rehearsal"
    ];

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
        scenarios.Add(new
        {
            id = "permanent-provider-failure-dead-letter",
            eventId = permanentFailureEventId,
            deliveryId = permanentFailure.DeliveryId,
            correlationId = permanentFailure.CorrelationId,
            outboxStatus = permanentFailure.Status,
            deliveryStatus = permanentFailure.DeliveryStatus,
            deliveryAttempts = permanentFailure.DeliveryAttemptCount,
            providerRequests = await CountProviderRequestsAsync(target, permanentFailure.DeliveryId),
            providerCommits = await CountProviderCommitsAsync(target, permanentFailure.DeliveryId),
            boundedAndRedactedDiagnostics = permanentFailure.DeliveryError,
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
                faultInjectionMode = "external-loopback-http-provider",
                productionFaultInjectionEnabled = false
            },
            scenarios,
            remainingGates = RemainingGates
        });
    }

    private static ServiceProvider CreateServiceProvider(
        PostgreSqlTestDatabase target,
        TestClock clock,
        IWebhookDeliveryTransport transport)
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
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<IWebhookDeliveryTransport>(transport);
        services.AddScoped<WmsJobRunner>();
        services.AddSingleton(new WmsBackgroundJobHealthState(enabled: true));
        return services.BuildServiceProvider(validateScopes: true);
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
        await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            $"Issue 133 {eventType}",
            endpointUrl,
            new HashSet<string>(StringComparer.Ordinal) { eventType },
            MaximumAttempts: maximumAttempts));
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
        string idempotencyKey)
    {
        await using var scope = root.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<WmsJobRunner>();
        await runner.ExecuteAsync(WmsJobEnvelope.Create(
            WmsJobNames.IntegrationRetries,
            idempotencyKey,
            correlationId: $"corr-{idempotencyKey}"));
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
            delivery.Id,
            delivery.Status,
            delivery.AttemptCount,
            delivery.NextAttemptAtUtc,
            delivery.ResponseStatusCode,
            delivery.ResponseBody,
            delivery.LastError);
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

    private static void WriteEvidence(object evidence)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_RESILIENCE_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(evidence, EvidenceJsonOptions));
    }

    private sealed record OutboxSnapshot(
        long Id,
        Guid EventId,
        string CorrelationId,
        string Status,
        long DeliveryId,
        string DeliveryStatus,
        int DeliveryAttemptCount,
        DateTimeOffset? DeliveryNextAttemptAtUtc,
        int? ResponseStatusCode,
        string? ResponseBody,
        string? DeliveryError);

    private sealed record JobExecutionSnapshot(string Status, int AttemptCount);

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
        Uri baseAddress) : IAsyncDisposable
    {
        public string EndpointUrl => new Uri(baseAddress, "/events").ToString();

        public IOptions<WebhookDeliveryTransportOptions> TransportOptions => OptionsFor(baseAddress);

        public static async Task<ResilienceWebhookReceiver> StartAsync(string connectionString)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.Run(context => HandleRequestAsync(context, connectionString));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
                ?? throw new InvalidOperationException("The disposable resilience receiver did not bind an address.");
            return new ResilienceWebhookReceiver(app, new Uri(address));
        }

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

        private static async Task HandleRequestAsync(HttpContext context, string connectionString)
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

            if (eventType == WmsIntegrationEventTypes.ItemChanged)
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
