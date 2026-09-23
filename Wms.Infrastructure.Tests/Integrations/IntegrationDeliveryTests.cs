using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Integrations;

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
                MaximumAttempts: 1));
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
        public Task<WebhookDeliveryResult> SendAsync(
            WebhookDeliveryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WebhookDeliveryResult(
                Succeeded: false,
                Retryable: false,
                ResponseStatusCode: 502,
                Error: "provider rejected the payload"));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
