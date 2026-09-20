namespace Wms.ASP.Health;

public sealed class WmsHealthOptions
{
    public required string StoragePath { get; init; }

    public long MinimumFreeBytes { get; init; }

    public static WmsHealthOptions From(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredPath = new[]
        {
            configuration["Wms:Health:StoragePath"],
            configuration["DataProtection:KeyDirectory"],
            environment.ContentRootPath
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))!;
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        var configuredFreeBytes = configuration.GetValue<long?>(
            "Wms:Health:MinimumFreeBytes");

        return new WmsHealthOptions
        {
            StoragePath = path,
            MinimumFreeBytes = Math.Clamp(
                configuredFreeBytes ?? 256L * 1024 * 1024,
                0,
                1L * 1024 * 1024 * 1024 * 1024)
        };
    }
}
