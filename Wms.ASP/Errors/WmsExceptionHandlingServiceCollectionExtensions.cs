using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Wms.ASP.Errors;

public static class WmsExceptionHandlingServiceCollectionExtensions
{
    public static IServiceCollection AddWmsExceptionHandling(this IServiceCollection services)
    {
        services.AddScoped<IExceptionHandler, WmsExceptionHandler>();
        return services;
    }
}
