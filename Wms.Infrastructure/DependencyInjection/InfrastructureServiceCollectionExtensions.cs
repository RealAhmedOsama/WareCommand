using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Services;

namespace Wms.Infrastructure.DependencyInjection;

/// <summary>
/// Registers persistence and infrastructure adapters for the composition roots.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWmsInfrastructure(
        this IServiceCollection services,
        string? connectionString,
        WmsDatabaseProvider provider = WmsDatabaseProvider.PostgreSql)
    {
        services.AddPersistence(connectionString, provider);
        services.AddInventoryInfrastructure();
        services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddScoped<IWarehouseAccessService, WarehouseAccessService>();
        services.AddScoped<IUserAccessDirectory, UserAccessDirectory>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<IRequestContext, WmsRequestContext>();
        services.TryAddScoped<IWarehouseContext, WmsWarehouseContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<WmsAuthorizationBootstrapper>();
        services.AddDatabaseInitialization();

        return services;
    }

    public static IServiceCollection AddWmsDesktopIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<WmsUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<WmsDbContext>();

        services.AddSingleton<DesktopUserSession>();
        services.AddSingleton<Wms.Application.Identity.ICurrentUser>(
            provider => provider.GetRequiredService<DesktopUserSession>());
        services.AddScoped<IRequestContext>(_ => new WmsRequestContext("Desktop"));
        services.AddScoped<IDesktopAuthenticationService, DesktopAuthenticationService>();
        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string? connectionString,
        WmsDatabaseProvider provider)
    {
        if (provider == WmsDatabaseProvider.PostgreSql && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required when Wms:DatabaseProvider is PostgreSql.");
        }

        var resolvedConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=warehouse.db"
            : connectionString;

        services.AddSingleton(new WmsDatabaseOptions(provider));
        services.AddDbContext<WmsDbContext>(options =>
        {
            if (provider == WmsDatabaseProvider.PostgreSql)
            {
                options.UseNpgsql(
                    resolvedConnectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName));
            }
            else
            {
                options.UseSqlite(resolvedConnectionString);
            }
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();

        return services;
    }

    private static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IStockMovementService, StockMovementService>();
        return services;
    }

    private static IServiceCollection AddDatabaseInitialization(this IServiceCollection services)
    {
        services.AddScoped<IWmsSeedService, WmsSeedService>();
        services.AddScoped<IWmsDatabaseInitializer, WmsDatabaseInitializer>();
        return services;
    }
}
