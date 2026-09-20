using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Context;
using Wms.Application.Idempotency;
using Wms.Application.Identity;
using Wms.Domain.Enums;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryCommandIdempotencyServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly InventoryCommandIdempotencyService _service;

    public InventoryCommandIdempotencyServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        var warehouseAccess = new Mock<IWarehouseAccessService>();
        warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _unitOfWork = new UnitOfWork(_context, warehouseAccess.Object);
        _service = new InventoryCommandIdempotencyService(
            _unitOfWork,
            _context,
            _clock,
            NullLogger<InventoryCommandIdempotencyService>.Instance);
    }

    [Fact]
    public async Task SuccessfulCommandReturnsTheOriginalResultOnRetry()
    {
        var request = CreateRequest("receipt-1");

        var first = await _service.BeginAsync(request);
        first.IsSuccess.Should().BeTrue();
        first.Value.ShouldExecute.Should().BeTrue();
        first.Value.Lease.Should().NotBeNull();

        await _service.CompleteAsync(
            first.Value.Lease!,
            new InventoryCommandIdempotencyCompletion(
                "ReceiptResultDto",
                "{\"movementId\":42}",
                "42"));
        await _context.SaveChangesAsync();

        var retry = await _service.BeginAsync(request);

        retry.IsSuccess.Should().BeTrue();
        retry.Value.ShouldExecute.Should().BeFalse();
        retry.Value.ResultType.Should().Be("ReceiptResultDto");
        retry.Value.ResultPayloadJson.Should().Be("{\"movementId\":42}");
        retry.Value.ResultReference.Should().Be("42");
        (await _context.InventoryCommandIdempotencies.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReusingAKeyWithDifferentPayloadIsRejected()
    {
        var request = CreateRequest("receipt-2");
        var first = await _service.BeginAsync(request);
        first.IsSuccess.Should().BeTrue();
        await _service.CompleteAsync(
            first.Value.Lease!,
            new InventoryCommandIdempotencyCompletion("ReceiptResultDto", "{}"));
        await _context.SaveChangesAsync();

        var mismatch = await _service.BeginAsync(request with
        {
            RequestHash = InventoryCommandRequestHasher.Compute(
                "inventory.receipt",
                new { ItemSku = "different-item", Quantity = 10m })
        });

        mismatch.IsFailure.Should().BeTrue();
        mismatch.ErrorCode.Should().Be("idempotency.payload_mismatch");
        mismatch.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public async Task InProgressDuplicateIsBlockedAndFailedCommandCanBeReclaimed()
    {
        var request = CreateRequest("receipt-3");
        var first = await _service.BeginAsync(request);
        first.IsSuccess.Should().BeTrue();

        var duplicate = await _service.BeginAsync(request);
        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("idempotency.in_progress");
        duplicate.IsRetryable.Should().BeTrue();

        await _service.MarkFailedAsync(
            first.Value.Lease!,
            "data.timeout",
            "The inventory provider timed out.");
        await _context.SaveChangesAsync();

        var retry = await _service.BeginAsync(request);
        retry.IsSuccess.Should().BeTrue();
        retry.Value.ShouldExecute.Should().BeTrue();
        retry.Value.Lease.Should().NotBeNull();
        (await _context.InventoryCommandIdempotencies.SingleAsync()).Status
            .Should().Be(InventoryCommandIdempotencyStatus.InProgress);
    }

    [Fact]
    public async Task CleanupDoesNotDeleteActiveCommandsAndPrunesExpiredStates()
    {
        var request = CreateRequest("receipt-4", TimeSpan.FromHours(1));
        var first = await _service.BeginAsync(request);
        first.IsSuccess.Should().BeTrue();
        await _service.MarkFailedAsync(first.Value.Lease!, "test.failed");
        await _context.SaveChangesAsync();

        var removedTooEarly = await _service.PruneAsync(_clock.UtcNow.AddMinutes(30));
        removedTooEarly.Should().Be(0);
        (await _context.InventoryCommandIdempotencies.CountAsync()).Should().Be(1);

        var removed = await _service.PruneAsync(_clock.UtcNow.AddDays(2));
        removed.Should().Be(1);
        (await _context.InventoryCommandIdempotencies.CountAsync()).Should().Be(0);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static InventoryCommandIdempotencyRequest CreateRequest(
        string commandKey,
        TimeSpan? retentionWindow = null) =>
        new(
            "inventory.receipt",
            commandKey,
            "Test:USER1:warehouse:7",
            InventoryCommandRequestHasher.Compute(
                "inventory.receipt",
                new { ItemSku = "ITEM-1", Quantity = 10m }),
            "correlation-1",
            "USER1",
            7,
            retentionWindow);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
