using Microsoft.Extensions.DependencyInjection;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Locations;
using Wms.Application.UseCases.Picking;
using Wms.Application.UseCases.Receiving;
using Wms.Application.UseCases.Reports;

namespace Wms.Application.DependencyInjection;

/// <summary>
/// Registers application workflows by business capability.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddWmsApplication(this IServiceCollection services)
    {
        services.AddCatalogModule();
        services.AddWarehousesModule();
        services.AddInventoryModule();
        services.AddInboundModule();
        services.AddOutboundModule();
        services.AddReportingModule();

        return services;
    }

    private static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<IGetItemsUseCase, GetItemsUseCase>();
        services.AddScoped<ICreateItemUseCase, CreateItemUseCase>();
        services.AddScoped<IUpdateItemUseCase, UpdateItemUseCase>();
        services.AddScoped<IDeleteItemUseCase, DeleteItemUseCase>();
        return services;
    }

    private static IServiceCollection AddWarehousesModule(this IServiceCollection services)
    {
        services.AddScoped<IGetLocationsUseCase, GetLocationsUseCase>();
        services.AddScoped<ICreateLocationUseCase, CreateLocationUseCase>();
        services.AddScoped<IUpdateLocationUseCase, UpdateLocationUseCase>();
        services.AddScoped<IDeleteLocationUseCase, DeleteLocationUseCase>();
        return services;
    }

    private static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddScoped<IGetStockUseCase, GetStockUseCase>();
        services.AddScoped<IStockAdjustmentUseCase, StockAdjustmentUseCase>();
        return services;
    }

    private static IServiceCollection AddInboundModule(this IServiceCollection services)
    {
        services.AddScoped<IReceiveItemUseCase, ReceiveItemUseCase>();
        services.AddScoped<IPutawayUseCase, PutawayUseCase>();
        return services;
    }

    private static IServiceCollection AddOutboundModule(this IServiceCollection services)
    {
        services.AddScoped<IPickOrderUseCase, PickOrderUseCase>();
        return services;
    }

    private static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<IMovementReportUseCase, MovementReportUseCase>();
        return services;
    }
}
