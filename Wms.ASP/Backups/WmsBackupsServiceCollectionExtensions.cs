using System.Text.Json;
using Wms.Application.Backups;
using Wms.Infrastructure.Backups;
using Wms.Infrastructure.Database;

namespace Wms.ASP.Backups;

public static class WmsBackupsServiceCollectionExtensions
{
    public static IServiceCollection AddWmsBackups(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        WmsDatabaseProvider databaseProvider,
        string? connectionString)
    {
        var options = WmsBackupOptionsFactory.From(configuration, environment);
        services.AddSingleton(options);
        var healthState = new WmsBackupHealthState(
            options.Enabled,
            TimeSpan.FromHours(options.MaximumAgeHours));
        RestoreHealthState(options, healthState);
        services.AddSingleton(healthState);

        if (!options.Enabled)
        {
            services.AddSingleton<IWmsBackupService, WmsDisabledBackupService>();
            return services;
        }

        if (!configuration.GetValue("Wms:Jobs:Enabled", false))
        {
            throw new InvalidOperationException(
                "Wms:Backups:Enabled requires Wms:Jobs:Enabled because scheduled backups use the durable job runner.");
        }

        if (databaseProvider != WmsDatabaseProvider.PostgreSql ||
            string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Wms:Backups:Enabled requires a PostgreSQL provider and connection string.");
        }

        services.AddSingleton<IWmsBackupService>(serviceProvider =>
            new WmsPostgreSqlBackupService(
                options,
                connectionString,
                serviceProvider.GetRequiredService<WmsBackupHealthState>(),
                serviceProvider.GetRequiredService<ILogger<WmsPostgreSqlBackupService>>()));
        return services;
    }

    private static void RestoreHealthState(
        WmsBackupOptions options,
        WmsBackupHealthState healthState)
    {
        if (!options.Enabled)
        {
            return;
        }

        var statusPath = Path.Combine(options.RootPath, "backup-status.json");
        try
        {
            if (!File.Exists(statusPath))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(statusPath));
            var root = document.RootElement;
            if (!string.Equals(
                    root.GetProperty("status").GetString(),
                    "succeeded",
                    StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("updatedAtUtc", out var updatedAtElement) ||
                !updatedAtElement.TryGetDateTimeOffset(out var updatedAtUtc) ||
                !root.TryGetProperty("artifactPath", out var artifactPathElement))
            {
                return;
            }

            var artifactPath = artifactPathElement.GetString();
            if (string.IsNullOrWhiteSpace(artifactPath))
            {
                return;
            }

            artifactPath = Path.IsPathRooted(artifactPath)
                ? artifactPath
                : Path.GetFullPath(Path.Combine(options.RootPath, artifactPath));
            if (!File.Exists(artifactPath))
            {
                return;
            }

            var sizeBytes = root.TryGetProperty("artifactSizeBytes", out var sizeElement) &&
                sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : new FileInfo(artifactPath).Length;
            healthState.MarkSuccess(
                updatedAtUtc,
                Path.GetFileName(artifactPath),
                sizeBytes);
        }
        catch (Exception)
        {
            // A malformed or inaccessible status file fails closed in readiness.
        }
    }
}
