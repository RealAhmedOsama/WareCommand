using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Wms.Application.Jobs;
using Wms.Infrastructure.Database;

namespace Wms.ASP.Jobs;

public static class WmsJobsServiceCollectionExtensions
{
    public static IServiceCollection AddWmsJobs(
        this IServiceCollection services,
        IConfiguration configuration,
        WmsDatabaseProvider databaseProvider,
        string? connectionString)
    {
        var options = WmsJobOptions.From(configuration);
        services.AddSingleton(options);
        services.AddScoped<WmsJobRunner>();

        if (!options.Enabled)
        {
            services.AddSingleton<IWmsJobDispatcher, WmsDisabledJobDispatcher>();
            return services;
        }

        if (databaseProvider != WmsDatabaseProvider.PostgreSql)
        {
            throw new InvalidOperationException(
                "Wms:Jobs:Enabled requires the PostgreSql database provider because durable jobs use PostgreSQL storage.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Wms:Jobs:Enabled requires a PostgreSQL connection string.");
        }

        services.AddHangfire((serviceProvider, globalConfiguration) =>
        {
            globalConfiguration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseFilter(new AutomaticRetryAttribute
                {
                    Attempts = options.MaximumRetryAttempts,
                    DelaysInSeconds = options.RetryDelaysInSeconds,
                    OnAttemptsExceeded = AttemptsExceededAction.Fail
                })
                .UsePostgreSqlStorage(
                    options => options.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = options.StorageSchema,
                        PrepareSchemaIfNecessary = options.PrepareSchemaIfNecessary
                    });
        });
        services.AddHangfireServer(serverOptions =>
        {
            serverOptions.Queues = WmsJobQueues.All.ToArray();
            serverOptions.WorkerCount = options.WorkerCount;
            serverOptions.ServerName = $"WareCommand:{Environment.MachineName}:{Guid.NewGuid():N}";
        });
        services.AddSingleton<IWmsJobDispatcher, WmsHangfireJobDispatcher>();
        services.AddHostedService<WmsRecurringJobRegistrar>();
        services.AddHostedService<WmsHangfireMonitoringService>();
        return services;
    }
}
