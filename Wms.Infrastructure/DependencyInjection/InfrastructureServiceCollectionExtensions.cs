using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
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
        string? connectionString)
    {
        services.AddPersistence(connectionString);
        services.AddInventoryInfrastructure();
        services.AddDatabaseInitialization();

        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string? connectionString)
    {
        var resolvedConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=warehouse.db"
            : connectionString;

        services.AddDbContext<WmsDbContext>(options => options.UseSqlite(resolvedConnectionString));
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
        services.AddScoped<IWmsDatabaseInitializer, WmsDatabaseInitializer>();
        return services;
    }
}
