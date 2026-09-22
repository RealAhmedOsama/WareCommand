using Wms.Application.Common;

namespace Wms.Application.Support;

public sealed record SupportBundleRequest(
    string Reason,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MaximumBytes = 1_000_000,
    TimeSpan? ExpiresAfter = null,
    IReadOnlyCollection<string>? ReferenceIds = null);

public sealed record SupportBundleSection(
    string Name,
    int ItemCount,
    IReadOnlyDictionary<string, string?> Values);

public sealed record SupportBundleManifest(
    string BundleVersion,
    string ApplicationVersion,
    string SchemaVersion,
    string Environment,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<SupportBundleSection> Sections,
    IReadOnlyList<string> RedactedFields);

public interface ISupportBundleService
{
    Task<Result<SupportBundleManifest>> CreateAsync(
        SupportBundleRequest request,
        CancellationToken cancellationToken = default);
}

public static class SupportBundlePolicy
{
    public const int MaximumReasonLength = 500;
    public const int MaximumBundleBytes = 5_000_000;
    public static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(4);
    public static readonly TimeSpan MaximumExpiry = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    private static readonly HashSet<string> SensitiveKeys = new(
        [
            "password",
            "secret",
            "token",
            "authorization",
            "cookie",
            "connectionstring",
            "connection-string",
            "dataprotectionkey",
            "payload",
            "body",
            "raw"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static Result Validate(SupportBundleRequest request, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > MaximumReasonLength)
        {
            return Result.Failure(WmsErrors.Validation(
                "support.reason_invalid",
                "A bounded support reason is required."));
        }

        if (request.FromUtc > request.ToUtc ||
            request.ToUtc - request.FromUtc > MaximumWindow)
        {
            return Result.Failure(WmsErrors.Validation(
                "support.window_invalid",
                "The support bundle window must be ordered and no longer than thirty-one days."));
        }

        if (request.MaximumBytes is < 1_024 or > MaximumBundleBytes)
        {
            return Result.Failure(WmsErrors.Validation(
                "support.size_invalid",
                $"The support bundle size must be between 1024 and {MaximumBundleBytes} bytes."));
        }

        var expiry = request.ExpiresAfter ?? DefaultExpiry;
        if (expiry <= TimeSpan.Zero || expiry > MaximumExpiry)
        {
            return Result.Failure(WmsErrors.Validation(
                "support.expiry_invalid",
                "The support bundle expiry must be positive and no longer than one day."));
        }

        if (request.ToUtc > nowUtc.AddMinutes(5))
        {
            return Result.Failure(WmsErrors.Validation(
                "support.future_window_invalid",
                "A support bundle cannot include an unbounded future window."));
        }

        return Result.Success();
    }

    public static IReadOnlyDictionary<string, string?> Redact(
        IReadOnlyDictionary<string, string?> values,
        ICollection<string>? redactedFields = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var safe = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (SensitiveKeys.Contains(pair.Key) ||
                SensitiveKeys.Any(key => pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase)))
            {
                safe[pair.Key] = "[redacted]";
                redactedFields?.Add(pair.Key);
                continue;
            }

            safe[pair.Key] = Limit(pair.Value, 2_000);
        }

        return safe;
    }

    private static string? Limit(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}
