using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Integrations;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Tests.Integrations;

public sealed class IntegrationDeliveryTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly IntegrationEventWriter _writer;
    private readonly WmsRequestContext _requestContext = new("Test");
    private readonly WmsWarehouseContext _warehouseContext = new();
    private readonly DataProtectionWebhookSecretProtector _secretProtector;

    public IntegrationDeliveryTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        _secretProtector = new DataProtectionWebhookSecretProtector(
            new EphemeralDataProtectionProvider());
        _writer = new IntegrationEventWriter(
            _context,
            _clock,
            _requestContext,
            _warehouseContext);
    }

    public async Task InitializeAsync() => await _context.Database.EnsureCreatedAsync();

    public async Task DisposeAsync() => await _context.DisposeAsync();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OutboxEventCommitsAndRollsBackWithTheOwningTransaction()
    {
        var rolledBackEventId = Guid.NewGuid();
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await _writer.EnqueueAsync(CreateDraft(rolledBackEventId));
            await _context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await _context.IntegrationOutbox.CountAsync()).Should().Be(0);
        _context.ChangeTracker.Clear();

        var committedEventId = Guid.NewGuid();
        await using (var transaction = await _context.Database.BeginTransactionAsync())
        {
            await _writer.EnqueueAsync(CreateDraft(committedEventId));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var committed = await _context.IntegrationOutbox.SingleAsync();
        committed.EventId.Should().Be(committedEventId);
        committed.Status.Should().Be(WmsIntegrationEventStatuses.Pending);
    }

    [Fact]
    public async Task InboxProcessesAnExternalMessageOnlyOnceAndRejectsHashReuse()
    {
        var inbox = new IntegrationInboxService(_context);
        var request = new IntegrationInboxRequest(
            "erp",
            "message-001",
            WmsIntegrationEventTypes.ItemChanged,
            "{\"sku\":\"A-1\"}");

        var first = await inbox.TryBeginAsync(request, _clock.UtcNow);
        first.ShouldProcess.Should().BeTrue();
        first.WasDuplicate.Should().BeFalse();

        await inbox.CompleteAsync(first.MessageId, _clock.UtcNow.AddSeconds(1));
        var duplicate = await inbox.TryBeginAsync(request, _clock.UtcNow.AddSeconds(2));
        duplicate.ShouldProcess.Should().BeFalse();
        duplicate.WasDuplicate.Should().BeTrue();

        var reused = request with { PayloadJson = "{\"sku\":\"A-2\"}" };
        var act = () => inbox.TryBeginAsync(reused, _clock.UtcNow.AddSeconds(3));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different payload*");
    }

    [Fact]
    public async Task DispatcherCreatesScopedDeliveryAndSignsPayload()
    {
        var subscriptionService = new WebhookSubscriptionService(
            _context,
            _secretProtector,
            _clock);
        var issue = await subscriptionService.CreateAsync(
            new WebhookSubscriptionCreateRequest(
                "ERP",
                "https://erp.example.test/wms/events",
                [WmsIntegrationEventTypes.InventoryMovementRecorded],
                [17]));

        _warehouseContext.SetWarehouse(17);
        var eventId = Guid.NewGuid();
        await _writer.EnqueueAsync(CreateDraft(eventId) with { WarehouseId = 17 });
        await _context.SaveChangesAsync();

        var transport = new CapturingWebhookTransport();
        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync();

        result.EventsClaimed.Should().Be(1);
        result.DeliveriesAttempted.Should().Be(1);
        result.DeliveriesSucceeded.Should().Be(1);
        result.DeliveriesFailed.Should().Be(0);
        transport.Requests.Should().ContainSingle();
        var sent = transport.Requests[0];
        sent.SubscriptionId.Should().Be(issue.Subscription.Id);
        sent.EventId.Should().Be(eventId);
        sent.DeliveryId.Should().BeGreaterThan(0);
        sent.PayloadVersion.Should().Be(1);
        sent.Headers[WebhookSignature.EventIdHeaderName].Should().Be(eventId.ToString("D"));
        sent.Headers[WebhookSignature.DeliveryIdHeaderName]
            .Should().Be(sent.DeliveryId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        sent.Headers[WebhookSignature.EventTypeHeaderName]
            .Should().Be(WmsIntegrationEventTypes.InventoryMovementRecorded);
        sent.Headers[WebhookSignature.VersionHeaderName].Should().Be("1");
        WebhookSignature.Verify(
                issue.Secret,
                sent.PayloadJson,
                sent.Headers[WebhookSignature.HeaderName],
                _clock.UtcNow)
            .Should().BeTrue();

        (await _context.IntegrationOutbox.SingleAsync()).Status
            .Should().Be(WmsIntegrationEventStatuses.Delivered);
        (await _context.WebhookDeliveries.SingleAsync()).Status
            .Should().Be(WmsWebhookDeliveryStatuses.Delivered);
    }

    [Fact]
    public async Task NotificationWebhookUsesOutboxAndMarksTransportAcceptanceOnlyAfterHttpSuccess()
    {
        var subscriptions = new WebhookSubscriptionService(_context, _secretProtector, _clock);
        await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Warehouse 17 notifications",
            "https://receiver.example.test/notifications",
            [WmsIntegrationEventTypes.NotificationPublished],
            [17]));
        var (notification, recipient) = await AddQueuedWebhookRecipientAsync(warehouseId: 17);
        var transport = new CapturingWebhookTransport();
        var adapter = new IntegrationWebhookNotificationAdapter(_context, _writer, transport);

        var enqueued = await adapter.DeliverAsync(CreateNotificationMessage(notification, recipient));
        enqueued.Succeeded.Should().BeTrue();
        enqueued.SuccessStatus.Should().Be(NotificationDeliveryStatus.Queued);
        transport.Requests.Should().BeEmpty();
        recipient.DeliveryStatus = NotificationDeliveryStatus.Queued;
        await _context.SaveChangesAsync();

        var outbox = await _context.IntegrationOutbox.SingleAsync();
        outbox.EventType.Should().Be(WmsIntegrationEventTypes.NotificationPublished);
        outbox.AggregateKey.Should().Be(notification.Id.ToString(CultureInfo.InvariantCulture));
        outbox.PayloadJson.Should().NotContain("private-user");
        outbox.PayloadJson.Should().NotContain("private@example.test");

        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);
        var dispatched = await dispatcher.DispatchAsync();

        dispatched.DeliveriesSucceeded.Should().Be(1);
        transport.Requests.Should().ContainSingle();
        (await _context.IntegrationOutbox.SingleAsync()).Status.Should().Be(WmsIntegrationEventStatuses.Delivered);
        (await _context.NotificationRecipients.SingleAsync()).DeliveryStatus
            .Should().Be(NotificationDeliveryStatus.TransportAccepted);
    }

    [Fact]
    public async Task NotificationWebhookIsDeliveredToTheControlledHttpReceiverThroughTheSharedTransport()
    {
        string? receivedPayload = null;
        Dictionary<string, string>? receivedHeaders = null;
        await using var receiver = await WebhookReceiver.StartAsync(async request =>
        {
            using var reader = new StreamReader(request.Request.Body, Encoding.UTF8);
            receivedPayload = await reader.ReadToEndAsync(request.RequestAborted);
            receivedHeaders = request.Request.Headers
                .Where(header => header.Key.StartsWith("X-WareCommand-", StringComparison.Ordinal))
                .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            request.Response.StatusCode = StatusCodes.Status202Accepted;
        });
        var options = Options.Create(new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = [receiver.BaseAddress.Host],
            AllowedSchemes = [Uri.UriSchemeHttp],
            AllowedPorts = [receiver.BaseAddress.Port],
            AllowLocalHttpForDevelopment = true
        });
        var destinationPolicy = new WebhookDestinationPolicy(
            options,
            new TestHostEnvironment(Environments.Development),
            new SystemWebhookDnsResolver());
        using var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = destinationPolicy.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            UseCookies = false,
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var transport = new HttpWebhookDeliveryTransport(
            httpClient,
            options,
            destinationPolicy,
            NullLogger<HttpWebhookDeliveryTransport>.Instance);
        var subscriptions = new WebhookSubscriptionService(_context, _secretProtector, _clock);
        var issue = await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Controlled notification receiver",
            new Uri(receiver.BaseAddress, "/notifications").ToString(),
            [WmsIntegrationEventTypes.NotificationPublished],
            [17]));
        var (notification, recipient) = await AddQueuedWebhookRecipientAsync(warehouseId: 17);
        var adapter = new IntegrationWebhookNotificationAdapter(_context, _writer, transport);
        var queued = await adapter.DeliverAsync(CreateNotificationMessage(notification, recipient));
        queued.SuccessStatus.Should().Be(NotificationDeliveryStatus.Queued);
        recipient.DeliveryStatus = NotificationDeliveryStatus.Queued;
        await _context.SaveChangesAsync();
        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);

        var dispatched = await dispatcher.DispatchAsync();

        dispatched.DeliveriesSucceeded.Should().Be(1);
        receivedPayload.Should().Contain(notification.Id.ToString(CultureInfo.InvariantCulture));
        receivedHeaders.Should().NotBeNull();
        receivedHeaders![WebhookSignature.EventTypeHeaderName]
            .Should().Be(WmsIntegrationEventTypes.NotificationPublished);
        WebhookSignature.Verify(
                issue.Secret,
                receivedPayload!,
                receivedHeaders[WebhookSignature.HeaderName],
                _clock.UtcNow)
            .Should().BeTrue();
        (await _context.NotificationRecipients.SingleAsync()).DeliveryStatus
            .Should().Be(NotificationDeliveryStatus.TransportAccepted);
    }

    [Fact]
    public async Task NotificationWebhookRejectsGlobalAndCrossWarehouseSubscriptions()
    {
        var subscriptions = new WebhookSubscriptionService(_context, _secretProtector, _clock);
        await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Global notification endpoint",
            "https://global.example.test/notifications",
            [WmsIntegrationEventTypes.NotificationPublished]));
        await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Warehouse 18 endpoint",
            "https://warehouse18.example.test/notifications",
            [WmsIntegrationEventTypes.NotificationPublished],
            [18]));
        var (notification, recipient) = await AddQueuedWebhookRecipientAsync(warehouseId: 17);
        var transport = new CapturingWebhookTransport();
        await _writer.EnqueueAsync(new IntegrationEventDraft(
            WmsIntegrationEventTypes.NotificationPublished,
            "Notification",
            notification.Id.ToString(CultureInfo.InvariantCulture),
            new { notificationId = notification.Id, warehouseId = 17 },
            WarehouseId: 17,
            EventId: Guid.NewGuid()));
        recipient.DeliveryStatus = NotificationDeliveryStatus.Queued;
        await _context.SaveChangesAsync();
        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync();

        result.EventsClaimed.Should().Be(1);
        result.DeliveriesAttempted.Should().Be(0);
        transport.Requests.Should().BeEmpty();
        (await _context.IntegrationOutbox.SingleAsync()).Status.Should().Be(WmsIntegrationEventStatuses.NoSubscribers);
        (await _context.NotificationRecipients.SingleAsync()).DeliveryStatus.Should().Be(NotificationDeliveryStatus.Disabled);
    }

    [Fact]
    public async Task NotificationWebhookRemainsQueuedAcrossRestartAndUsesStableRetryIdentity()
    {
        var subscriptions = new WebhookSubscriptionService(_context, _secretProtector, _clock);
        await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Warehouse 17 notifications",
            "https://receiver.example.test/notifications",
            [WmsIntegrationEventTypes.NotificationPublished],
            [17]));
        var (notification, recipient) = await AddQueuedWebhookRecipientAsync(warehouseId: 17);
        var transport = new SequencedWebhookTransport(
            new WebhookDeliveryResult(false, true, ResponseStatusCode: 503, Error: "temporary_failure"),
            new WebhookDeliveryResult(true, false, ResponseStatusCode: 202));
        var adapter = new IntegrationWebhookNotificationAdapter(_context, _writer, transport);
        var queued = await adapter.DeliverAsync(CreateNotificationMessage(notification, recipient));
        queued.SuccessStatus.Should().Be(NotificationDeliveryStatus.Queued);
        recipient.DeliveryStatus = NotificationDeliveryStatus.Queued;
        await _context.SaveChangesAsync();

        var firstDispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);
        var first = await firstDispatcher.DispatchAsync();
        first.DeliveriesFailed.Should().Be(1);
        (await _context.NotificationRecipients.SingleAsync()).DeliveryStatus
            .Should().Be(NotificationDeliveryStatus.Queued);
        var pending = await _context.IntegrationOutbox.SingleAsync();
        pending.Status.Should().Be(WmsIntegrationEventStatuses.Failed);
        var eventId = transport.Requests[0].EventId;
        var deliveryId = transport.Requests[0].DeliveryId;

        _context.ChangeTracker.Clear();
        _clock.UtcNow = pending.NextAttemptAtUtc!.Value;
        var resumedDispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);
        var resumed = await resumedDispatcher.DispatchAsync();

        resumed.DeliveriesSucceeded.Should().Be(1);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(request => request.EventId == eventId);
        transport.Requests.Should().OnlyContain(request => request.DeliveryId == deliveryId);
        (await _context.IntegrationOutbox.SingleAsync()).Status.Should().Be(WmsIntegrationEventStatuses.Delivered);
        (await _context.NotificationRecipients.SingleAsync()).DeliveryStatus
            .Should().Be(NotificationDeliveryStatus.TransportAccepted);
    }

    private async Task<(WmsNotificationEntity Notification, WmsNotificationRecipientEntity Recipient)>
        AddQueuedWebhookRecipientAsync(int warehouseId)
    {
        var notification = new WmsNotificationEntity
        {
            DeduplicationKey = $"test:notification:{Guid.NewGuid():N}",
            Kind = "stock.low",
            Severity = NotificationSeverity.Warning,
            TitleEn = "Low stock",
            TitleAr = "مخزون منخفض",
            MessageEn = "Stock is low.",
            MessageAr = "المخزون منخفض.",
            WarehouseId = warehouseId,
            CreatedAtUtc = _clock.UtcNow,
            CorrelationId = "notification-test"
        };
        var recipient = new WmsNotificationRecipientEntity
        {
            RecipientUserId = "private-user",
            RecipientEmail = "private@example.test",
            Channel = NotificationChannel.Webhook,
            DeliveryStatus = NotificationDeliveryStatus.Pending,
            CreatedAtUtc = _clock.UtcNow
        };
        notification.Recipients.Add(recipient);
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
        return (notification, recipient);
    }

    private static NotificationDeliveryMessage CreateNotificationMessage(
        WmsNotificationEntity notification,
        WmsNotificationRecipientEntity recipient) =>
        new(
            notification.Id,
            recipient.Id,
            recipient.RecipientUserId,
            recipient.RecipientEmail,
            notification.Kind,
            notification.Severity,
            notification.TitleEn,
            notification.TitleAr,
            notification.MessageEn,
            notification.MessageAr,
            notification.DeepLink,
            notification.WarehouseId,
            notification.Mandatory,
            notification.CorrelationId,
            "en-US");

    [Fact]
    public async Task HttpDispatcherHonorsRetryAfterUsesRotatedSecretAndKeepsResponseOutOfDeliveryRecord()
    {
        var attempts = 0;
        var businessMutationCount = 0;
        var appliedEventIds = new HashSet<string>(StringComparer.Ordinal);
        var requests = new List<(byte[] Body, Dictionary<string, string> Headers)>();
        await using var receiver = await WebhookReceiver.StartAsync(async context =>
        {
            await using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            requests.Add((
                body.ToArray(),
                context.Request.Headers.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase)));

            var attempt = Interlocked.Increment(ref attempts);
            var receivedEventId = context.Request.Headers[WebhookSignature.EventIdHeaderName].ToString();
            if (attempt == 1)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = _clock.UtcNow
                    .AddMinutes(30)
                    .ToString("r", CultureInfo.InvariantCulture);
                await context.Response.WriteAsync("sensitive receiver response", context.RequestAborted);
                return;
            }

            if (appliedEventIds.Add(receivedEventId))
            {
                Interlocked.Increment(ref businessMutationCount);
            }

            if (attempt == 2)
            {
                context.Abort();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsync("accepted", context.RequestAborted);
        });

        var options = Options.Create(new WebhookDeliveryTransportOptions
        {
            Enabled = true,
            AllowedHosts = [receiver.BaseAddress.Host],
            AllowedSchemes = [Uri.UriSchemeHttp],
            AllowedPorts = [receiver.BaseAddress.Port],
            AllowLocalHttpForDevelopment = true
        });
        var destinationPolicy = new WebhookDestinationPolicy(
            options,
            new TestHostEnvironment(Environments.Development),
            new SystemWebhookDnsResolver());
        using var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = destinationPolicy.ConnectAsync,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            UseCookies = false,
            UseProxy = false
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var transport = new HttpWebhookDeliveryTransport(
            httpClient,
            options,
            destinationPolicy,
            NullLogger<HttpWebhookDeliveryTransport>.Instance);
        var subscriptions = new WebhookSubscriptionService(_context, _secretProtector, _clock);
        var issue = await subscriptions.CreateAsync(new WebhookSubscriptionCreateRequest(
            "Local receiver",
            new Uri(receiver.BaseAddress, "/events").ToString(),
            [WmsIntegrationEventTypes.InventoryMovementRecorded]));
        var rotation = await subscriptions.RotateSecretAsync(issue.Subscription.Id, TimeSpan.FromHours(2));
        var eventId = Guid.NewGuid();
        await _writer.EnqueueAsync(CreateDraft(eventId));
        await _context.SaveChangesAsync();

        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);

        var firstDispatch = await dispatcher.DispatchAsync();
        firstDispatch.DeliveriesAttempted.Should().Be(1);
        firstDispatch.DeliveriesFailed.Should().Be(1);
        var failedDelivery = await _context.WebhookDeliveries.SingleAsync();
        failedDelivery.Status.Should().Be(WmsWebhookDeliveryStatuses.Failed);
        failedDelivery.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddMinutes(30));
        failedDelivery.ResponseStatusCode.Should().Be(429);
        failedDelivery.ResponseBody.Should().BeNull();
        (await _context.IntegrationOutbox.SingleAsync()).NextAttemptAtUtc
            .Should().Be(failedDelivery.NextAttemptAtUtc);

        var prematureDispatch = await dispatcher.DispatchAsync();
        prematureDispatch.EventsClaimed.Should().Be(0);
        attempts.Should().Be(1);

        _clock.UtcNow = failedDelivery.NextAttemptAtUtc!.Value;
        var responseLossDispatch = await dispatcher.DispatchAsync();
        responseLossDispatch.DeliveriesAttempted.Should().Be(1);
        responseLossDispatch.DeliveriesFailed.Should().Be(1);
        attempts.Should().Be(2);
        var responseLossDelivery = await _context.WebhookDeliveries.SingleAsync();
        responseLossDelivery.Status.Should().Be(WmsWebhookDeliveryStatuses.Failed);
        responseLossDelivery.LastError.Should().Be("webhook_connection_failed");
        responseLossDelivery.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddMinutes(2));
        responseLossDelivery.ResponseBody.Should().BeNull();

        _context.ChangeTracker.Clear();
        var resumedDispatcher = new IntegrationOutboxDispatcher(
            _context,
            transport,
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);
        var earlyRetry = await resumedDispatcher.DispatchAsync();
        earlyRetry.EventsClaimed.Should().Be(0);
        attempts.Should().Be(2);

        _clock.UtcNow = responseLossDelivery.NextAttemptAtUtc!.Value;
        var retryDispatch = await resumedDispatcher.DispatchAsync();
        retryDispatch.DeliveriesAttempted.Should().Be(1);
        retryDispatch.DeliveriesSucceeded.Should().Be(1);
        attempts.Should().Be(3);
        businessMutationCount.Should().Be(1);

        var deliveredDelivery = await _context.WebhookDeliveries.SingleAsync();
        deliveredDelivery.Status.Should().Be(WmsWebhookDeliveryStatuses.Delivered);
        deliveredDelivery.AttemptCount.Should().Be(3);
        deliveredDelivery.ResponseBody.Should().BeNull();
        (await _context.IntegrationOutbox.SingleAsync()).Status
            .Should().Be(WmsIntegrationEventStatuses.Delivered);
        requests.Should().HaveCount(3);
        requests[0].Body.Should().Equal(requests[1].Body);
        requests[1].Body.Should().Equal(requests[2].Body);
        requests.Should().OnlyContain(request =>
            request.Headers[WebhookSignature.EventIdHeaderName] == eventId.ToString("D"));
        requests.Select(request => request.Headers[WebhookSignature.DeliveryIdHeaderName])
            .Distinct(StringComparer.Ordinal)
            .Should().ContainSingle();
        var transmittedPayload = Encoding.UTF8.GetString(requests[2].Body);
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(
            requests[2].Headers[WebhookSignature.TimestampHeaderName],
            CultureInfo.InvariantCulture));
        WebhookSignature.Verify(
                rotation.Secret,
                transmittedPayload,
                requests[2].Headers[WebhookSignature.HeaderName],
                signedAt)
            .Should().BeTrue();
        WebhookSignature.Verify(
                issue.Secret,
                transmittedPayload,
                requests[2].Headers[WebhookSignature.HeaderName],
                signedAt)
            .Should().BeFalse();
        var verification = await subscriptions.GetDeliveryVerificationSummaryAsync();
        verification.ActiveSubscriptions.Should().Be(1);
        verification.VerifiedSubscriptions.Should().Be(1);
    }

    [Fact]
    public async Task WebhookSecretRotationAdvancesVersionAndKeepsExplicitOverlap()
    {
        var subscriptionService = new WebhookSubscriptionService(
            _context,
            _secretProtector,
            _clock);
        var issue = await subscriptionService.CreateAsync(
            new WebhookSubscriptionCreateRequest(
                "ERP",
                "https://erp.example.test/wms/events"));

        var rotation = await subscriptionService.RotateSecretAsync(
            issue.Subscription.Id,
            TimeSpan.FromHours(2));

        rotation.Secret.Should().NotBe(issue.Secret);
        rotation.Subscription.SecretVersion.Should().Be(2);
        rotation.PreviousSecretValidUntilUtc.Should().Be(_clock.UtcNow.AddHours(2));
    }

    [Fact]
    public async Task UnconfiguredTransportReportsDisabledAndNeverClaimsDelivery()
    {
        var transport = new UnconfiguredWebhookDeliveryTransport();

        var result = await transport.SendAsync(new WebhookDeliveryRequest(
            SubscriptionId: 1,
            EndpointUrl: "https://receiver.example.test/events",
            EventType: WmsIntegrationEventTypes.InventoryMovementRecorded,
            EventId: Guid.NewGuid(),
            PayloadJson: "{}",
            Headers: new Dictionary<string, string>()));

        transport.Capability.Should().Be(new WebhookDeliveryTransportCapability(false, false));
        result.Succeeded.Should().BeFalse();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task NonRetryableDeliveryBecomesVisibleAsDeadLetter()
    {
        var subscriptionService = new WebhookSubscriptionService(
            _context,
            _secretProtector,
            _clock);
        await subscriptionService.CreateAsync(
            new WebhookSubscriptionCreateRequest(
                "ERP",
                "https://erp.example.test/wms/events",
                [WmsIntegrationEventTypes.InventoryMovementRecorded],
                MaximumAttempts: 8));
        await _writer.EnqueueAsync(CreateDraft(Guid.NewGuid()));
        await _context.SaveChangesAsync();

        var dispatcher = new IntegrationOutboxDispatcher(
            _context,
            new FailingWebhookTransport(),
            _secretProtector,
            _clock,
            NullLogger<IntegrationOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync();

        result.DeliveriesFailed.Should().Be(1);
        result.EventsDeadLettered.Should().Be(1);
        (await _context.IntegrationOutbox.SingleAsync()).Status
            .Should().Be(WmsIntegrationEventStatuses.DeadLettered);
        (await _context.WebhookDeliveries.SingleAsync()).Status
            .Should().Be(WmsWebhookDeliveryStatuses.DeadLettered);
    }

    [Fact]
    public void WebhookSignatureRejectsReplayOutsideTheWindow()
    {
        var payload = "{\"event\":\"inventory\"}";
        var old = _clock.UtcNow.AddMinutes(-6);
        var signature = WebhookSignature.Create("secret", payload, old);

        WebhookSignature.Verify("secret", payload, signature, _clock.UtcNow)
            .Should().BeFalse();
        WebhookSignature.Verify("secret", payload, signature, old)
            .Should().BeTrue();
    }

    private static IntegrationEventDraft CreateDraft(Guid eventId) =>
        new(
            WmsIntegrationEventTypes.InventoryMovementRecorded,
            "Movement",
            "movement-1",
            new { movementId = 1, quantity = 3 },
            CorrelationId: "corr-1",
            EventId: eventId);

    private sealed class CapturingWebhookTransport : IWebhookDeliveryTransport
    {
        public WebhookDeliveryTransportCapability Capability { get; } = new(true, true);

        public List<WebhookDeliveryRequest> Requests { get; } = [];

        public Task<WebhookDeliveryResult> SendAsync(
            WebhookDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new WebhookDeliveryResult(true, false, ResponseStatusCode: 202));
        }
    }

    private sealed class FailingWebhookTransport : IWebhookDeliveryTransport
    {
        public WebhookDeliveryTransportCapability Capability { get; } = new(true, true);

        public Task<WebhookDeliveryResult> SendAsync(
            WebhookDeliveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: false,
                ResponseStatusCode: 422,
                Error: "webhook_http_permanent_failure"));
    }

    private sealed class SequencedWebhookTransport(params WebhookDeliveryResult[] results)
        : IWebhookDeliveryTransport
    {
        private readonly Queue<WebhookDeliveryResult> _results = new(results);

        public WebhookDeliveryTransportCapability Capability { get; } = new(true, true);
        public List<WebhookDeliveryRequest> Requests { get; } = [];

        public Task<WebhookDeliveryResult> SendAsync(
            WebhookDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Wms.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class WebhookReceiver(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public static async Task<WebhookReceiver> StartAsync(RequestDelegate handler)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build();
            app.Run(handler);
            await app.StartAsync();
            var address = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses
                .SingleOrDefault()
                ?? throw new InvalidOperationException("The disposable HTTP receiver did not bind an address.");
            return new WebhookReceiver(app, new Uri(address));
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
