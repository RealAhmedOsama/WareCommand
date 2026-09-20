using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wms.Application.Logging;

namespace Wms.Infrastructure.Logging;

public sealed class WmsHostLifecycleLoggingService(
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILogger<WmsHostLifecycleLoggingService> logger) : IHostedService
{
    private CancellationTokenRegistration _started;
    private CancellationTokenRegistration _stopping;
    private CancellationTokenRegistration _stopped;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _started = lifetime.ApplicationStarted.Register(() =>
            logger.LogInformation(
                WmsLogEvents.ApplicationStarted,
                "WareCommand host started in {EnvironmentName}",
                environment.EnvironmentName));
        _stopping = lifetime.ApplicationStopping.Register(() =>
            logger.LogInformation(
                WmsLogEvents.ApplicationStopping,
                "WareCommand host stopping in {EnvironmentName}",
                environment.EnvironmentName));
        _stopped = lifetime.ApplicationStopped.Register(LogStopped);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _started.Dispose();
        _stopping.Dispose();
        return Task.CompletedTask;
    }

    private void LogStopped()
    {
        logger.LogInformation(
            WmsLogEvents.ApplicationStopped,
            "WareCommand host stopped in {EnvironmentName}",
            environment.EnvironmentName);
        _stopped.Dispose();
    }
}
