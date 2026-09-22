using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wms.Application.Common;
using Wms.Application.Connectors;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Connectors;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Connectors;

public sealed class ConnectorServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 22, 13, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly CapturingConnectorAdapter _adapter = new();
    private readonly ConnectorService _service;

    public ConnectorServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(_connection)
                .Options,
            _clock);
        var access = new Mock<IWarehouseAccessService>();
        access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        var requestContext = new WmsRequestContext("Test");
        requestContext.Initialize("connector-test", "Test");
        _service = new ConnectorService(
            _context,
            [_adapter],
            access.Object,
            _clock,
            requestContext,
            NullLogger<ConnectorService>.Instance);
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
    public async Task MappingProfileAndInstanceKeepCredentialReferencesAndScope()
    {
        var mapping = await SaveMappingAsync();

        var created = await _service.CreateAsync(
            new ConnectorInstanceCreateRequest(
                WmsConnectorTypes.GenericErpV1,
                "ERP main",
                [17, 18],
                "secret-manager://warehouse/erp-main",
                [WmsConnectorModes.Pull, WmsConnectorModes.Push],
                "*/15 * * * *",
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.Status.Should().Be(WmsConnectorStatuses.Active);
        created.Value.AllowedWarehouseIds.Should().Equal(17, 18);
        created.Value.CredentialReference.Should().Be("secret-manager://warehouse/erp-main");
        created.Value.CredentialVersion.Should().Be(1);

        var rotated = await _service.RotateCredentialAsync(
            created.Value.Id,
            new ConnectorCredentialRotationRequest("secret-manager://warehouse/erp-main-v2"));

        rotated.IsSuccess.Should().BeTrue(rotated.Error);
        rotated.Value.CredentialVersion.Should().Be(2);
        rotated.Value.CredentialReference.Should().Be("secret-manager://warehouse/erp-main-v2");
        (await _context.ConnectorInstances.SingleAsync()).CredentialReference
            .Should().NotContain("secret=");
    }

    [Fact]
    public async Task PullRunPersistsCheckpointAndDoesNotDuplicateAnExternalRecord()
    {
        var mapping = await SaveMappingAsync();
        var created = await _service.CreateAsync(
            new ConnectorInstanceCreateRequest(
                WmsConnectorTypes.GenericErpV1,
                "Orders",
                [17],
                "secret-manager://warehouse/orders",
                [WmsConnectorModes.Pull],
                null,
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));

        var request = new ConnectorRunRequest(
            created.Value.Id,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorModes.Pull,
            WarehouseId: 17,
            Cursor: null,
            IdempotencyKey: "orders-run-1");
        var first = await _service.RunAsync(request);

        first.IsSuccess.Should().BeTrue(first.Error);
        first.Value.Status.Should().Be(WmsConnectorRunStatuses.Succeeded);
        first.Value.RecordsCreated.Should().Be(1);
        first.Value.CursorAfter.Should().Be("cursor-2");
        (await _context.ConnectorExternalRecords.CountAsync()).Should().Be(1);

        var duplicate = await _service.RunAsync(request);
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.Id.Should().Be(first.Value.Id);
        (await _context.ConnectorRuns.CountAsync()).Should().Be(1);

        var replay = await _service.RunAsync(request with { IdempotencyKey = "orders-run-2", Cursor = "cursor-2" });
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.RecordsSkipped.Should().Be(1);
        replay.Value.RecordsCreated.Should().Be(0);
        (await _context.ConnectorExternalRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SensitiveCredentialReferenceAndOutOfScopeRecordAreRejectedWithoutRawPayload()
    {
        var mapping = await SaveMappingAsync();
        var invalid = await _service.CreateAsync(
            new ConnectorInstanceCreateRequest(
                WmsConnectorTypes.GenericErpV1,
                "ERP",
                [17],
                "https://example.test?token=raw-secret",
                [WmsConnectorModes.Pull],
                null,
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));

        invalid.IsFailure.Should().BeTrue();
        invalid.ErrorCode.Should().Be("connector.credential_reference_invalid");

        var created = await _service.CreateAsync(
            new ConnectorInstanceCreateRequest(
                WmsConnectorTypes.GenericErpV1,
                "ERP",
                [17],
                "secret-manager://warehouse/erp",
                [WmsConnectorModes.Pull],
                null,
                mapping.Value.Name,
                mapping.Value.Version,
                Activate: true));
        _adapter.Record = new ConnectorExternalRecord(
            "order",
            "outside-scope",
            99,
            new Dictionary<string, string?> { ["orderNo"] = "SO-99" });

        var result = await _service.RunAsync(new ConnectorRunRequest(
            created.Value.Id,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorModes.Pull,
            WarehouseId: 17,
            IdempotencyKey: "outside-scope"));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Status.Should().Be(WmsConnectorRunStatuses.Failed);
        result.Value.Conflicts.Should().Be(1);
        (await _context.ConnectorExternalRecords.CountAsync()).Should().Be(0);
    }

    private async Task<Result<ConnectorMappingProfileDto>> SaveMappingAsync(
        string connectorType = WmsConnectorTypes.GenericErpV1,
        string conflictPolicy = WmsConnectorConflictPolicies.Reject)
    {
        return await _service.SaveMappingProfileAsync(
            new ConnectorMappingProfileRequest(
                connectorType,
                "orders-v1",
                1,
                "orderNo",
                [
                    new ConnectorMappingRule("orderNo", "ExternalOrderNumber", "trim"),
                    new ConnectorMappingRule("status", "Status", ValueMap: new Dictionary<string, string>
                    {
                        ["open"] = "Open"
                    })
                ],
                ConflictPolicy: conflictPolicy));
    }

    private sealed class CapturingConnectorAdapter : IConnectorAdapter
    {
        public ConnectorExternalRecord Record { get; set; } = new(
            "order",
            "SO-1",
            17,
            new Dictionary<string, string?>
            {
                ["orderNo"] = "SO-1",
                ["status"] = "open"
            },
            "v1");

        public string ConnectorType => WmsConnectorTypes.GenericErpV1;

        public IReadOnlySet<string> SupportedModes { get; } = new HashSet<string>(
            [WmsConnectorModes.Pull],
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> SupportedOperations { get; } = new HashSet<string>(
            [WmsConnectorOperations.OutboundOrders],
            StringComparer.OrdinalIgnoreCase);

        public Task<ConnectorHealthResult> TestConnectionAsync(
            ConnectorInstanceContext connector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorHealthResult(
                true,
                false,
                WmsConnectorHealthStatuses.Healthy,
                "test connector"));

        public Task<ConnectorPullResult> PullAsync(
            ConnectorPullRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorPullResult([Record], "cursor-2", false));

        public Task<ConnectorPushResult> PushAsync(
            ConnectorPushRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectorPushResult(0));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
