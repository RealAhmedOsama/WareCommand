using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.ASP.Telemetry;
using Wms.Infrastructure.Database;

namespace Wms.ASP.Health;

public sealed class WmsCriticalConfigurationHealthCheck(
    IConfiguration configuration,
    IHostEnvironment environment,
    WmsDatabaseOptions databaseOptions,
    WmsTelemetryOptions telemetryOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var invalid = new List<string>();
        if (databaseOptions.Provider == WmsDatabaseProvider.PostgreSql &&
            !databaseOptions.ConnectionStringConfigured)
        {
            invalid.Add("database");
        }

        if (environment.IsProduction() &&
            string.IsNullOrWhiteSpace(configuration["DataProtection:KeyDirectory"]))
        {
            invalid.Add("data_protection");
        }

        if (environment.IsProduction() &&
            configuration.GetValue("Authentication:Bootstrap:Enabled", false))
        {
            invalid.Add("bootstrap_authentication");
        }

        if (environment.IsProduction() &&
            string.Equals(
                configuration["Wms:SeedProfile"],
                "Demo",
                StringComparison.OrdinalIgnoreCase))
        {
            invalid.Add("seed_profile");
        }

        if (telemetryOptions.OtlpExporterEnabled &&
            telemetryOptions.OtlpEndpoint is not null &&
            telemetryOptions.OtlpEndpoint.Scheme is not ("http" or "https"))
        {
            invalid.Add("telemetry_exporter");
        }

        return Task.FromResult(invalid.Count == 0
            ? HealthCheckResult.Healthy("Critical application configuration is valid.")
            : HealthCheckResult.Unhealthy("Critical application configuration is invalid."));
    }
}
