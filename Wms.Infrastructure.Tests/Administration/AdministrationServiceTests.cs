using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wms.Application.Administration;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Administration;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Settings;

namespace Wms.Infrastructure.Tests.Administration;

public sealed class AdministrationServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly FixedClock clock = new(
        new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IWarehouseAccessService> warehouseAccess = new();
    private readonly Mock<IAuditQueryService> auditQuery = new();
    private readonly WmsDbContext context;
    private readonly AdministrationService service;
    private readonly int warehouseId;

    public AdministrationServiceTests()
    {
        connection.Open();
        context = new WmsDbContext(
            new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlite(connection)
                .Options,
            clock);
        context.Database.EnsureCreated();

        var warehouse = new Warehouse("ADM-01", "Administration test warehouse");
        context.Warehouses.Add(warehouse);
        context.SaveChanges();
        warehouseId = warehouse.Id;

        warehouseAccess
            .Setup(item => item.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        warehouseAccess
            .Setup(item => item.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        warehouseAccess
            .Setup(item => item.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        auditQuery
            .Setup(item => item.SearchAsync(
                It.IsAny<AuditQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuditPage([], 1, 25, 0)));

        service = new AdministrationService(
            context,
            clock,
            warehouseAccess.Object,
            auditQuery.Object);
    }

    [Fact]
    public async Task Readiness_reports_configuration_and_execution_blockers()
    {
        var result = await service.GetReadinessAsync(warehouseId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.OverallStatus.Should().Be(AdministrationReadinessStatus.Blocked);
        result.Value.Checks.Single(item => item.Key == "global-settings").IsBlocking.Should().BeTrue();
        result.Value.Checks.Single(item => item.Key == $"inbound-execution-{warehouseId}").Status
            .Should().Be(AdministrationReadinessStatus.Blocked);
        result.Value.Checks.Single(item => item.Key == $"outbound-execution-{warehouseId}").Status
            .Should().Be(AdministrationReadinessStatus.Blocked);
        var connectors = result.Value.Checks.Single(item => item.Key == "connector-transports");
        connectors.Status.Should().Be(AdministrationReadinessStatus.Warning);
        connectors.IsBlocking.Should().BeFalse();
        connectors.Detail.Should().Contain("contract-only");
        connectors.Detail.Should().Contain("reference fixtures are excluded");
    }

    [Fact]
    public async Task Readiness_reports_configured_execution_without_blockers()
    {
        context.GlobalSettings.Add(new WmsGlobalSettingsEntity
        {
            CompanyName = "Administration test company",
            CompanyCode = "ADM"
        });

        var roles = Warehouse.RequiredOperationalLocationRoles;
        foreach (var role in roles)
        {
            var location = new Location(
                $"{role.ToString().ToUpperInvariant()}-01",
                role.ToString(),
                warehouseId);
            context.Locations.Add(location);
            context.SaveChanges();
            context.WarehouseOperationalLocations.Add(
                new WarehouseOperationalLocation(warehouseId, location.Id, role));
        }

        var warehouse = await context.Warehouses.SingleAsync(item => item.Id == warehouseId);
        warehouse.EnableWorkflows(roles);
        await context.SaveChangesAsync();

        var result = await service.GetReadinessAsync(warehouseId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Checks.Should().NotContain(item => item.IsBlocking);
        result.Value.Checks.Single(item => item.Key == $"inbound-execution-{warehouseId}").Status
            .Should().Be(AdministrationReadinessStatus.Ready);
        result.Value.Checks.Single(item => item.Key == $"outbound-execution-{warehouseId}").Status
            .Should().Be(AdministrationReadinessStatus.Ready);
    }

    [Fact]
    public async Task Catalog_marks_each_surface_with_the_callers_permission()
    {
        warehouseAccess
            .Setup(item => item.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string permission, CancellationToken _) =>
                permission == WmsPermissions.AccessManage);

        var result = await service.GetCatalogAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().Contain(item => item.Key == "access.users" && item.IsAuthorized);
        result.Value.Should().NotContain(item => item.Key == "organization.settings" && item.IsAuthorized);
    }

    [Fact]
    public async Task History_uses_the_existing_paged_audit_query()
    {
        var result = await service.GetHistoryAsync(new AdministrationHistoryQuery(
            WarehouseId: warehouseId,
            Page: 0,
            PageSize: 500));

        result.IsSuccess.Should().BeTrue(result.Error);
        auditQuery.Verify(item => item.SearchAsync(
                It.Is<AuditQuery>(query =>
                    query.WarehouseId == warehouseId &&
                    query.Page == 1 &&
                    query.PageSize == 200),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    public void Dispose()
    {
        context.Dispose();
        connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
