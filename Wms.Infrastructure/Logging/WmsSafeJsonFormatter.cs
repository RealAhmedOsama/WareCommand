using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Wms.Infrastructure.Logging;

/// <summary>
/// Emits structured JSON while keeping exception text, sensitive properties, and large payloads bounded.
/// </summary>
public sealed class WmsSafeJsonFormatter : ITextFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        var safeProperties = logEvent.Properties.ToDictionary(
            property => property.Key,
            property => ConvertValue(property.Key, property.Value),
            StringComparer.Ordinal);
        var safeEvent = new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            exception: null,
            logEvent.MessageTemplate,
            safeProperties.Select(property => new LogEventProperty(
                property.Key,
                ConvertToLogEventValue(property.Value))));

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["timestamp"] = logEvent.Timestamp,
            ["level"] = logEvent.Level.ToString(),
            ["message"] = WmsLogRedactor.RedactText(
                safeEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)),
            ["messageTemplate"] = WmsLogRedactor.RedactText(logEvent.MessageTemplate.Text),
            ["properties"] = safeProperties
        };

        if (logEvent.Exception is not null)
        {
            payload["exception"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = logEvent.Exception.GetType().FullName,
                ["message"] = WmsLogRedactor.RedactText(logEvent.Exception.Message)
            };
        }

        output.Write(JsonSerializer.Serialize(payload, SerializerOptions));
        output.WriteLine();
    }

    private static object? ConvertValue(
        string propertyName,
        LogEventPropertyValue value,
        int depth = 0)
    {
        if (depth > 5)
        {
            return WmsLogRedactor.TruncatedValue;
        }

        if (WmsLogRedactor.IsSensitiveProperty(propertyName))
        {
            return WmsLogRedactor.RedactedValue;
        }

        return value switch
        {
            ScalarValue scalar => WmsLogRedactor.SanitizeScalar(propertyName, scalar.Value),
            SequenceValue sequence => sequence.Elements
                .Take(50)
                .Select(element => ConvertValue(propertyName, element, depth + 1))
                .ToArray(),
            StructureValue structure => structure.Properties
                .Take(50)
                .ToDictionary(
                    property => property.Name,
                    property => ConvertValue(property.Name, property.Value, depth + 1),
                    StringComparer.Ordinal),
            DictionaryValue dictionary => dictionary.Elements
                .Take(50)
                .ToDictionary(
                    element => Convert.ToString(element.Key.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    element => ConvertValue(propertyName, element.Value, depth + 1),
                    StringComparer.Ordinal),
            _ => WmsLogRedactor.TruncatedValue
        };
    }

    private static ScalarValue ConvertToLogEventValue(object? value) => value switch
    {
        null => new ScalarValue(null),
        string text => new ScalarValue(text),
        bool boolean => new ScalarValue(boolean),
        int integer => new ScalarValue(integer),
        long longValue => new ScalarValue(longValue),
        decimal decimalValue => new ScalarValue(decimalValue),
        double doubleValue => new ScalarValue(doubleValue),
        float floatValue => new ScalarValue(floatValue),
        _ => new ScalarValue(value.ToString())
    };
}
