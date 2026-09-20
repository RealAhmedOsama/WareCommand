using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Identification;
using Wms.Application.Idempotency;
using Wms.Application.Inventory;
using Wms.Application.InventoryStatuses;
using Wms.Application.LicensePlates;
using Wms.Application.Items;
using Wms.Application.Jobs;
using Wms.Application.Locations;
using Wms.Application.Lots;
using Wms.Application.SerialNumbers;
using Wms.Application.Settings;
using Wms.Application.Units;
using Wms.Application.Warehouses;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Identification;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.InventoryStatuses;
using Wms.Infrastructure.LicensePlates;
using Wms.Infrastructure.Items;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Locations;
using Wms.Infrastructure.Lots;
using Wms.Infrastructure.Logging;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.SerialNumbers;
using Wms.Infrastructure.Services;
using Wms.Infrastructure.Settings;
using Wms.Infrastructure.Telemetry;
using Wms.Infrastructure.Units;
using Wms.Infrastructure.Warehouses;

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
        services.TryAddSingleton<IWmsOperationContextAccessor, WmsOperationContextAccessor>();
        services.TryAddScoped<IRequestContext, WmsRequestContext>();
        services.TryAddScoped<IWarehouseContext, WmsWarehouseContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IWmsJobExecutionStore, WmsJobExecutionStore>();
        services.AddScoped<WmsJobHandlerCatalog>();
        services.AddScoped<WmsExpiryAlertJob>();
        services.AddScoped<WmsLowStockAlertJob>();
        services.AddScoped<WmsReportGenerationJob>();
        services.AddScoped<WmsIntegrationRetryJob>();
        services.AddScoped<WmsCleanupJob>();
        services.AddScoped<WmsDatabaseBackupJob>();
        services.AddScoped<WmsCycleCountGenerationJob>();
        services.AddScoped<WmsReplenishmentGenerationJob>();
        services.AddSingleton<WmsSettingsCache>();
        services.AddScoped<IWmsSettingsService, WmsSettingsService>();
        services.AddScoped<IWarehouseManagementService, WarehouseManagementService>();
        services.AddScoped<ILocationManagementService, LocationManagementService>();
        services.AddScoped<IItemManagementService, ItemManagementService>();
        services.AddScoped<IIdentificationRegistry, IdentificationRegistry>();
        services.AddScoped<IIdentificationService, IdentificationService>();
        services.AddScoped<ILotService, LotService>();
        services.AddScoped<ISerialNumberService, SerialNumberService>();
        services.AddScoped<IInventoryStatusService, InventoryStatusService>();
        services.AddScoped<ILicensePlateService, LicensePlateService>();
        services.AddScoped<IUnitOfMeasureManagementService, UnitOfMeasureService>();
        services.AddScoped<IItemQuantityConversionService, UnitOfMeasureService>();
        services.AddScoped<WmsAuthorizationBootstrapper>();
        services.AddDatabaseInitialization();
        services.AddSingleton<WmsDbCommandMetricsInterceptor>();

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

        services.AddSingleton(new WmsDatabaseOptions(
            provider,
            connectionStringConfigured: provider == WmsDatabaseProvider.Sqlite ||
                !string.IsNullOrWhiteSpace(connectionString)));
        services.AddDbContext<WmsDbContext>((serviceProvider, options) =>
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

            options.AddInterceptors(
                serviceProvider.GetRequiredService<WmsDbCommandMetricsInterceptor>());
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ILotRepository, LotRepository>();
        services.AddScoped<ISerialNumberRepository, SerialNumberRepository>();
        services.AddScoped<IInventoryStatusRepository, InventoryStatusRepository>();
        services.AddScoped<ILicensePlateRepository, LicensePlateRepository>();
        services.AddScoped<IInventoryBalanceRepository, InventoryBalanceRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IInventoryCommandIdempotencyRepository, InventoryCommandIdempotencyRepository>();
        services.AddScoped<IInventoryReservationRepository, InventoryReservationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();

        return services;
    }

    private static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IStockMovementService, StockMovementService>();
        services.AddScoped<IInventoryLedgerService, InventoryLedgerService>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();
        services.AddScoped<IInventoryCommandIdempotencyService, InventoryCommandIdempotencyService>();
        services.AddScoped<IInventoryInquiryService, InventoryInquiryService>();
        services.AddScoped<IInventoryReplenishmentPolicyService, InventoryReplenishmentPolicyService>();
        return services;
    }

    private static IServiceCollection AddDatabaseInitialization(this IServiceCollection services)
    {
        services.AddScoped<IWmsSeedService, WmsSeedService>();
        services.AddScoped<IWmsDatabaseInitializer, WmsDatabaseInitializer>();
        return services;
    }
}
