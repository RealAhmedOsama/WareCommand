using Microsoft.Data.Sqlite;
using Wms.Application.Jobs;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Jobs;

namespace Wms.Infrastructure.Tests.Jobs;

public sealed class WmsJobExecutionStoreTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;

    public WmsJobExecutionStoreTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new WmsDbContext(options);
    }

    public async Task InitializeAsync() => await _context.Database.EnsureCreatedAsync();

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DuplicateExecutionIsSkippedBeforeAndAfterSuccessfulCompletion()
    {
        var store = new WmsJobExecutionStore(_context);
        var now = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        var envelope = WmsJobEnvelope.Create(
            WmsJobNames.LowStockAlerts,
            "low-stock:20260920100000",
            "corr-1",
            actorUserId: "system",
            actorUserName: "Background Jobs");

        var first = await store.TryStartAsync(envelope, now);
        var duplicateWhileRunning = await store.TryStartAsync(envelope, now.AddMinutes(1));

        first.ShouldExecute.Should().BeTrue();
        first.Attempt.Should().Be(1);
        duplicateWhileRunning.ShouldExecute.Should().BeFalse();
        duplicateWhileRunning.SkipReason.Should().Contain("already running");

        await store.CompleteAsync(
            first,
            new WmsJobExecutionResult(ItemsExamined: 3, ItemsCreated: 2, Summary: "created 2"),
            now.AddMinutes(2));

        var duplicateAfterSuccess = await store.TryStartAsync(envelope, now.AddMinutes(3));
        duplicateAfterSuccess.ShouldExecute.Should().BeFalse();
        duplicateAfterSuccess.SkipReason.Should().Contain("already succeeded");

        var execution = await _context.JobExecutions.SingleAsync();
        execution.Status.Should().Be(WmsJobExecutionStatuses.Succeeded);
        execution.AttemptCount.Should().Be(1);
        execution.ResultSummary.Should().Be("created 2");
    }

    [Fact]
    public async Task FailedExecutionCanBeRetriedWithAnIncrementedAttempt()
    {
        var store = new WmsJobExecutionStore(_context);
        var now = new DateTimeOffset(2026, 9, 20, 11, 0, 0, TimeSpan.Zero);
        var envelope = WmsJobEnvelope.Create(
            WmsJobNames.IntegrationRetries,
            "integration:20260920110000");

        var first = await store.TryStartAsync(envelope, now);
        await store.MarkFailedAsync(first, new TimeoutException("provider timed out"), now.AddMinutes(1));

        var retry = await store.TryStartAsync(envelope, now.AddMinutes(2));

        retry.ShouldExecute.Should().BeTrue();
        retry.Attempt.Should().Be(2);

        var execution = await _context.JobExecutions.SingleAsync();
        execution.Status.Should().Be(WmsJobExecutionStatuses.Running);
        execution.AttemptCount.Should().Be(2);
        execution.LastErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task NotificationsAreUpsertedByTheirDeduplicationKey()
    {
        var store = new WmsJobExecutionStore(_context);
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var first = new WmsJobNotification(
            "low-stock:item-7",
            "low-stock",
            "warning",
            "Low stock",
            "Initial message",
            WmsJobNames.LowStockAlerts,
            "low-stock:20260920120000",
            "corr-1");
        var updated = first with
        {
            Title = "Low stock updated",
            Message = "Updated message",
            ExpiresAtUtc = now.AddDays(1)
        };

        await store.UpsertNotificationAsync(first, now);
        await store.UpsertNotificationAsync(updated, now.AddMinutes(1));

        var notification = await _context.JobNotifications.SingleAsync();
        notification.Title.Should().Be("Low stock updated");
        notification.Message.Should().Be("Updated message");
        notification.ExpiresAtUtc.Should().Be(now.AddDays(1));
    }
}
