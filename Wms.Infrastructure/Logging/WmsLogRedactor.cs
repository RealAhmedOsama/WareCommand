using System.Text.RegularExpressions;

namespace Wms.Infrastructure.Logging;

public static partial class WmsLogRedactor
{
    public const string RedactedValue = "[REDACTED]";
    public const string TruncatedValue = "[TRUNCATED]";

    public static bool IsSensitiveProperty(string propertyName)
    {
        var normalized = propertyName
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("connectionstring", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("cookie", StringComparison.Ordinal) ||
               normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("barcode", StringComparison.Ordinal) ||
               normalized.Contains("requestbody", StringComparison.Ordinal) ||
               normalized.Contains("responsebody", StringComparison.Ordinal) ||
               normalized.Equals("payload", StringComparison.Ordinal) ||
               normalized.Contains("personaldata", StringComparison.Ordinal) ||
               normalized.Equals("email", StringComparison.Ordinal) ||
               normalized.Equals("phone", StringComparison.Ordinal) ||
               normalized.Equals("displayname", StringComparison.Ordinal) ||
               normalized.Equals("username", StringComparison.Ordinal);
    }

    public static object? SanitizeScalar(string propertyName, object? value)
    {
        if (IsSensitiveProperty(propertyName))
        {
            return RedactedValue;
        }

        if (value is string text)
        {
            return RedactText(text);
        }

        return value;
    }

    public static string RedactText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var redacted = SecretAssignmentRegex().Replace(value, "$1=[REDACTED]");
        redacted = ConnectionStringAssignmentRegex().Replace(redacted, "$1=[REDACTED]");
        return redacted.Length > 4096 ? redacted[..4096] + TruncatedValue : redacted;
    }

    [GeneratedRegex(
        "(?i)(password|token|secret|connectionstring|authorization|cookie|api[-_]?key)\\s*[:=]\\s*[^;\\s,]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(
        "(?i)(host|server|database|user[ ]?id|username|password|port)\\s*=\\s*[^;]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringAssignmentRegex();
}
