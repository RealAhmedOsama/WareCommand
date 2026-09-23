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
        _service = CreateService(_adapter);
    }

    public async Task InitializeAsync() => await _context.Database.EnsureCreatedAsync();

    public async Task DisposeAsync() => await _context.DisposeAsync();

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private ConnectorService CreateService(params IConnectorAdapter[] adapters)
    {
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
        return new ConnectorService(
            _context,
            adapters,
            access.Object,
            _clock,
            requestContext,
            NullLogger<ConnectorService>.Instance);
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
                [WmsConnectorModes.Pull],
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

    [Fact]
    public async Task ReferenceAdapterIsVisibleButCannotActivateVerifyOrSynchronize()
    {
        var mapping = await SaveMappingAsync();
        var referenceAdapter = new GenericErpReferenceConnectorAdapter();
        var service = CreateService(referenceAdapter);
        var request = new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "ERP reference fixture",
            [17],
            "secret-manager://warehouse/erp-reference",
            [WmsConnectorModes.Pull],
            null,
            mapping.Value.Name,
            mapping.Value.Version);

        var activation = await service.CreateAsync(request with { Activate = true });
        activation.IsFailure.Should().BeTrue();
        activation.ErrorCode.Should().Be("connector.reference_adapter_not_operational");

        var created = await service.CreateAsync(request);
        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.ImplementationStatus.Should().Be(WmsConnectorImplementationStatuses.Reference);
        created.Value.CapabilityStatus.Should().Be(WmsConnectorCapabilityStatuses.Reference);
        created.Value.ConfigurationStatus.Should().Be(WmsConnectorConfigurationStatuses.Disabled);
        created.Value.VerificationStatus.Should().Be(WmsConnectorVerificationStatuses.Unverified);
        created.Value.HealthStatus.Should().Be(WmsConnectorHealthStatuses.Unknown);

        var connection = await service.TestConnectionAsync(created.Value.Id);
        connection.IsFailure.Should().BeTrue();
        connection.ErrorCode.Should().Be("connector.reference_adapter_not_operational");
        (await _context.ConnectorRuns.CountAsync()).Should().Be(0);

        var persisted = await _context.ConnectorInstances.SingleAsync();
        persisted.Status = WmsConnectorStatuses.Active;
        persisted.HealthStatus = WmsConnectorHealthStatuses.Healthy;
        persisted.HealthSummary = "legacy reference adapter reported healthy";
        persisted.LastHealthCheckAtUtc = _clock.UtcNow;
        persisted.LastSuccessfulRunAtUtc = _clock.UtcNow;
        await _context.SaveChangesAsync();

        var listed = await service.ListAsync();
        listed.Value.Should().ContainSingle();
        listed.Value[0].ImplementationStatus.Should().Be(WmsConnectorImplementationStatuses.Reference);
        listed.Value[0].CapabilityStatus.Should().Be(WmsConnectorCapabilityStatuses.Reference);
        listed.Value[0].HealthStatus.Should().Be(WmsConnectorHealthStatuses.Unknown);
        listed.Value[0].HealthSummary.Should().Contain("provider transport is disabled");
        listed.Value[0].LastHealthCheckAtUtc.Should().BeNull();
        listed.Value[0].LastSuccessfulRunAtUtc.Should().BeNull();

        var run = await service.RunAsync(new ConnectorRunRequest(
            created.Value.Id,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorModes.Pull,
            WarehouseId: 17,
            IdempotencyKey: "reference-run"));
        run.IsFailure.Should().BeTrue();
        run.ErrorCode.Should().Be("connector.reference_adapter_not_operational");
        (await _context.ConnectorRuns.CountAsync()).Should().Be(0);
        (await _context.ConnectorExternalRecords.CountAsync()).Should().Be(0);

        var connectorContext = new ConnectorInstanceContext(
            created.Value.Id,
            WmsConnectorTypes.GenericErpV1,
            created.Value.Name,
            new HashSet<int> { 17 },
            created.Value.CredentialReference,
            created.Value.CredentialVersion,
            new HashSet<string>([WmsConnectorModes.Pull]),
            created.Value.MappingProfileName,
            created.Value.MappingProfileVersion,
            null,
            "reference-test");
        var directHealth = await referenceAdapter.TestConnectionAsync(connectorContext);
        directHealth.Succeeded.Should().BeFalse();
        directHealth.HealthStatus.Should().Be(WmsConnectorHealthStatuses.Unknown);
        referenceAdapter.SupportedModes.Should().BeEmpty();
        referenceAdapter.SupportedOperations.Should().BeEmpty();
        await Assert.ThrowsAsync<NotSupportedException>(() => referenceAdapter.PullAsync(new ConnectorPullRequest(
            connectorContext,
            WmsConnectorOperations.OutboundOrders,
            null,
            100,
            "reference-pull")));
        await Assert.ThrowsAsync<NotSupportedException>(() => referenceAdapter.PushAsync(new ConnectorPushRequest(
            connectorContext,
            WmsConnectorOperations.OutboundOrders,
            "reference-push",
            100)));
    }

    [Fact]
    public async Task MissingTransportHasDistinctTypedErrorsAndContractOnlyCapabilities()
    {
        var mapping = await SaveMappingAsync();
        var active = await _service.CreateAsync(new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "ERP controlled adapter",
            [17],
            "secret-manager://warehouse/erp-controlled",
            [WmsConnectorModes.Pull],
            null,
            mapping.Value.Name,
            mapping.Value.Version,
            Activate: true));
        active.IsSuccess.Should().BeTrue(active.Error);

        var serviceWithoutTransport = CreateService();
        var capabilities = await serviceWithoutTransport.GetCapabilitiesAsync();
        capabilities.IsSuccess.Should().BeTrue(capabilities.Error);
        var genericErp = capabilities.Value.Single(item => item.ConnectorType == WmsConnectorTypes.GenericErpV1);
        genericErp.ImplementationStatus.Should().Be(WmsConnectorImplementationStatuses.ContractOnly);
        genericErp.CanActivate.Should().BeFalse();
        genericErp.SupportedModes.Should().BeEmpty();
        genericErp.SupportedOperations.Should().BeEmpty();
        genericErp.ConfigurationStatus.Should().Be(WmsConnectorConfigurationStatuses.Unconfigured);
        genericErp.VerificationStatus.Should().Be(WmsConnectorVerificationStatuses.Unverified);

        var connection = await serviceWithoutTransport.TestConnectionAsync(active.Value.Id);
        connection.IsFailure.Should().BeTrue();
        connection.ErrorCode.Should().Be("connector.transport_not_implemented");

        var run = await serviceWithoutTransport.RunAsync(new ConnectorRunRequest(
            active.Value.Id,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorModes.Pull,
            WarehouseId: 17,
            IdempotencyKey: "missing-transport-run"));
        run.IsFailure.Should().BeTrue();
        run.ErrorCode.Should().Be("connector.transport_not_implemented");

        var create = await serviceWithoutTransport.CreateAsync(new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "ERP without transport",
            [17],
            "secret-manager://warehouse/erp-unavailable",
            [WmsConnectorModes.Pull],
            null,
            mapping.Value.Name,
            mapping.Value.Version));
        create.IsFailure.Should().BeTrue();
        create.ErrorCode.Should().Be("connector.transport_not_implemented");
    }

    [Fact]
    public async Task ControlledAdapterCapabilitiesAndConnectionCheckReportVerifiedHealth()
    {
        var mapping = await SaveMappingAsync();
        var capabilities = await _service.GetCapabilitiesAsync();
        capabilities.IsSuccess.Should().BeTrue(capabilities.Error);
        var genericErp = capabilities.Value.Single(item => item.ConnectorType == WmsConnectorTypes.GenericErpV1);
        genericErp.ImplementationStatus.Should().Be(WmsConnectorImplementationStatuses.Implemented);
        genericErp.CanActivate.Should().BeTrue();
        genericErp.SupportedModes.Should().Equal(WmsConnectorModes.Pull);
        genericErp.SupportedOperations.Should().Equal(WmsConnectorOperations.OutboundOrders);

        var unsupportedMode = await _service.CreateAsync(new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "Controlled adapter file mode",
            [17],
            "secret-manager://warehouse/erp-file-mode",
            [WmsConnectorModes.File],
            null,
            mapping.Value.Name,
            mapping.Value.Version,
            Activate: true));
        unsupportedMode.IsFailure.Should().BeTrue();
        unsupportedMode.ErrorCode.Should().Be("connector.mode_not_supported");

        var created = await _service.CreateAsync(new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "Controlled ERP adapter",
            [17],
            "secret-manager://warehouse/erp-controlled-health",
            [WmsConnectorModes.Pull],
            null,
            mapping.Value.Name,
            mapping.Value.Version,
            Activate: true));
        created.IsSuccess.Should().BeTrue(created.Error);

        var connection = await _service.TestConnectionAsync(created.Value.Id);
        connection.IsSuccess.Should().BeTrue(connection.Error);
        connection.Value.Status.Should().Be(WmsConnectorRunStatuses.Succeeded);
        var listed = await _service.ListAsync();
        listed.Value.Single().CapabilityStatus.Should().Be(WmsConnectorCapabilityStatuses.Healthy);
        listed.Value.Single().ConfigurationStatus.Should().Be(WmsConnectorConfigurationStatuses.Configured);
        listed.Value.Single().VerificationStatus.Should().Be(WmsConnectorVerificationStatuses.Verified);
        listed.Value.Single().HealthStatus.Should().Be(WmsConnectorHealthStatuses.Healthy);

        var rotated = await _service.RotateCredentialAsync(
            created.Value.Id,
            new ConnectorCredentialRotationRequest("secret-manager://warehouse/erp-controlled-health-v2"));
        rotated.IsSuccess.Should().BeTrue(rotated.Error);
        rotated.Value.CapabilityStatus.Should().Be(WmsConnectorCapabilityStatuses.Unverified);
        rotated.Value.ConfigurationStatus.Should().Be(WmsConnectorConfigurationStatuses.Configured);
        rotated.Value.VerificationStatus.Should().Be(WmsConnectorVerificationStatuses.Unverified);
        rotated.Value.HealthStatus.Should().Be(WmsConnectorHealthStatuses.Unknown);
        rotated.Value.LastHealthCheckAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task FailedProviderCheckCannotPersistHealthyCapabilityState()
    {
        var mapping = await SaveMappingAsync();
        _adapter.Health = new ConnectorHealthResult(
            false,
            false,
            WmsConnectorHealthStatuses.Healthy,
            "provider rejected the credential reference");
        var created = await _service.CreateAsync(new ConnectorInstanceCreateRequest(
            WmsConnectorTypes.GenericErpV1,
            "Rejected ERP adapter",
            [17],
            "secret-manager://warehouse/erp-rejected",
            [WmsConnectorModes.Pull],
            null,
            mapping.Value.Name,
            mapping.Value.Version,
            Activate: true));
        created.IsSuccess.Should().BeTrue(created.Error);

        var connection = await _service.TestConnectionAsync(created.Value.Id);
        connection.IsSuccess.Should().BeTrue(connection.Error);
        connection.Value.Status.Should().Be(WmsConnectorRunStatuses.Failed);
        var listed = await _service.ListAsync();
        listed.Value.Single().CapabilityStatus.Should().Be(WmsConnectorCapabilityStatuses.Failed);
        listed.Value.Single().VerificationStatus.Should().Be(WmsConnectorVerificationStatuses.Failed);
        listed.Value.Single().HealthStatus.Should().Be(WmsConnectorHealthStatuses.Unhealthy);
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
        public ConnectorHealthResult Health { get; set; } = new(
            true,
            false,
            WmsConnectorHealthStatuses.Healthy,
            "test connector");

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

        public string ImplementationStatus => WmsConnectorImplementationStatuses.Implemented;

        public IReadOnlySet<string> SupportedModes { get; } = new HashSet<string>(
            [WmsConnectorModes.Pull],
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> SupportedOperations { get; } = new HashSet<string>(
            [WmsConnectorOperations.OutboundOrders],
            StringComparer.OrdinalIgnoreCase);

        public Task<ConnectorHealthResult> TestConnectionAsync(
            ConnectorInstanceContext connector,
            CancellationToken cancellationToken = default) => Task.FromResult(Health);

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
