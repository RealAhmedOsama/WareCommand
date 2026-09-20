using System.Globalization;
using Wms.Domain.Enums;

namespace Wms.Domain.Identification;

public sealed record BarcodeParseResult(
    string OriginalValue,
    string NormalizedValue,
    BarcodeSymbology Symbology,
    bool IsCheckDigitValidated);

/// <summary>
/// Normalizes scanner input without guessing that a numeric value is a retail
/// product code. Product-code validation is explicit because internal warehouse
/// labels commonly use short numeric values too.
/// </summary>
public static class BarcodeParser
{
    public const int MaximumLength = 200;

    public static BarcodeParseResult Parse(
        string value,
        bool strictProductCode = false)
    {
        var original = NormalizeInput(value, allowGroupSeparator: false);
        var normalized = StripScannerSymbologyPrefix(original);
        if (strictProductCode)
        {
            return ParseProductCode(original);
        }

        var symbology = normalized.All(IsAsciiDigit)
            ? BarcodeSymbology.Internal
            : BarcodeSymbology.Code128;

        return new BarcodeParseResult(
            original,
            normalized.ToUpperInvariant(),
            symbology,
            IsCheckDigitValidated: false);
    }

    public static BarcodeParseResult ParseProductCode(string value)
    {
        var original = NormalizeInput(value, allowGroupSeparator: false);
        var normalized = StripScannerSymbologyPrefix(original);
        if (!normalized.All(IsAsciiDigit) || normalized.Length is not (8 or 12 or 13 or 14))
        {
            throw new ArgumentException(
                "A product code must contain exactly 8, 12, 13, or 14 digits.",
                nameof(value));
        }

        if (!HasValidCheckDigit(normalized))
        {
            throw new ArgumentException(
                "The product code check digit is invalid.",
                nameof(value));
        }

        return new BarcodeParseResult(
            original,
            normalized,
            GetProductCodeSymbology(normalized.Length),
            IsCheckDigitValidated: true);
    }

    public static string NormalizeProductCode(string value) =>
        ParseProductCode(value).NormalizedValue;

    public static BarcodeParseResult ParseSscc(string value)
    {
        var original = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
        var normalized = StripScannerSymbologyPrefix(original);
        if (normalized.Length != 18 || !normalized.All(IsAsciiDigit))
        {
            throw new ArgumentException(
                "An SSCC must contain exactly 18 digits.",
                nameof(value));
        }

        if (!HasValidCheckDigit(normalized))
        {
            throw new ArgumentException(
                "The SSCC check digit is invalid.",
                nameof(value));
        }

        return new BarcodeParseResult(
            original,
            normalized,
            BarcodeSymbology.Sscc,
            IsCheckDigitValidated: true);
    }

    public static bool HasValidCheckDigit(string digits)
    {
        if (string.IsNullOrEmpty(digits) ||
            digits.Length is not (8 or 12 or 13 or 14 or 18) ||
            !digits.All(IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        var positionFromRight = 1;
        for (var index = digits.Length - 2; index >= 0; index--, positionFromRight++)
        {
            var digit = digits[index] - '0';
            sum += digit * (positionFromRight % 2 == 1 ? 3 : 1);
        }

        var expected = (10 - (sum % 10)) % 10;
        return expected == digits[^1] - '0';
    }

    public static string StripScannerSymbologyPrefix(string value)
    {
        if (value.StartsWith("]C1", StringComparison.OrdinalIgnoreCase))
        {
            return value[3..];
        }

        return value;
    }

    private static string NormalizeInput(string value, bool allowGroupSeparator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A barcode value is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A barcode value cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        if (normalized.Any(character =>
                char.IsControl(character) &&
                (!allowGroupSeparator || character != '\u001D')))
        {
            throw new ArgumentException(
                "A barcode value contains unsupported control characters.",
                nameof(value));
        }

        return normalized;
    }

    private static BarcodeSymbology GetProductCodeSymbology(int length) => length switch
    {
        8 => BarcodeSymbology.Ean8,
        12 => BarcodeSymbology.UpcA,
        13 => BarcodeSymbology.Ean13,
        14 => BarcodeSymbology.Gtin14,
        _ => BarcodeSymbology.Unknown
    };

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}

public enum Gs1ValueKind
{
    Text = 0,
    ProductCode = 1,
    Date = 2,
    Quantity = 3,
    Sscc = 4
}

public sealed record Gs1ApplicationIdentifierDefinition(
    string Code,
    int? FixedLength,
    int MaximumLength,
    Gs1ValueKind ValueKind,
    bool IsVariable);

public sealed record Gs1Payload(
    string OriginalPayload,
    IReadOnlyDictionary<string, string> ApplicationIdentifiers,
    string? Gtin,
    string? Lot,
    string? Serial,
    DateOnly? ExpiryDate,
    decimal? Quantity,
    string? Sscc);

/// <summary>
/// Deterministic GS1 parser for the AIs used by warehouse receiving and
/// traceability. Parenthesized human-readable payloads and FNC1/group
/// separator payloads are both accepted.
/// </summary>
public static class Gs1Parser
{
    public const char GroupSeparator = '\u001D';

    public static IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition> DefaultDefinitions { get; } =
        new Dictionary<string, Gs1ApplicationIdentifierDefinition>(StringComparer.Ordinal)
        {
            ["00"] = new("00", 18, 18, Gs1ValueKind.Sscc, IsVariable: false),
            ["01"] = new("01", 14, 14, Gs1ValueKind.ProductCode, IsVariable: false),
            ["10"] = new("10", null, 20, Gs1ValueKind.Text, IsVariable: true),
            ["17"] = new("17", 6, 6, Gs1ValueKind.Date, IsVariable: false),
            ["21"] = new("21", null, 20, Gs1ValueKind.Text, IsVariable: true),
            ["30"] = new("30", null, 8, Gs1ValueKind.Quantity, IsVariable: true),
            ["37"] = new("37", null, 8, Gs1ValueKind.Quantity, IsVariable: true)
        };

    public static bool LooksLikeGs1(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("]C1", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith('(') ||
               trimmed.Contains(GroupSeparator);
    }

    public static Gs1Payload Parse(
        string value,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition>? definitions = null)
    {
        var original = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
        if (original.Length == 0 || original.Length > BarcodeParser.MaximumLength)
        {
            throw new ArgumentException(
                $"A GS1 payload must contain between 1 and {BarcodeParser.MaximumLength} characters.",
                nameof(value));
        }

        var configuredDefinitions = definitions ?? DefaultDefinitions;
        var payload = BarcodeParser.StripScannerSymbologyPrefix(original);
        var values = payload.StartsWith('(')
            ? ParseParenthesized(payload, configuredDefinitions)
            : ParseRaw(payload, configuredDefinitions);

        return CreatePayload(original, values, configuredDefinitions);
    }

    public static bool TryParse(
        string value,
        out Gs1Payload? payload,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition>? definitions = null)
    {
        try
        {
            payload = Parse(value, definitions);
            return true;
        }
        catch (ArgumentException)
        {
            payload = null;
            return false;
        }
    }

    private static Dictionary<string, string> ParseParenthesized(
        string value,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition> definitions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] != '(' || index + 3 >= value.Length || value[index + 3] != ')')
            {
                throw new ArgumentException(
                    "A parenthesized GS1 payload must use two-digit application identifiers.",
                    nameof(value));
            }

            var code = value.Substring(index + 1, 2);
            var definition = GetDefinition(code, definitions);
            index += 4;
            var valueStart = index;
            var nextIdentifier = value.IndexOf('(', valueStart);
            var valueEnd = nextIdentifier >= 0 ? nextIdentifier : value.Length;
            if (!definition.IsVariable && valueEnd - valueStart != definition.FixedLength)
            {
                throw new ArgumentException(
                    $"GS1 AI {code} must contain exactly {definition.FixedLength} characters.",
                    nameof(value));
            }

            var field = value[valueStart..valueEnd];
            AddField(values, code, field, definition);
            index = valueEnd;
        }

        return values;
    }

    private static Dictionary<string, string> ParseRaw(
        string value,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition> definitions)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = 0;
        while (index < value.Length)
        {
            if (value[index] == GroupSeparator)
            {
                index++;
                continue;
            }

            if (index + 2 > value.Length)
            {
                throw new ArgumentException("GS1 payload ended before an application identifier.", nameof(value));
            }

            var code = value.Substring(index, 2);
            var definition = GetDefinition(code, definitions);
            index += 2;
            var fieldStart = index;
            if (definition.IsVariable)
            {
                var separator = value.IndexOf(GroupSeparator, fieldStart);
                var fieldEnd = separator >= 0 ? separator : value.Length;
                if (fieldEnd - fieldStart > definition.MaximumLength)
                {
                    throw new ArgumentException(
                        $"GS1 AI {code} exceeds its maximum length of {definition.MaximumLength}.",
                        nameof(value));
                }

                AddField(values, code, value[fieldStart..fieldEnd], definition);
                index = separator >= 0 ? separator + 1 : fieldEnd;
                continue;
            }

            var fixedLength = definition.FixedLength ?? definition.MaximumLength;
            if (value.Length - fieldStart < fixedLength)
            {
                throw new ArgumentException(
                    $"GS1 AI {code} must contain exactly {fixedLength} characters.",
                    nameof(value));
            }

            AddField(values, code, value.Substring(fieldStart, fixedLength), definition);
            index = fieldStart + fixedLength;
        }

        return values;
    }

    private static Gs1Payload CreatePayload(
        string original,
        Dictionary<string, string> values,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition> definitions)
    {
        DateOnly? expiryDate = null;
        if (values.TryGetValue("17", out var expiry))
        {
            var year = int.Parse(expiry[..2], CultureInfo.InvariantCulture);
            var month = int.Parse(expiry.AsSpan(2, 2), CultureInfo.InvariantCulture);
            var day = int.Parse(expiry.AsSpan(4, 2), CultureInfo.InvariantCulture);
            year += year >= 50 ? 1900 : 2000;
            try
            {
                expiryDate = new DateOnly(year, month, day);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new ArgumentException("GS1 AI 17 contains an invalid expiry date.", nameof(original), exception);
            }
        }

        decimal? quantity = null;
        var quantityValue = values.TryGetValue("30", out var count30)
            ? count30
            : values.TryGetValue("37", out var count37) ? count37 : null;
        if (quantityValue is not null &&
            (!decimal.TryParse(quantityValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ||
             parsed < 0))
        {
            throw new ArgumentException("GS1 quantity must be a non-negative invariant decimal.", nameof(original));
        }

        if (quantityValue is not null)
        {
            quantity = decimal.Parse(quantityValue, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        return new Gs1Payload(
            original,
            new Dictionary<string, string>(values, StringComparer.Ordinal),
            values.GetValueOrDefault("01"),
            values.GetValueOrDefault("10"),
            values.GetValueOrDefault("21"),
            expiryDate,
            quantity,
            values.GetValueOrDefault("00"));
    }

    private static Gs1ApplicationIdentifierDefinition GetDefinition(
        string code,
        IReadOnlyDictionary<string, Gs1ApplicationIdentifierDefinition> definitions)
    {
        if (!definitions.TryGetValue(code, out var definition))
        {
            throw new ArgumentException(
                $"GS1 application identifier '{code}' is not configured.",
                nameof(code));
        }

        return definition;
    }

    private static void AddField(
        Dictionary<string, string> values,
        string code,
        string field,
        Gs1ApplicationIdentifierDefinition definition)
    {
        if (field.Length == 0 || field.Length > definition.MaximumLength || field.Contains(GroupSeparator))
        {
            throw new ArgumentException(
                $"GS1 AI {code} contains an empty or invalid value.",
                nameof(field));
        }

        if (values.ContainsKey(code))
        {
            throw new ArgumentException($"GS1 AI {code} appears more than once.", nameof(code));
        }

        switch (definition.ValueKind)
        {
            case Gs1ValueKind.ProductCode when field.Length != 14 || !BarcodeParser.HasValidCheckDigit(field):
                throw new ArgumentException("GS1 AI 01 contains an invalid GTIN-14 check digit.", nameof(field));
            case Gs1ValueKind.Sscc when field.Length != 18 || !BarcodeParser.HasValidCheckDigit(field):
                throw new ArgumentException("GS1 AI 00 contains an invalid SSCC check digit.", nameof(field));
            case Gs1ValueKind.Date when field.Length != 6 || !field.All(char.IsAsciiDigit):
                throw new ArgumentException("GS1 AI 17 must contain a YYMMDD date.", nameof(field));
            case Gs1ValueKind.Quantity when !field.All(character => char.IsAsciiDigit(character) || character == '.'):
                throw new ArgumentException($"GS1 AI {code} contains an invalid quantity.", nameof(field));
        }

        values.Add(code, field);
    }
}
