using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.AnomalyDetection;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.AnomalyDetection;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.AnomalyDetection;

public sealed class AnomalyDetectionServiceTests : IDisposable
{
    private static readonly DateTimeOffset From = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _status;
    private readonly Location _location;
    private readonly AnomalyDetectionService _service;

    public AnomalyDetectionServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access.Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _access.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _notifications.Setup(service => service.PublishAsync(
                It.IsAny<NotificationPublishInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new NotificationPublishResult(1, 1, false, false)));
        _currentUser.SetupGet(user => user.IsAuthenticated).Returns(false);

        _warehouse = new Warehouse("ANOMALY-WH", "Anomaly warehouse", timeZone: "UTC");
        _item = new Item("ANOMALY-ITEM", "Anomaly item", "EA");
        _status = new InventoryStatus(
            "ANOMALY-AVAILABLE", "Available", "متاح", true, true, true, true, true);
        _context.AddRange(_warehouse, _item, _status);
        _context.SaveChanges();
        _location = new Location(
            "ANOMALY-BIN", "Anomaly bin", _warehouse.Id, type: LocationType.Bin, isReceivable: false);
        _context.Add(_location);
        _context.SaveChanges();

        _service = new AnomalyDetectionService(
            _context,
            _access.Object,
            _notifications.Object,
            _currentUser.Object,
            _clock,
            NullLogger<AnomalyDetectionService>.Instance);
    }

    [Fact]
    public async Task Reads_adjustment_ledger_persists_finding_and_reuses_identical_run()
    {
        _notifications.SetupSequence(service => service.PublishAsync(
                It.IsAny<NotificationPublishInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<NotificationPublishResult>(WmsErrors.Dependency(
                "anomaly.notification_retryable",
                "Temporary notification-store failure.",
                isRetryable: true)))
            .ReturnsAsync(Result.Success(new NotificationPublishResult(1, 1, false, false)))
            .ReturnsAsync(Result.Success(new NotificationPublishResult(1, 1, false, false)));
        AddRule(AnomalyRuleKind.InventoryAdjustment, threshold: 5m);
        var key = BalanceKey();
        _context.InventoryTransactions.Add(new InventoryTransaction(
            InventoryTransactionType.Adjustment,
            key,
            quantityDelta: 12m,
            quantityBefore: 20m,
            quantityAfter: 32m,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 0m,
            actorUserId: "private-operator-id",
            occurredAtUtc: new DateTime(2026, 9, 20, 9, 30, 0, DateTimeKind.Utc),
            correlationId: "anomaly-correlation",
            idempotencyKey: "anomaly-adjustment-1",
            transactionGroupId: "anomaly-adjustment-1",
            entrySequence: 1));
        await _context.SaveChangesAsync();

        var first = await _service.RecalculateAsync(new AnomalyRecalculationInput(_warehouse.Id, From, To));
        var repeated = await _service.RecalculateAsync(new AnomalyRecalculationInput(_warehouse.Id, From, To));

        Assert.True(first.IsFailure);
        Assert.Equal("anomaly.notification_retryable", first.ErrorCode);
        Assert.True(repeated.IsSuccess, repeated.Error);
        Assert.True(repeated.Value.WasReused);
        Assert.Equal(1, await _context.AnomalyFindings.CountAsync());
        Assert.Equal(1, await _context.AnomalyDetectionRuns.CountAsync());
        Assert.Equal(1, await _context.AnomalyFindingObservations.CountAsync());

        var finding = await _context.AnomalyFindings.SingleAsync();
        Assert.Equal(12m, finding.ObservedValue);
        Assert.Equal(From, finding.SourceWindowFromUtc);
        Assert.Equal(To, finding.SourceWindowToUtc);
        Assert.Equal("inventoryadjustment.Z2xvYmFs.v1", finding.RuleVersion);
        Assert.Contains("adjustment", finding.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-operator-id", finding.Explanation, StringComparison.Ordinal);
        Assert.Equal(1, await _context.AnomalyFindingHistory.CountAsync());
        _notifications.Verify(service => service.PublishAsync(
            It.Is<NotificationPublishInput>(input =>
                input.DeduplicationKey == $"anomaly:{finding.Fingerprint}" &&
                input.SourceType == "anomaly-finding" &&
                input.SourceId == finding.Fingerprint &&
                input.DeepLink == $"/api/anomalies/{finding.Fingerprint}" &&
                input.RequiredPermission == WmsPermissions.AnomalyRead &&
                input.Channels!.SequenceEqual(new[] { NotificationChannel.InApp })),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        await using (var restartedContext = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options))
        {
            Assert.Equal(1, await restartedContext.AnomalyDetectionRuns.CountAsync());
            Assert.Equal(1, await restartedContext.AnomalyFindings.CountAsync());
            Assert.Equal(1, await restartedContext.AnomalyFindingObservations.CountAsync());
        }

        var transitioned = await _service.TransitionAsync(
            finding.Fingerprint,
            new AnomalyDispositionRequest(AnomalyStatus.New, AnomalyStatus.Investigating, "Reviewing ledger source."),
            "reviewer-1");
        Assert.True(transitioned.IsSuccess, transitioned.Error);
        Assert.Equal(AnomalyStatus.Investigating, transitioned.Value.Status);

        var assigned = await _service.AssignAsync(
            finding.Fingerprint,
            new AnomalyAssignmentInput(null, "inventory-control", "Assigned for follow-up."),
            "reviewer-1");
        Assert.True(assigned.IsSuccess, assigned.Error);
        Assert.Equal("INVENTORY-CONTROL", assigned.Value.AssignedTeamCode);

        var suppressed = await _service.TransitionAsync(
            finding.Fingerprint,
            new AnomalyDispositionRequest(
                AnomalyStatus.Investigating,
                AnomalyStatus.Suppressed,
                "Known maintenance adjustment window.",
                _clock.UtcNow.AddHours(1)),
            "reviewer-1");
        Assert.True(suppressed.IsSuccess, suppressed.Error);
        _clock.Advance(TimeSpan.FromHours(2));
        var scheduled = await _service.RecalculateScheduledAsync();
        Assert.True(scheduled.IsSuccess, scheduled.Error);
        _notifications.Verify(service => service.PublishAsync(
            It.Is<NotificationPublishInput>(input =>
                input.DeduplicationKey == $"anomaly:{finding.Fingerprint}"),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
        var expired = await _context.AnomalyFindings.SingleAsync();
        Assert.Equal(AnomalyStatus.Investigating.ToString(), expired.Status);
        Assert.Null(expired.SuppressionExpiresAtUtc);
        Assert.Contains(await _context.AnomalyFindingHistory.ToListAsync(), entry =>
            entry.Action == "suppression.expired");
    }

    [Fact]
    public async Task Appends_absence_observation_without_rewriting_historical_finding()
    {
        AddRule(AnomalyRuleKind.InventoryAdjustment, threshold: 5m);
        var firstRun = new AnomalyDetectionRunEntity
        {
            WarehouseId = _warehouse.Id,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            InputFingerprint = new string('a', 64),
            RuleVersionsJson = "{}",
            DataQualityFlagsJson = "[]",
            StartedAtUtc = From,
            CompletedAtUtc = To
        };
        var historicalFinding = new AnomalyFindingEntity
        {
            Fingerprint = new string('b', 64),
            WarehouseId = _warehouse.Id,
            RuleKind = AnomalyRuleKind.InventoryAdjustment.ToString(),
            Severity = AnomalySeverity.High.ToString(),
            Status = AnomalyStatus.Investigating.ToString(),
            RuleVersion = "inventoryadjustment.v1",
            SourceType = "inventory-transaction",
            SourceId = "999",
            FirstDetectionRun = firstRun,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            ObservedValue = 40m,
            ExpectedValue = 0m,
            Threshold = 5m,
            Explanation = "Original immutable explanation.",
            ObservedAtUtc = From.AddDays(2),
            FirstDetectedAtUtc = From.AddDays(2),
            Revision = 1
        };
        _context.AnomalyDetectionRuns.Add(firstRun);
        _context.AnomalyFindings.Add(historicalFinding);
        await _context.SaveChangesAsync();

        var result = await _service.RecalculateAsync(new AnomalyRecalculationInput(_warehouse.Id, From, To));

        Assert.True(result.IsSuccess, result.Error);
        var persistedFinding = await _context.AnomalyFindings.SingleAsync();
        Assert.Equal(40m, persistedFinding.ObservedValue);
        Assert.Equal("Original immutable explanation.", persistedFinding.Explanation);
        var observation = await _context.AnomalyFindingObservations.SingleAsync();
        Assert.False(observation.SourcePresent);
        Assert.False(observation.IsAnomalous);
        Assert.Null(observation.ObservedValue);
    }

    [Fact]
    public async Task Rejects_changes_to_persisted_source_baseline()
    {
        AddRule(AnomalyRuleKind.InventoryAdjustment, threshold: 5m);
        var run = new AnomalyDetectionRunEntity
        {
            WarehouseId = _warehouse.Id,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            InputFingerprint = new string('c', 64),
            RuleVersionsJson = "{}",
            DataQualityFlagsJson = "[]",
            StartedAtUtc = From,
            CompletedAtUtc = To
        };
        var finding = new AnomalyFindingEntity
        {
            Fingerprint = new string('d', 64),
            WarehouseId = _warehouse.Id,
            RuleKind = AnomalyRuleKind.InventoryAdjustment.ToString(),
            Severity = AnomalySeverity.High.ToString(),
            Status = AnomalyStatus.New.ToString(),
            RuleVersion = "inventoryadjustment.v1",
            SourceType = "inventory-transaction",
            SourceId = "123",
            FirstDetectionRun = run,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            ObservedValue = 10m,
            ExpectedValue = 0m,
            Threshold = 5m,
            Explanation = "Immutable.",
            ObservedAtUtc = From,
            FirstDetectedAtUtc = From,
            Revision = 1
        };
        _context.AddRange(run, finding);
        await _context.SaveChangesAsync();

        finding.ObservedValue = 0m;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _context.SaveChangesAsync());

        Assert.Contains("source evidence is immutable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_and_detail_enforce_warehouse_scope_and_redact_source_references()
    {
        var otherWarehouse = new Warehouse("ANOMALY-WH-OTHER", "Other anomaly warehouse", timeZone: "UTC");
        _context.Add(otherWarehouse);
        await _context.SaveChangesAsync();
        var allowedFinding = await AddFindingAsync(_warehouse, 'e');
        var restrictedFinding = await AddFindingAsync(otherWarehouse, 'f');
        _access.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(false, new HashSet<int> { _warehouse.Id }));
        _access.Setup(service => service.HasPermissionAsync(
                WmsPermissions.InventoryRead,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var page = await _service.SearchAsync(new AnomalyFindingSearchQuery());

        Assert.True(page.IsSuccess, page.Error);
        var listedFinding = Assert.Single(page.Value.Items);
        Assert.Equal(allowedFinding.Fingerprint, listedFinding.Fingerprint);
        Assert.Equal("restricted", listedFinding.SourceType);
        Assert.Equal("restricted", listedFinding.SourceId);

        var detail = await _service.GetAsync(allowedFinding.Fingerprint);
        Assert.True(detail.IsSuccess, detail.Error);
        Assert.Equal("restricted", detail.Value.SourceType);
        Assert.Equal("restricted", detail.Value.SourceId);

        _access.Setup(service => service.AuthorizeAsync(
                WmsPermissions.AnomalyRead,
                otherWarehouse.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden("warehouse.denied", "Warehouse access denied.")));
        var deniedDetail = await _service.GetAsync(restrictedFinding.Fingerprint);

        Assert.True(deniedDetail.IsFailure);
        Assert.Equal("warehouse.denied", deniedDetail.ErrorCode);
    }

    [Fact]
    public async Task False_positive_transition_is_audited_and_stale_state_is_rejected()
    {
        var finding = await AddFindingAsync(_warehouse, 'a');

        var falsePositive = await _service.TransitionAsync(
            finding.Fingerprint,
            new AnomalyDispositionRequest(AnomalyStatus.New, AnomalyStatus.FalsePositive, "Expected cycle-count variance."),
            "reviewer-1");

        Assert.True(falsePositive.IsSuccess, falsePositive.Error);
        Assert.Equal(AnomalyStatus.FalsePositive, falsePositive.Value.Status);
        var history = await _context.AnomalyFindingHistory.SingleAsync();
        Assert.Equal("status.changed", history.Action);
        Assert.Equal(AnomalyStatus.FalsePositive.ToString(), history.ToStatus);
        Assert.Equal("Expected cycle-count variance.", history.Comment);

        var staleTransition = await _service.TransitionAsync(
            finding.Fingerprint,
            new AnomalyDispositionRequest(AnomalyStatus.New, AnomalyStatus.Investigating, "Stale update."),
            "reviewer-2");

        Assert.True(staleTransition.IsFailure);
        Assert.Equal("anomaly.revision_conflict", staleTransition.ErrorCode);
        Assert.Single(await _context.AnomalyFindingHistory.ToListAsync());
    }

    [Fact]
    public async Task Assignment_requires_warehouse_management_permission()
    {
        var finding = await AddFindingAsync(_warehouse, 'b');
        _access.Setup(service => service.AuthorizeAsync(
                WmsPermissions.AnomalyManage,
                _warehouse.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden("anomaly.manage_denied", "Management permission required.")));

        var result = await _service.AssignAsync(
            finding.Fingerprint,
            new AnomalyAssignmentInput(null, "inventory-control", "Assign for review."),
            "reviewer-1");

        Assert.True(result.IsFailure);
        Assert.Equal("anomaly.manage_denied", result.ErrorCode);
        Assert.Empty(await _context.AnomalyFindingHistory.ToListAsync());
    }

    private async Task<AnomalyFindingEntity> AddFindingAsync(Warehouse warehouse, char fingerprintCharacter)
    {
        var fingerprint = new string(fingerprintCharacter, 64);
        var run = new AnomalyDetectionRunEntity
        {
            WarehouseId = warehouse.Id,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            InputFingerprint = new string((char)(fingerprintCharacter + 1), 64),
            RuleVersionsJson = "{}",
            DataQualityFlagsJson = "[]",
            StartedAtUtc = From,
            CompletedAtUtc = To
        };
        var finding = new AnomalyFindingEntity
        {
            Fingerprint = fingerprint,
            WarehouseId = warehouse.Id,
            RuleKind = AnomalyRuleKind.InventoryAdjustment.ToString(),
            Severity = AnomalySeverity.High.ToString(),
            Status = AnomalyStatus.New.ToString(),
            RuleVersion = "inventoryadjustment.v1",
            SourceType = "inventory-transaction",
            SourceId = "private-source-id",
            FirstDetectionRun = run,
            SourceWindowFromUtc = From,
            SourceWindowToUtc = To,
            ObservedValue = 12m,
            ExpectedValue = 0m,
            Threshold = 5m,
            Explanation = "Adjustment exceeds the configured threshold.",
            ObservedAtUtc = From.AddDays(2),
            FirstDetectedAtUtc = From.AddDays(2),
            Revision = 1
        };
        _context.AddRange(run, finding);
        await _context.SaveChangesAsync();
        return finding;
    }

    private void AddRule(AnomalyRuleKind kind, decimal threshold)
    {
        _context.AnomalyRuleConfigurations.Add(new AnomalyRuleConfigurationEntity
        {
            RuleKind = kind.ToString(),
            ScopeKey = "global",
            Version = 1,
            Threshold = threshold,
            IsEnabled = true,
            CreatedByUserId = "system",
            CreatedAtUtc = _clock.UtcNow
        });
        _context.SaveChanges();
    }

    private InventoryBalanceKey BalanceKey() => new(
        _warehouse.Id,
        _location.Id,
        _item.Id,
        null,
        null,
        null,
        null,
        _status.Id,
        _item.UnitOfMeasure);

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = value;

        public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }
}
