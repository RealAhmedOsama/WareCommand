using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wms.Application.Telemetry;
using Wms.Infrastructure.Integrations;

namespace Wms.ASP.Telemetry;

public static class WmsTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddWmsTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = WmsTelemetryOptions.From(configuration, environment);
        services.AddSingleton(options);
        services.AddSingleton(_ => new WmsBackgroundJobHealthState(
            configuration.GetValue("Wms:Jobs:Enabled", false)));

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                options.ServiceName,
                serviceVersion: typeof(WmsTelemetry).Assembly
                    .GetName()
                    .Version?
                    .ToString() ?? "0.0.0"));

        if (!options.Enabled)
        {
            return services;
        }

        builder.WithTracing(tracing =>
        {
            tracing
                .SetSampler(new ParentBasedSampler(
                    new TraceIdRatioBasedSampler(options.SamplingRatio)))
                .AddSource(WmsTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation(instrumentation =>
                    instrumentation.FilterHttpRequestMessage = static request =>
                        !request.Options.TryGetValue(
                            WebhookHttpTelemetryOptions.SuppressAutomaticTracing,
                            out var suppressTracing) || !suppressTracing)
                .AddEntityFrameworkCoreInstrumentation();

            ConfigureExporters(tracing, options);
        });

        builder.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(WmsTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();

            ConfigureExporters(metrics, options);
        });

        return services;
    }

    private static void ConfigureExporters(
        TracerProviderBuilder builder,
        WmsTelemetryOptions options)
    {
        if (options.ConsoleExporterEnabled)
        {
            builder.AddConsoleExporter();
        }

        if (options.OtlpExporterEnabled)
        {
            builder.AddOtlpExporter(exporter => ConfigureExporter(exporter, options));
        }
    }

    private static void ConfigureExporters(
        MeterProviderBuilder builder,
        WmsTelemetryOptions options)
    {
        if (options.ConsoleExporterEnabled)
        {
            builder.AddConsoleExporter();
        }

        if (options.OtlpExporterEnabled)
        {
            builder.AddOtlpExporter(exporter => ConfigureExporter(exporter, options));
        }
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        WmsTelemetryOptions options)
    {
        if (options.OtlpEndpoint is not null)
        {
            exporter.Endpoint = options.OtlpEndpoint;
        }

        exporter.Protocol = options.OtlpProtocol;
    }
}
