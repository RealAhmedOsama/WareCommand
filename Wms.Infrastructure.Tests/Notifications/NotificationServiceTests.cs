using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Tests.Notifications;

public sealed class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly TestRecipientDirectory _directory = new();
    private readonly WmsDbContext _context;
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        _context.Database.EnsureCreated();
        _context.Users.Add(new WmsUser
        {
            Id = "user-1",
            UserName = "user-1",
            NormalizedUserName = "USER-1",
            Email = "user-1@example.test",
            NormalizedEmail = "USER-1@EXAMPLE.TEST",
            Locale = "ar-SA",
            TimeZone = "UTC",
            DisplayName = "User One",
            EmployeeCode = "U-1"
        });
        _context.SaveChanges();

        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(user => user.UserId).Returns("user-1");
        _currentUser.SetupGet(user => user.UserName).Returns("operator");
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new NotificationService(
            _context,
            _directory,
            _warehouseAccess.Object,
            _currentUser.Object,
            _auditWriter.Object,
            _clock,
            NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task Publish_is_recipient_specific_and_idempotent()
    {
        _directory.Recipients =
        [
            Profile("user-1", "en-US"),
            Profile("user-2", "ar-SA")
        ];

        var input = CreateInput(
            deduplicationKey: "stock:item-1:warehouse-7",
            channels: [NotificationChannel.InApp, NotificationChannel.Email]);
        var first = await _service.PublishAsync(input);
        var replay = await _service.PublishAsync(input);

        first.IsSuccess.Should().BeTrue();
        first.Value.RecipientCount.Should().Be(2);
        first.Value.WasDeduplicated.Should().BeFalse();
        replay.IsSuccess.Should().BeTrue();
        replay.Value.WasDeduplicated.Should().BeTrue();
        (await _context.Notifications.CountAsync()).Should().Be(1);
        (await _context.NotificationRecipients.CountAsync()).Should().Be(4);
        (await _context.NotificationRecipients.CountAsync(
            row => row.Channel == NotificationChannel.InApp &&
                   row.DeliveryStatus == NotificationDeliveryStatus.Delivered))
            .Should().Be(2);
    }

    [Fact]
    public async Task Inbox_is_localized_and_read_acknowledge_are_scoped_to_recipient()
    {
        _directory.Recipients = [Profile("user-1", "ar-SA")];
        var published = await _service.PublishAsync(CreateInput(
            deduplicationKey: "approval:request-1",
            channels: [NotificationChannel.InApp]));
        var page = await _service.ListAsync(new NotificationQuery());
        var recipientId = page.Value.Items.Single().RecipientId;

        page.IsSuccess.Should().BeTrue();
        page.Value.Items.Single().Title.Should().Be("تنبيه المخزون");
        page.Value.UnreadCount.Should().Be(1);

        (await _service.MarkReadAsync(recipientId)).IsSuccess.Should().BeTrue();
        (await _service.AcknowledgeAsync(recipientId)).IsSuccess.Should().BeTrue();
        var count = await _service.GetUnreadCountAsync();

        count.IsSuccess.Should().BeTrue();
        count.Value.Should().Be(0);
        published.Value.NotificationId.Should().BeGreaterThan(0);
        _auditWriter.Verify(
            writer => writer.RecordAsync(
                It.Is<AuditRecord>(record => record.Action == WmsAuditActions.NotificationAcknowledged),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Mandatory_alerts_ignore_disable_preferences_but_normal_alerts_can_be_suppressed()
    {
        _directory.Recipients = [Profile("user-1", "en-US")];
        var preference = await _service.SavePreferenceAsync(new NotificationPreferenceInput(
            NotificationPreferenceScope.User,
            null,
            null,
            "stock.low",
            NotificationChannel.InApp,
            IsEnabled: false));
        var disabledMandatory = await _service.SavePreferenceAsync(new NotificationPreferenceInput(
            NotificationPreferenceScope.User,
            null,
            null,
            "backup.failure",
            NotificationChannel.InApp,
            IsEnabled: false));

        var normal = await _service.PublishAsync(CreateInput(
            kind: "stock.low",
            deduplicationKey: "stock:low:1"));
        var mandatory = await _service.PublishAsync(CreateInput(
            kind: "backup.failure",
            deduplicationKey: "backup:failure:1"));

        preference.IsSuccess.Should().BeTrue();
        disabledMandatory.IsFailure.Should().BeTrue();
        disabledMandatory.ErrorCode.Should().Be("notifications.mandatory_cannot_disable");
        normal.IsSuccess.Should().BeTrue();
        normal.Value.WasSuppressed.Should().BeTrue();
        mandatory.IsSuccess.Should().BeTrue();
        mandatory.Value.WasSuppressed.Should().BeFalse();
    }

    [Fact]
    public async Task External_delivery_is_deferred_and_retry_service_records_success()
    {
        _directory.Recipients = [Profile("user-1", "en-US")];
        var published = await _service.PublishAsync(CreateInput(
            deduplicationKey: "shipment:delayed:1",
            channels: [NotificationChannel.Email]));
        var pending = await _context.NotificationRecipients.SingleAsync();
        var adapter = new SuccessfulAdapter();
        var delivery = new NotificationDeliveryService(
            _context,
            [adapter],
            _directory,
            _clock,
            NullLogger<NotificationDeliveryService>.Instance);

        pending.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Pending);
        adapter.Calls.Should().Be(0);
        var dispatched = await delivery.DispatchPendingAsync();
        var delivered = await _context.NotificationRecipients.SingleAsync();

        published.IsSuccess.Should().BeTrue();
        dispatched.Should().Be(1);
        adapter.Calls.Should().Be(1);
        delivered.DeliveryStatus.Should().Be(NotificationDeliveryStatus.TransportAccepted);
        delivered.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Delivery_rechecks_current_recipient_and_channel_preference()
    {
        _directory.Recipients = [Profile("user-1", "en-US")];
        await _service.PublishAsync(CreateInput(
            deduplicationKey: "stock:low:disabled-user",
            channels: [NotificationChannel.Email]));
        var adapter = new SuccessfulAdapter();
        var delivery = CreateDeliveryService(adapter);
        _directory.Recipients = [];

        await delivery.DispatchPendingAsync();

        var inactiveRecipient = await _context.NotificationRecipients.SingleAsync();
        inactiveRecipient.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Suppressed);
        inactiveRecipient.AttemptCount.Should().Be(0);
        adapter.Calls.Should().Be(0);

        _directory.Recipients = [Profile("user-1", "en-US")];
        await _service.PublishAsync(CreateInput(
            deduplicationKey: "stock:low:preference-change",
            channels: [NotificationChannel.Email]) with
        {
            CooldownKey = null,
            Cooldown = null
        });
        _context.NotificationPreferences.Add(new WmsNotificationPreferenceEntity
        {
            Scope = NotificationPreferenceScope.User,
            UserId = "user-1",
            Kind = "stock.low",
            Channel = NotificationChannel.Email,
            IsEnabled = false,
            TimeZone = "UTC",
            UpdatedAtUtc = _clock.UtcNow
        });
        await _context.SaveChangesAsync();

        await delivery.DispatchPendingAsync();

        var preferenceSuppressed = await _context.NotificationRecipients
            .Where(row => row.Notification.DeduplicationKey == "stock:low:preference-change")
            .SingleAsync();
        preferenceSuppressed.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Suppressed);
        preferenceSuppressed.AttemptCount.Should().Be(0);
        adapter.Calls.Should().Be(0);

        _context.NotificationPreferences.RemoveRange(await _context.NotificationPreferences.ToListAsync());
        await _context.SaveChangesAsync();
        await _service.PublishAsync(CreateInput(
            deduplicationKey: "stock:low:digest-change",
            channels: [NotificationChannel.Email]) with
        {
            CooldownKey = null,
            Cooldown = null
        });
        _context.NotificationPreferences.Add(new WmsNotificationPreferenceEntity
        {
            Scope = NotificationPreferenceScope.User,
            UserId = "user-1",
            Kind = "stock.low",
            Channel = NotificationChannel.Email,
            IsEnabled = true,
            DigestMinutes = 60,
            TimeZone = "UTC",
            UpdatedAtUtc = _clock.UtcNow
        });
        await _context.SaveChangesAsync();

        await delivery.DispatchPendingAsync();

        var deferred = await _context.NotificationRecipients
            .Where(row => row.Notification.DeduplicationKey == "stock:low:digest-change")
            .SingleAsync();
        deferred.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Pending);
        deferred.AttemptCount.Should().Be(0);
        deferred.NextAttemptAtUtc.Should().Be(_clock.UtcNow.AddMinutes(60));
        adapter.Calls.Should().Be(0);

        _clock.UtcNow = deferred.NextAttemptAtUtc!.Value;
        await delivery.DispatchPendingAsync();
        (await _context.NotificationRecipients
                .Where(row => row.Notification.DeduplicationKey == "stock:low:digest-change")
                .SingleAsync())
            .DeliveryStatus.Should().Be(NotificationDeliveryStatus.TransportAccepted);
        adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Retryable_transport_failures_are_bounded_and_store_only_safe_error_codes()
    {
        _directory.Recipients = [Profile("user-1", "en-US")];
        await _service.PublishAsync(CreateInput(
            deduplicationKey: "stock:low:retry-limit",
            channels: [NotificationChannel.Email]));
        var adapter = new FailingAdapter();
        var delivery = CreateDeliveryService(adapter);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await delivery.DispatchPendingAsync();
            var recipient = await _context.NotificationRecipients.SingleAsync();
            if (recipient.DeliveryStatus == NotificationDeliveryStatus.Failed)
            {
                _clock.UtcNow = recipient.NextAttemptAtUtc!.Value;
            }
        }

        var terminal = await _context.NotificationRecipients.SingleAsync();
        terminal.DeliveryStatus.Should().Be(NotificationDeliveryStatus.DeadLettered);
        terminal.AttemptCount.Should().Be(10);
        terminal.NextAttemptAtUtc.Should().BeNull();
        terminal.LastError.Should().Be("delivery_failed");
        terminal.LastError.Should().NotContain("user-1");
        terminal.LastError.Should().NotContain("secret-token");
        adapter.Calls.Should().Be(10);
        (await delivery.DispatchPendingAsync()).Should().Be(0);
    }

    private NotificationDeliveryService CreateDeliveryService(params INotificationChannelAdapter[] adapters) =>
        new(
            _context,
            adapters,
            _directory,
            _clock,
            NullLogger<NotificationDeliveryService>.Instance);

    private static NotificationPublishInput CreateInput(
        string deduplicationKey,
        string kind = "stock.low",
        IReadOnlyCollection<NotificationChannel>? channels = null) =>
        new(
            kind,
            NotificationSeverity.Warning,
            "Stock alert",
            "تنبيه المخزون",
            "Stock is below the configured threshold.",
            "المخزون أقل من الحد المسموح.",
            new NotificationAudience(UserIds: ["user-1"], WarehouseId: 7),
            channels,
            RequiredPermission: WmsPermissions.InventoryRead,
            SourceType: "Inventory",
            SourceId: "1",
            DeepLink: "/inventory/1",
            DeduplicationKey: deduplicationKey,
            CooldownKey: $"{kind}:warehouse:7",
            Cooldown: TimeSpan.FromMinutes(15),
            ExpiresAtUtc: new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
            CorrelationId: "notification-test");

    private static NotificationRecipientProfile Profile(string userId, string locale) =>
        new(userId, userId, $"{userId}@example.test", locale, "UTC", new HashSet<string>([WmsRoleNames.WarehouseManager]));

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TestRecipientDirectory : INotificationRecipientDirectory
    {
        public IReadOnlyList<NotificationRecipientProfile> Recipients { get; set; } = [];

        public Task<IReadOnlyList<NotificationRecipientProfile>> ResolveAsync(
            NotificationAudience audience,
            string? requiredPermission,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Recipients);
    }

    private sealed class SuccessfulAdapter : INotificationChannelAdapter
    {
        public NotificationChannel Channel => NotificationChannel.Email;

        public int Calls { get; private set; }

        public Task<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryMessage message,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new NotificationDeliveryResult(true, false));
        }
    }

    private sealed class FailingAdapter : INotificationChannelAdapter
    {
        public NotificationChannel Channel => NotificationChannel.Email;

        public int Calls { get; private set; }

        public Task<NotificationDeliveryResult> DeliverAsync(
            NotificationDeliveryMessage message,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new NotificationDeliveryResult(
                Succeeded: false,
                Retryable: true,
                Error: "smtp failed for user-1@example.test with secret-token=abc"));
        }
    }
}
