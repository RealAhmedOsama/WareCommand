using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.ApiClients;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Identity;

public sealed class WmsAuthorizationTests : IAsyncLifetime, IDisposable
{
    private const string ValidPassword = "ValidPassword123!";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly CountingCommandInterceptor _commandCounter = new();
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<WmsDbContext>(options =>
            options.UseSqlite(_connection).AddInterceptors(_commandCounter));
        services
            .AddIdentityCore<WmsUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<WmsDbContext>();
        services.AddSingleton<DesktopUserSession>();
        services.AddScoped<Wms.Application.Identity.ICurrentUser>(
            provider => provider.GetRequiredService<DesktopUserSession>());
        services.AddScoped<IWarehouseAccessService, WarehouseAccessService>();
        services.AddScoped<IWarehouseNavigationAccessService, WarehouseAccessService>();
        services.AddScoped<IApiClientContextAccessor, ApiClientContextAccessor>();
        services.AddScoped<IUserAccessDirectory, UserAccessDirectory>();
        services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IRequestContext, WmsRequestContext>();
        services.AddScoped<IWarehouseContext, WmsWarehouseContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        _serviceProvider = services.BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
        await _scope.ServiceProvider.GetRequiredService<WmsDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _scope.Dispose();
        await _serviceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task AssignedUserCannotReadOrMutateAnUnassignedWarehouse()
    {
        var user = await CreateUserAsync("operator");
        var role = await CreateRoleAsync(WmsRoleNames.InventoryController);
        await AddPermissionAsync(role, WmsPermissions.InventoryRead);
        await AddPermissionAsync(role, WmsPermissions.InventoryAdjust);
        Assert.True((await GetUserManager().AddToRoleAsync(user, role.Name!)).Succeeded);

        var firstWarehouse = new Warehouse("MAIN", "Main Warehouse");
        var secondWarehouse = new Warehouse("SECOND", "Second Warehouse");
        var item = new Item("WIDGET-001", "Widget", "EA");
        GetContext().Warehouses.AddRange(firstWarehouse, secondWarehouse);
        GetContext().Items.Add(item);
        await GetContext().SaveChangesAsync();

        var firstLocation = new Location("MAIN-A", "Main A", firstWarehouse.Id);
        var secondLocation = new Location("SECOND-A", "Second A", secondWarehouse.Id);
        GetContext().Locations.AddRange(firstLocation, secondLocation);
        await GetContext().SaveChangesAsync();
        GetContext().Stock.AddRange(
            new Stock(item.Id, firstLocation.Id, new Quantity(10)),
            new Stock(item.Id, secondLocation.Id, new Quantity(20)));
        GetContext().UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = user.Id,
            WarehouseId = firstWarehouse.Id,
            IsDefault = true
        });
        await GetContext().SaveChangesAsync();

        GetSession().SignIn(user);
        var access = GetAccessService();
        Assert.True((await access.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            firstWarehouse.Id)).IsSuccess);
        Assert.False((await access.AuthorizeAsync(
            WmsPermissions.InventoryRead,
            secondWarehouse.Id)).IsSuccess);

        var unitOfWork = new UnitOfWork(GetContext(), access);
        var visibleStock = (await unitOfWork.Stock.GetAllAsync()).ToList();
        Assert.Single(visibleStock);
        Assert.Equal(firstLocation.Id, visibleStock[0].LocationId);
        Assert.Null(await unitOfWork.Locations.GetByIdAsync(secondLocation.Id));
        Assert.Single(await access.GetAccessibleWarehousesAsync(WmsPermissions.InventoryRead));

        GetContext().Movements.Add(new Movement(
            MovementType.Putaway,
            item.Id,
            new Quantity(1),
            user.Id,
            firstLocation.Id,
            secondLocation.Id));
        await GetContext().SaveChangesAsync();

        var movementRepository = new MovementRepository(GetContext(), access);
        Assert.Empty(await movementRepository.GetByItemIdAsync(item.Id));
    }

    [Fact]
    public async Task PermissionChangesTakeEffectWithoutTrustingStaleCookieClaims()
    {
        var user = await CreateUserAsync("operator");
        var role = await CreateRoleAsync(WmsRoleNames.Viewer);
        await AddPermissionAsync(role, WmsPermissions.InventoryRead);
        Assert.True((await GetUserManager().AddToRoleAsync(user, role.Name!)).Succeeded);

        GetSession().SignIn(user);
        var access = GetAccessService();
        Assert.True(await access.HasPermissionAsync(WmsPermissions.InventoryRead));
        Assert.False(await access.HasPermissionAsync(WmsPermissions.InventoryAdjust));

        Assert.True((await GetUserManager().AddClaimAsync(
            user,
            new Claim(WmsAuthorizationClaimTypes.Permission, WmsPermissions.InventoryAdjust))).Succeeded);
        Assert.True(await access.HasPermissionAsync(WmsPermissions.InventoryAdjust));

        Assert.True((await GetUserManager().RemoveClaimAsync(
            user,
            new Claim(WmsAuthorizationClaimTypes.Permission, WmsPermissions.InventoryAdjust))).Succeeded);
        Assert.False(await access.HasPermissionAsync(WmsPermissions.InventoryAdjust));
    }

    [Fact]
    public async Task NavigationAccessUsesOneFreshPermissionAndWarehouseSnapshot()
    {
        var user = await CreateUserAsync("navigation-operator");
        var role = await CreateRoleAsync(WmsRoleNames.InventoryController);
        await AddPermissionAsync(role, WmsPermissions.DashboardView);
        await AddPermissionAsync(role, WmsPermissions.InventoryRead);
        Assert.True((await GetUserManager().AddToRoleAsync(user, role.Name!)).Succeeded);

        var assignedWarehouse = new Warehouse("NAV-MAIN", "Navigation Main");
        var unassignedWarehouse = new Warehouse("NAV-OTHER", "Navigation Other");
        GetContext().Warehouses.AddRange(assignedWarehouse, unassignedWarehouse);
        await GetContext().SaveChangesAsync();
        GetContext().UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = user.Id,
            WarehouseId = assignedWarehouse.Id,
            IsDefault = true
        });
        await GetContext().SaveChangesAsync();

        GetSession().SignIn(user);
        var navigationService = GetNavigationAccessService();
        _commandCounter.Reset();

        var snapshot = await navigationService.GetNavigationAccessAsync();

        Assert.Equal(3, _commandCounter.Count);
        Assert.True(snapshot.HasPermission(WmsPermissions.DashboardView));
        Assert.True(snapshot.HasPermission(WmsPermissions.InventoryRead));
        Assert.False(snapshot.HasPermission(WmsPermissions.InventoryAdjust));
        var warehouse = Assert.Single(snapshot.AccessibleWarehouses);
        Assert.Equal(assignedWarehouse.Id, warehouse.Id);
        Assert.True(warehouse.IsDefault);

        Assert.True((await GetUserManager().AddClaimAsync(
            user,
            new Claim(WmsAuthorizationClaimTypes.Permission, WmsPermissions.InventoryAdjust))).Succeeded);
        Assert.True((await navigationService.GetNavigationAccessAsync())
            .HasPermission(WmsPermissions.InventoryAdjust));
    }

    [Fact]
    public async Task ApiClientNavigationUsesExactScopesAndWarehouseScope()
    {
        var assignedWarehouse = new Warehouse("API-NAV-MAIN", "API Navigation Main");
        var unassignedWarehouse = new Warehouse("API-NAV-OTHER", "API Navigation Other");
        GetContext().Warehouses.AddRange(assignedWarehouse, unassignedWarehouse);
        await GetContext().SaveChangesAsync();

        GetApiClientContextAccessor().Current = new ApiClientContext(
            "navigation-client",
            "Navigation Client",
            new HashSet<string>([WmsPermissions.DashboardView], StringComparer.Ordinal),
            new HashSet<int> { assignedWarehouse.Id },
            HasGlobalWarehouseAccess: false);

        var snapshot = await GetNavigationAccessService().GetNavigationAccessAsync();

        Assert.True(snapshot.HasPermission(WmsPermissions.DashboardView));
        Assert.False(snapshot.HasPermission(WmsPermissions.InventoryRead));
        var warehouse = Assert.Single(snapshot.AccessibleWarehouses);
        Assert.Equal(assignedWarehouse.Id, warehouse.Id);
        Assert.False(warehouse.IsDefault);

        GetApiClientContextAccessor().Current = new ApiClientContext(
            "legacy-all-scope-client",
            "Legacy All Scope Client",
            new HashSet<string>([WmsPermissions.All], StringComparer.Ordinal),
            new HashSet<int>(),
            HasGlobalWarehouseAccess: false);
        var legacySnapshot = await GetNavigationAccessService().GetNavigationAccessAsync();
        Assert.False(legacySnapshot.HasPermission(WmsPermissions.DashboardView));
        Assert.Empty(legacySnapshot.AccessibleWarehouses);
    }

    [Fact]
    public async Task UserNavigationWildcardIncludesActiveWarehousesAndDefaults()
    {
        var user = await CreateUserAsync("navigation-administrator");
        var role = await CreateRoleAsync(WmsRoleNames.Administrator);
        await AddPermissionAsync(role, WmsPermissions.All);
        Assert.True((await GetUserManager().AddToRoleAsync(user, role.Name!)).Succeeded);

        var assignedWarehouse = new Warehouse("ADMIN-NAV-MAIN", "Administrator Navigation Main");
        var unassignedWarehouse = new Warehouse("ADMIN-NAV-OTHER", "Administrator Navigation Other");
        GetContext().Warehouses.AddRange(assignedWarehouse, unassignedWarehouse);
        await GetContext().SaveChangesAsync();
        GetContext().UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = user.Id,
            WarehouseId = assignedWarehouse.Id,
            IsDefault = true
        });
        await GetContext().SaveChangesAsync();
        GetSession().SignIn(user);

        var snapshot = await GetNavigationAccessService().GetNavigationAccessAsync();

        Assert.True(snapshot.HasPermission(WmsPermissions.InventoryAdjust));
        Assert.Equal(2, snapshot.AccessibleWarehouses.Count);
        Assert.Contains(snapshot.AccessibleWarehouses, warehouse =>
            warehouse.Id == assignedWarehouse.Id && warehouse.IsDefault);
        Assert.Contains(snapshot.AccessibleWarehouses, warehouse =>
            warehouse.Id == unassignedWarehouse.Id && !warehouse.IsDefault);
    }

    [Fact]
    public async Task AdministratorAccessDirectoryUpdatesRolesPermissionsAndDefaultWarehouse()
    {
        var administrator = await CreateUserAsync("administrator");
        var administratorRole = await CreateRoleAsync(WmsRoleNames.Administrator);
        await AddPermissionAsync(administratorRole, WmsPermissions.All);
        Assert.True((await GetUserManager().AddToRoleAsync(administrator, administratorRole.Name!)).Succeeded);

        var target = await CreateUserAsync("receiver");
        var receiverRole = await CreateRoleAsync(WmsRoleNames.Receiver);
        await AddPermissionAsync(receiverRole, WmsPermissions.ReceivingExecute);

        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        GetContext().Warehouses.Add(warehouse);
        await GetContext().SaveChangesAsync();

        GetSession().SignIn(administrator);
        var directory = GetAccessDirectory();
        var update = await directory.UpdateAsync(
            administrator.Id,
            target.Id,
            [WmsRoleNames.Receiver],
            [WmsPermissions.InventoryAdjust],
            [warehouse.Id],
            warehouse.Id);

        Assert.True(update.IsSuccess, update.Error);
        var profile = await directory.GetProfileAsync(target.Id);
        Assert.NotNull(profile);
        Assert.Contains(profile!.Roles, role => role.Name == WmsRoleNames.Receiver && role.IsSelected);
        Assert.Contains(profile.Permissions, permission =>
            permission.Name == WmsPermissions.InventoryAdjust && permission.IsDirectGrant);
        Assert.Contains(profile.Warehouses, option =>
            option.Id == warehouse.Id && option.IsSelected && option.IsDefault);

        GetSession().SignIn(target);
        var access = GetAccessService();
        Assert.True((await access.AuthorizeAsync(
            WmsPermissions.InventoryAdjust,
            warehouse.Id)).IsSuccess);
        Assert.True((await access.AuthorizeAsync(
            WmsPermissions.ReceivingExecute,
            warehouse.Id)).IsSuccess);
    }

    private WmsDbContext GetContext() =>
        _scope.ServiceProvider.GetRequiredService<WmsDbContext>();

    private UserManager<WmsUser> GetUserManager() =>
        _scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();

    private RoleManager<IdentityRole> GetRoleManager() =>
        _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    private DesktopUserSession GetSession() =>
        _scope.ServiceProvider.GetRequiredService<DesktopUserSession>();

    private IWarehouseAccessService GetAccessService() =>
        _scope.ServiceProvider.GetRequiredService<IWarehouseAccessService>();

    private IWarehouseNavigationAccessService GetNavigationAccessService() =>
        _scope.ServiceProvider.GetRequiredService<IWarehouseNavigationAccessService>();

    private IApiClientContextAccessor GetApiClientContextAccessor() =>
        _scope.ServiceProvider.GetRequiredService<IApiClientContextAccessor>();

    private IUserAccessDirectory GetAccessDirectory() =>
        _scope.ServiceProvider.GetRequiredService<IUserAccessDirectory>();

    private async Task<WmsUser> CreateUserAsync(string userName)
    {
        var user = new WmsUser
        {
            UserName = userName,
            Email = userName + "@example.test",
            EmailConfirmed = true,
            DisplayName = userName,
            EmployeeCode = "EMP-" + userName.ToUpperInvariant(),
            Locale = "en-US",
            TimeZone = "UTC",
            IsActive = true,
            LockoutEnabled = true
        };
        var result = await GetUserManager().CreateAsync(user, ValidPassword);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private async Task<IdentityRole> CreateRoleAsync(string roleName)
    {
        var role = new IdentityRole(roleName);
        var result = await GetRoleManager().CreateAsync(role);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        return role;
    }

    private async Task AddPermissionAsync(IdentityRole role, string permission)
    {
        var result = await GetRoleManager().AddClaimAsync(
            role,
            new Claim(WmsAuthorizationClaimTypes.Permission, permission));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
