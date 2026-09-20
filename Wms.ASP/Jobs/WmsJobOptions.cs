using System.Globalization;

namespace Wms.ASP.Jobs;

public sealed class WmsJobOptions
{
    public bool Enabled { get; init; }

    public string StorageSchema { get; init; } = "hangfire";

    public bool PrepareSchemaIfNecessary { get; init; } = true;

    public int WorkerCount { get; init; } = Math.Max(1, Environment.ProcessorCount);

    public int MaximumRetryAttempts { get; init; } = 5;

    public int[] RetryDelaysInSeconds { get; init; } = [30, 120, 600, 1_800, 3_600];

    public string DashboardPath { get; init; } = "/jobs";

    public static WmsJobOptions From(IConfiguration configuration)
    {
        var section = configuration.GetSection("Wms:Jobs");
        var retryAttempts = Math.Clamp(
            section.GetValue("MaximumRetryAttempts", 5),
            0,
            10);
        var retryDelays = ParseRetryDelays(section["RetryDelaysInSeconds"], retryAttempts);

        return new WmsJobOptions
        {
            Enabled = section.GetValue("Enabled", false),
            StorageSchema = NormalizeSchema(section["StorageSchema"]),
            PrepareSchemaIfNecessary = section.GetValue("PrepareSchemaIfNecessary", true),
            WorkerCount = Math.Clamp(
                section.GetValue("WorkerCount", Math.Max(1, Environment.ProcessorCount)),
                1,
                100),
            MaximumRetryAttempts = retryAttempts,
            RetryDelaysInSeconds = retryDelays,
            DashboardPath = NormalizeDashboardPath(section["DashboardPath"])
        };
    }

    private static int[] ParseRetryDelays(string? value, int retryAttempts)
    {
        var parsed = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(
                item,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds)
                ? seconds
                : 0)
            .Where(seconds => seconds >= 0)
            .Take(10)
            .ToArray();
        if (parsed.Length == 0)
        {
            parsed = [30, 120, 600, 1_800, 3_600];
        }

        return Enumerable.Range(0, retryAttempts)
            .Select(index => parsed[Math.Min(index, parsed.Length - 1)])
            .ToArray();
    }

    private static string NormalizeSchema(string? value)
    {
        var normalized = value?.Trim() ?? "hangfire";
        return normalized.Length is 0 or > 63 ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character != '_')
            ? "hangfire"
            : normalized;
    }

    private static string NormalizeDashboardPath(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "/jobs" : value.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.TrimEnd('/') is { Length: > 0 } path ? path : "/jobs";
    }
}
