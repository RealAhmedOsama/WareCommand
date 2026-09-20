using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Auditing;

public sealed class AuditEntryTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WmsDbContext _context = null!;
    private DesktopUserSession _currentUser = null!;
    private WmsRequestContext _requestContext = null!;
    private WmsWarehouseContext _warehouseContext = null!;
    private AuditWriter _writer = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new WmsDbContext(options);
        await _context.Database.EnsureCreatedAsync();

        _currentUser = new DesktopUserSession();
        _currentUser.SignIn(new WmsUser
        {
            Id = "actor-1",
            UserName = "operator",
            DisplayName = "Operator"
        });
        _requestContext = new WmsRequestContext();
        _requestContext.Initialize("corr-123", "Test", "127.0.0.1", "test-agent");
        _warehouseContext = new WmsWarehouseContext();
        _warehouseContext.SetWarehouse(7);
        _writer = new AuditWriter(
            _context,
            _currentUser,
            new FixedClock(new DateTimeOffset(2026, 9, 20, 10, 30, 0, TimeSpan.Zero)),
            _requestContext,
            _warehouseContext);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task RecordAsyncPersistsActorContextAndRedactsSensitiveMetadata()
    {
        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.StockAdjusted,
            WmsAuditEntityTypes.Stock,
            "item-1:location-1",
            Before: new Dictionary<string, object?>
            {
                ["quantity"] = 4,
                ["password"] = "do-not-store",
                ["email"] = "operator@example.test",
                ["unsupportedObject"] = new object()
            },
            After: new Dictionary<string, object?>
            {
                ["quantity"] = 8,
                ["reason"] = "Cycle count"
            }));

        await _context.SaveChangesAsync();
        var entry = await _context.AuditEntries.SingleAsync();

        Assert.Equal("actor-1", entry.ActorUserId);
        Assert.Equal("operator", entry.ActorUserName);
        Assert.Equal(7, entry.WarehouseId);
        Assert.Equal("corr-123", entry.CorrelationId);
        Assert.Equal("Test", entry.SourceClient);
        Assert.Equal("127.0.0.1", entry.RemoteIpAddress);
        Assert.Equal("test-agent", entry.UserAgent);
        Assert.Contains("[REDACTED]", entry.BeforeJson);
        Assert.Contains("[REDACTED_UNSUPPORTED]", entry.BeforeJson);
        Assert.DoesNotContain("do-not-store", entry.BeforeJson);
        Assert.DoesNotContain("operator@example.test", entry.BeforeJson);
        Assert.Contains("Cycle count", entry.AfterJson);
    }

    [Fact]
    public async Task AuditEntriesCannotBeModifiedOrDeletedThroughDbContext()
    {
        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.ItemCreated,
            WmsAuditEntityTypes.Item,
            "SKU-1"));
        await _context.SaveChangesAsync();
        var entry = await _context.AuditEntries.SingleAsync();

        _context.Entry(entry).Property(nameof(AuditEntry.Details)).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _context.SaveChangesAsync());

        _context.Entry(entry).State = EntityState.Detached;
        _context.AuditEntries.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task AuditEntryRollsBackWithTheBusinessTransaction()
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        _context.Warehouses.Add(new Warehouse("AUDIT", "Audit Warehouse"));
        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.LocationCreated,
            WmsAuditEntityTypes.Location,
            "AUDIT-A"));
        await _context.SaveChangesAsync();
        await transaction.RollbackAsync();
        _context.ChangeTracker.Clear();

        Assert.Empty(await _context.AuditEntries.ToListAsync());
        Assert.Empty(await _context.Warehouses.ToListAsync());
    }

    [Fact]
    public async Task QuerySupportsFiltersPagingAndWarehouseScope()
    {
        var assignedWarehouse = new Warehouse("ASSIGNED", "Assigned");
        var otherWarehouse = new Warehouse("OTHER", "Other");
        _context.Warehouses.AddRange(assignedWarehouse, otherWarehouse);
        await _context.SaveChangesAsync();

        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.ItemCreated,
            WmsAuditEntityTypes.Item,
            "assigned-1",
            assignedWarehouse.Id));
        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.ItemCreated,
            WmsAuditEntityTypes.Item,
            "other-1",
            otherWarehouse.Id));
        _warehouseContext.SetWarehouse(null);
        await _writer.RecordAsync(new AuditRecord(
            WmsAuditActions.AuthenticationEvent,
            WmsAuditEntityTypes.Authentication,
            "login"));
        await _context.SaveChangesAsync();

        var queryService = new AuditQueryService(
            _context,
            new ScopedWarehouseAccessService(assignedWarehouse.Id));
        var result = await queryService.SearchAsync(new AuditQuery(Page: 1, PageSize: 1));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.TotalPages);
        Assert.Single(result.Value.Items);
        Assert.Equal(WmsAuditActions.AuthenticationEvent, result.Value.Items[0].Action);

        var filtered = await queryService.SearchAsync(new AuditQuery(
            WarehouseId: assignedWarehouse.Id,
            Action: WmsAuditActions.ItemCreated));
        Assert.True(filtered.IsSuccess, filtered.Error);
        Assert.Single(filtered.Value.Items);
        Assert.Equal("assigned-1", filtered.Value.Items[0].EntityId);
        Assert.Equal("ASSIGNED", filtered.Value.Items[0].WarehouseCode);

        var beforeEntries = await queryService.SearchAsync(new AuditQuery(
            ToUtc: new DateTimeOffset(2026, 9, 20, 10, 29, 59, TimeSpan.Zero)));
        Assert.True(beforeEntries.IsSuccess, beforeEntries.Error);
        Assert.Empty(beforeEntries.Value.Items);
    }

    [Fact]
    public async Task AccountEventsUseDedicatedAuditActionsAndSafeStateChanges()
    {
        var authenticationAudit = new AuthenticationAuditService(
            _context,
            _writer,
            new FixedClock(new DateTimeOffset(2026, 9, 20, 10, 30, 0, TimeSpan.Zero)),
            NullLogger<AuthenticationAuditService>.Instance);

        await authenticationAudit.RecordAsync(
            WmsAuthenticationEventTypes.AccountCreated,
            succeeded: true,
            userId: "target-1",
            userName: "target");
        await authenticationAudit.RecordAsync(
            WmsAuthenticationEventTypes.AccountDisabledByAdmin,
            succeeded: true,
            userId: "target-1",
            userName: "target");

        var entries = await _context.AuditEntries
            .OrderBy(entry => entry.Id)
            .ToListAsync();

        var created = Assert.Single(entries, entry => entry.Action == WmsAuditActions.AccountCreated);
        Assert.Equal(WmsAuditEntityTypes.User, created.EntityType);
        Assert.Equal("target-1", created.EntityId);
        Assert.Contains("\"isActive\":true", created.AfterJson);

        var disabled = Assert.Single(entries, entry => entry.Action == WmsAuditActions.AccountStatusChanged);
        Assert.Equal("target-1", disabled.EntityId);
        Assert.Contains("\"isActive\":true", disabled.BeforeJson);
        Assert.Contains("\"isActive\":false", disabled.AfterJson);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow => value;
    }

    private sealed class ScopedWarehouseAccessService(int warehouseId) : IWarehouseAccessService
    {
        public Task<bool> HasPermissionAsync(
            string permission,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Result> AuthorizeAsync(
            string permission,
            int? requestedWarehouseId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                requestedWarehouseId is null || requestedWarehouseId == warehouseId
                    ? Result.Success()
                    : Result.Failure("Warehouse is not assigned."));

        public Task<WarehouseAccessScope> GetScopeAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WarehouseAccessScope(false, new HashSet<int> { warehouseId }));

        public Task<IReadOnlyList<WmsWarehouseOption>> GetAccessibleWarehousesAsync(
            string permission,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WmsWarehouseOption>>([]);
    }
}
