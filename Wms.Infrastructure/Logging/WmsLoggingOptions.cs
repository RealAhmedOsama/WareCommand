using Microsoft.Extensions.Configuration;

namespace Wms.Infrastructure.Logging;

public sealed class WmsLoggingOptions
{
    public string MinimumLevel { get; set; } = "Information";

    public bool ConsoleEnabled { get; set; } = true;

    public WmsFileLoggingOptions File { get; } = new();

    public int SlowOperationThresholdMilliseconds { get; set; } = 1000;

    public Dictionary<string, string> Overrides { get; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft"] = "Warning",
            ["Microsoft.AspNetCore"] = "Warning",
            ["Microsoft.EntityFrameworkCore"] = "Warning",
            ["System.Net.Http.HttpClient"] = "Warning"
        };

    public static WmsLoggingOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("Wms:Logging");
        var options = new WmsLoggingOptions
        {
            MinimumLevel = section["MinimumLevel"] ?? "Information",
            ConsoleEnabled = ParseBoolean(section["ConsoleEnabled"], true),
            SlowOperationThresholdMilliseconds = Math.Clamp(
                ParseInteger(section["SlowOperationThresholdMilliseconds"], 1000),
                50,
                600_000)
        };

        options.File.Enabled = ParseBoolean(section["File:Enabled"], false);
        options.File.Path = section["File:Path"] ?? options.File.Path;
        options.File.RetainedFileCountLimit = Math.Clamp(
            ParseInteger(section["File:RetainedFileCountLimit"], options.File.RetainedFileCountLimit),
            1,
            365);
        options.File.FileSizeLimitBytes = Math.Clamp(
            ParseLong(section["File:FileSizeLimitBytes"], options.File.FileSizeLimitBytes),
            1_048_576,
            1_073_741_824);

        foreach (var overrideSection in section.GetSection("Overrides").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(overrideSection.Value))
            {
                options.Overrides[overrideSection.Key] = overrideSection.Value.Trim();
            }
        }

        return options;
    }

    private static bool ParseBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ParseInteger(string? value, int fallback) =>
        int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long ParseLong(string? value, long fallback) =>
        long.TryParse(value, out var parsed) ? parsed : fallback;
}

public sealed class WmsFileLoggingOptions
{
    public bool Enabled { get; set; }

    public string Path { get; set; } = "logs/warecommand-.json";

    public int RetainedFileCountLimit { get; set; } = 14;

    public long FileSizeLimitBytes { get; set; } = 50 * 1024 * 1024;
}
