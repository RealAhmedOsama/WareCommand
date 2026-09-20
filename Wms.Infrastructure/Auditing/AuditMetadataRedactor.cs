using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace Wms.Infrastructure.Auditing;

public static class AuditMetadataRedactor
{
    private const int MaximumJsonLength = 8_000;
    private const int MaximumDepth = 3;
    private const int MaximumCollectionItems = 50;

    private static readonly string[] SensitiveKeyFragments =
    [
        "password",
        "secret",
        "token",
        "connectionstring",
        "authorization",
        "cookie",
        "apikey",
        "privatekey",
        "securitystamp",
        "email",
        "phonenumber",
        "address",
        "ssn",
        "dateofbirth",
        "birthdate",
        "employeeid"
    ];

    public static string? Serialize(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var sanitized = SanitizeDictionary(values, 0);
        var json = JsonSerializer.Serialize(sanitized);
        return json.Length <= MaximumJsonLength
            ? json
            : "{\"_redacted\":\"payload_too_large\"}";
    }

    public static string? TrimText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static Dictionary<string, object?> SanitizeDictionary(
        IEnumerable<KeyValuePair<string, object?>> values,
        int depth)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in values.Take(MaximumCollectionItems))
        {
            var key = TrimText(pair.Key, 100) ?? "field";
            result[key] = IsSensitiveKey(key)
                ? "[REDACTED]"
                : SanitizeValue(key, pair.Value, depth + 1);
        }

        return result;
    }

    private static object? SanitizeValue(string key, object? value, int depth)
    {
        if (IsSensitiveKey(key))
        {
            return "[REDACTED]";
        }

        if (value is null || depth > MaximumDepth)
        {
            return depth > MaximumDepth ? "[REDACTED_NESTED]" : null;
        }

        return value switch
        {
            string text => TrimText(text, 1_000),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or
                float or double or decimal => value,
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            Enum enumValue => enumValue.ToString(),
            IReadOnlyDictionary<string, object?> dictionary => SanitizeDictionary(dictionary, depth),
            IDictionary dictionary => SanitizeDictionary(dictionary.Cast<DictionaryEntry>()
                .Where(entry => entry.Key is not null)
                .Select(entry => new KeyValuePair<string, object?>(entry.Key!.ToString()!, entry.Value)), depth),
            IEnumerable collection => SanitizeCollection(key, collection, depth),
            _ => "[REDACTED_UNSUPPORTED]"
        };
    }

    private static List<object?> SanitizeCollection(
        string key,
        IEnumerable values,
        int depth)
    {
        var result = new List<object?>();
        foreach (var value in values.Cast<object?>().Take(MaximumCollectionItems))
        {
            result.Add(SanitizeValue(key, value, depth + 1));
        }

        return result;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new string(key
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray())
            .ToLowerInvariant();

        return SensitiveKeyFragments.Any(normalized.Contains);
    }
}
