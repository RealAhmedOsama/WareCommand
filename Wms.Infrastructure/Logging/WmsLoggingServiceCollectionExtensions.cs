using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wms.Application.Context;

namespace Wms.Infrastructure.Logging;

public static class WmsLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddWmsLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IWmsOperationContextAccessor, WmsOperationContextAccessor>();
        services.TryAddSingleton(WmsLoggingOptions.From(configuration));
        services.AddHostedService<WmsHostLifecycleLoggingService>();
        return services;
    }
}
