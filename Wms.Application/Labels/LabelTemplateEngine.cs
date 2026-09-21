using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Wms.Application.Common;
using Wms.Application.Devices;
using Wms.Application.Localization;
using Wms.Domain.Enums;
using Wms.Domain.Identification;

namespace Wms.Application.Labels;

public static class WmsLabelTemplateValidator
{
    private static readonly string[] ForbiddenFieldFragments =
    [
        "password",
        "secret",
        "token",
        "credential",
        "connectionstring",
        "apikey",
        "privatekey"
    ];

    private static readonly Regex KeyPattern = new(
        "^[A-Za-z][A-Za-z0-9_.-]{0,79}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlaceholderPattern = new(
        "\\{\\{([A-Za-z][A-Za-z0-9_.-]{0,79})\\}\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string[]> Validate(
        WmsLabelTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        RequireText(errors, nameof(definition.Name), definition.Name, 1, WmsLabelLimits.MaximumTemplateNameLength);
        if (definition.Version < 1)
        {
            Add(errors, nameof(definition.Version), "Template version must be positive.");
        }

        if (!WmsLocaleCatalog.IsSupported(definition.Language))
        {
            Add(errors, nameof(definition.Language), "Template language must be en-US or ar-SA.");
        }

        if (definition.WidthMillimeters is < 10m or > 1_000m)
        {
            Add(errors, nameof(definition.WidthMillimeters), "Label width must be between 10 and 1,000 millimetres.");
        }

        if (definition.HeightMillimeters is < 10m or > 1_000m)
        {
            Add(errors, nameof(definition.HeightMillimeters), "Label height must be between 10 and 1,000 millimetres.");
        }

        if (definition.Body.Length > WmsLabelLimits.MaximumTemplateBodyLength)
        {
            Add(errors, nameof(definition.Body), "Template body exceeds the configured size limit.");
        }

        var fields = definition.Fields ?? [];
        if (fields.Count > WmsLabelLimits.MaximumFields)
        {
            Add(errors, nameof(definition.Fields), "A template cannot define more than 100 fields.");
        }

        var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!KeyPattern.IsMatch(field.Key ?? string.Empty))
            {
                Add(errors, $"Fields.{field.Key}", "Field keys must start with a letter and contain only letters, digits, '.', '-', or '_'.");
            }

            if (ForbiddenFieldFragments.Any(fragment =>
                    (field.Key ?? string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                Add(errors, $"Fields.{field.Key}", "Secret, credential, token, and connection-string fields are not allowed in label templates.");
            }

            if (!fieldKeys.Add(field.Key ?? string.Empty))
            {
                Add(errors, $"Fields.{field.Key}", "Field keys must be unique.");
            }

            if (field.MaximumLength is < 1 or > 2_000)
            {
                Add(errors, $"Fields.{field.Key}.MaximumLength", "Field maximum length must be between 1 and 2,000.");
            }
        }

        var lines = definition.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length > WmsLabelLimits.MaximumTemplateLines)
        {
            Add(errors, nameof(definition.Body), "A template cannot contain more than 100 text lines.");
        }

        foreach (var line in lines)
        {
            if (line.Length > WmsLabelLimits.MaximumLineLength)
            {
                Add(errors, nameof(definition.Body), "A template line exceeds the 250-character limit.");
            }

            if (line.Any(char.IsControl) || line.Contains('^') || line.Contains('~'))
            {
                Add(errors, nameof(definition.Body), "Template text cannot contain control characters or printer command prefixes.");
            }
        }

        if (definition.Body.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
            definition.Body.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            definition.Body.Contains("${", StringComparison.Ordinal))
        {
            Add(errors, nameof(definition.Body), "Executable template content is not allowed.");
        }

        foreach (Match match in PlaceholderPattern.Matches(definition.Body))
        {
            if (!fieldKeys.Contains(match.Groups[1].Value))
            {
                Add(errors, nameof(definition.Body), $"Placeholder '{match.Value}' is not declared as a field.");
            }
        }

        var barcodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (definition.Barcodes.Count > WmsLabelLimits.MaximumBarcodes)
        {
            Add(errors, nameof(definition.Barcodes), "A template cannot define more than 20 barcodes.");
        }

        foreach (var barcode in definition.Barcodes)
        {
            if (!KeyPattern.IsMatch(barcode.Name ?? string.Empty) || !barcodeNames.Add(barcode.Name ?? string.Empty))
            {
                Add(errors, $"Barcodes.{barcode.Name}", "Barcode names must be unique and use a safe field-key format.");
            }

            if (!fieldKeys.Contains(barcode.SourceKey))
            {
                Add(errors, $"Barcodes.{barcode.Name}.SourceKey", "Barcode source must reference a declared field.");
            }

            if (barcode.HeightDots is < 20 or > 1_000)
            {
                Add(errors, $"Barcodes.{barcode.Name}.HeightDots", "Barcode height must be between 20 and 1,000 dots.");
            }
        }

        foreach (var route in definition.Routes)
        {
            if (string.IsNullOrWhiteSpace(route.Name) || route.Name.Length > 100)
            {
                Add(errors, nameof(definition.Routes), "Print route names are required and must be at most 100 characters.");
            }

            if (!string.Equals(route.TemplateName, definition.Name, StringComparison.OrdinalIgnoreCase))
            {
                Add(errors, nameof(definition.Routes), "A route must target the containing template name.");
            }

            if (route.Transport == WmsPrintTransport.BrowserPdf && route.Format != WmsPrintFormat.Pdf)
            {
                Add(errors, nameof(definition.Routes), "Browser PDF routes must use PDF format.");
            }

        }

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static void RequireText(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int minimum,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < minimum || value.Trim().Length > maximum)
        {
            Add(errors, key, $"The value is required and must be between {minimum} and {maximum} characters.");
        }
    }

    private static void Add(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}

public static class WmsLabelTemplateEngine
{
    private static readonly Regex PlaceholderPattern = new(
        "\\{\\{([A-Za-z][A-Za-z0-9_.-]{0,79})\\}\\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Result<WmsRenderedLabel> Render(
        WmsLabelTemplateDefinition definition,
        IReadOnlyDictionary<string, string?> data)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(data);

        var validation = WmsLabelTemplateValidator.Validate(definition);
        if (validation.Count > 0)
        {
            return Result.Failure<WmsRenderedLabel>(WmsErrors.Validation(
                "label.template_invalid",
                "The label template is invalid.",
                validation));
        }

        var values = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.Fields)
        {
            values.TryGetValue(field.Key, out var rawValue);
            if (string.IsNullOrWhiteSpace(rawValue) && field.Required)
            {
                return Result.Failure<WmsRenderedLabel>(WmsErrors.Validation(
                    "label.data_required",
                    $"Label data is missing required field '{field.Key}'."));
            }

            if (rawValue is null)
            {
                fields[field.Key] = null;
                continue;
            }

            if (rawValue.Length > field.MaximumLength || rawValue.Any(char.IsControl))
            {
                return Result.Failure<WmsRenderedLabel>(WmsErrors.Validation(
                    "label.data_invalid",
                    $"Label field '{field.Key}' contains invalid or oversized data."));
            }

            var normalized = NormalizeField(field, rawValue);
            if (normalized.IsFailure)
            {
                return normalized.ToFailure<WmsRenderedLabel>();
            }

            fields[field.Key] = normalized.Value;
        }

        var bodyLines = definition.Body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => PlaceholderPattern.Replace(
                line,
                match => fields.TryGetValue(match.Groups[1].Value, out var value)
                    ? value ?? string.Empty
                    : string.Empty))
            .ToArray();

        var barcodeValues = new List<(WmsLabelBarcodeDefinition Definition, string Value)>();
        foreach (var barcode in definition.Barcodes)
        {
            var value = fields.GetValueOrDefault(barcode.SourceKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                return Result.Failure<WmsRenderedLabel>(WmsErrors.Validation(
                    "label.barcode_data_required",
                    $"Barcode '{barcode.Name}' has no source value."));
            }

            var barcodeValidation = ValidateBarcode(barcode, value);
            if (barcodeValidation.IsFailure)
            {
                return barcodeValidation.ToFailure<WmsRenderedLabel>();
            }

            barcodeValues.Add((barcode, value));
        }

        var textPreview = string.Join(Environment.NewLine, bodyLines.Concat(
            barcodeValues.Select(value => $"[{value.Definition.Name}] {value.Value}")));
        var culture = WmsLocaleCatalog.TryGetCulture(definition.Language, out var supportedCulture)
            ? supportedCulture
            : CultureInfo.InvariantCulture;
        var browserHtml = RenderBrowserHtml(definition, bodyLines, barcodeValues, culture);
        return definition.Format == WmsPrintFormat.Zpl
            ? Result.Success(RenderZpl(definition, bodyLines, barcodeValues, textPreview, browserHtml))
            : Result.Success(RenderPdf(definition, bodyLines, barcodeValues, textPreview, browserHtml, culture));
    }

    private static Result<string> NormalizeField(WmsLabelFieldDefinition field, string value)
    {
        try
        {
            return field.Kind switch
            {
                WmsLabelFieldKind.Number when !decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out _) => Result.Failure<string>(WmsErrors.Validation(
                    "label.number_invalid",
                    $"Label field '{field.Key}' must be an invariant decimal.")),
                WmsLabelFieldKind.Date when !DateOnly.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _) => Result.Failure<string>(WmsErrors.Validation(
                    "label.date_invalid",
                    $"Label field '{field.Key}' must be an ISO date.")),
                WmsLabelFieldKind.ProductCode => NormalizeProductCode(field, value),
                WmsLabelFieldKind.Sscc => NormalizeSscc(field, value),
                WmsLabelFieldKind.Gs1 => NormalizeGs1(field, value),
                _ => Result.Success(value.Trim())
            };
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<string>(WmsErrors.Validation(
                "label.field_invalid",
                $"Label field '{field.Key}' is invalid: {exception.Message}"));
        }
    }

    private static Result<string> NormalizeProductCode(WmsLabelFieldDefinition field, string value) =>
        TryNormalize(field, () => BarcodeParser.ParseProductCode(value).NormalizedValue);

    private static Result<string> NormalizeSscc(WmsLabelFieldDefinition field, string value) =>
        TryNormalize(field, () => BarcodeParser.ParseSscc(value).NormalizedValue);

    private static Result<string> NormalizeGs1(WmsLabelFieldDefinition field, string value) =>
        TryNormalize(field, () => Gs1Parser.Parse(value).OriginalPayload);

    private static Result<string> TryNormalize(
        WmsLabelFieldDefinition field,
        Func<string> parser)
    {
        try
        {
            return Result.Success(parser());
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<string>(WmsErrors.Validation(
                "label.field_invalid",
                $"Label field '{field.Key}' is invalid: {exception.Message}"));
        }
    }

    private static Result ValidateBarcode(
        WmsLabelBarcodeDefinition definition,
        string value)
    {
        try
        {
            switch (definition.Symbology)
            {
                case BarcodeSymbology.Ean8:
                case BarcodeSymbology.UpcA:
                case BarcodeSymbology.Ean13:
                case BarcodeSymbology.Gtin14:
                    BarcodeParser.ParseProductCode(value);
                    break;
                case BarcodeSymbology.Sscc:
                    BarcodeParser.ParseSscc(value);
                    break;
                case BarcodeSymbology.Gs1:
                    Gs1Parser.Parse(value);
                    break;
                case BarcodeSymbology.Unknown:
                case BarcodeSymbology.Internal:
                case BarcodeSymbology.Code128:
                    if (value.Length == 0 || value.Any(char.IsControl))
                    {
                        return Result.Failure(WmsErrors.Validation(
                            "label.barcode_invalid",
                            $"Barcode '{definition.Name}' contains invalid data."));
                    }

                    break;
            }

            return Result.Success();
        }
        catch (ArgumentException exception)
        {
            return Result.Failure(WmsErrors.Validation(
                "label.barcode_invalid",
                $"Barcode '{definition.Name}' is invalid: {exception.Message}"));
        }
    }

    private static WmsRenderedLabel RenderZpl(
        WmsLabelTemplateDefinition definition,
        IReadOnlyList<string> lines,
        IReadOnlyList<(WmsLabelBarcodeDefinition Definition, string Value)> barcodes,
        string textPreview,
        string browserHtml)
    {
        var widthDots = Math.Clamp((int)Math.Round(definition.WidthMillimeters * 8m), 80, 8_000);
        var heightDots = Math.Clamp((int)Math.Round(definition.HeightMillimeters * 8m), 80, 8_000);
        var zpl = new StringBuilder()
            .Append("^XA\n^CI28\n")
            .Append("^PW").Append(widthDots).Append('\n')
            .Append("^LL").Append(heightDots).Append('\n');
        var y = 20;
        foreach (var line in lines)
        {
            zpl.Append("^FO20,").Append(y).Append("^A0N,30,30^FD")
                .Append(EscapeZpl(line)).Append("^FS\n");
            y += 38;
        }

        foreach (var barcode in barcodes)
        {
            zpl.Append("^FO20,").Append(y).Append("^BY2^BCN,")
                .Append(Math.Clamp(barcode.Definition.HeightDots, 20, heightDots / 2))
                .Append(",Y,N,N^FD").Append(EscapeZpl(barcode.Value)).Append("^FS\n");
            y += Math.Clamp(barcode.Definition.HeightDots, 20, heightDots / 2) + 20;
        }

        zpl.Append("^XZ\n");
        return new WmsRenderedLabel(
            Encoding.UTF8.GetBytes(zpl.ToString()),
            "application/zpl; charset=utf-8",
            textPreview,
            browserHtml,
            false,
            definition.Name,
            definition.Version,
            definition.Format);
    }

    private static WmsRenderedLabel RenderPdf(
        WmsLabelTemplateDefinition definition,
        IReadOnlyList<string> lines,
        IReadOnlyList<(WmsLabelBarcodeDefinition Definition, string Value)> barcodes,
        string textPreview,
        string browserHtml,
        CultureInfo culture)
    {
        var pdfLines = lines.Concat(barcodes.Select(value =>
            $"[{value.Definition.Name}] {value.Value}"));
        var payload = SimplePdfWriter.Create(
            pdfLines,
            definition.WidthMillimeters,
            definition.HeightMillimeters);
        return new WmsRenderedLabel(
            payload,
            "application/pdf",
            textPreview,
            browserHtml,
            culture.TextInfo.IsRightToLeft,
            definition.Name,
            definition.Version,
            definition.Format);
    }

    private static string RenderBrowserHtml(
        WmsLabelTemplateDefinition definition,
        IReadOnlyList<string> lines,
        IReadOnlyList<(WmsLabelBarcodeDefinition Definition, string Value)> barcodes,
        CultureInfo culture)
    {
        var direction = culture.TextInfo.IsRightToLeft ? "rtl" : "ltr";
        var body = new StringBuilder()
            .Append("<!doctype html><html lang=\"")
            .Append(WebUtility.HtmlEncode(culture.Name))
            .Append("\" dir=\"").Append(direction).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<style>@page{size:")
            .Append(definition.WidthMillimeters.ToString(CultureInfo.InvariantCulture)).Append("mm ")
            .Append(definition.HeightMillimeters.ToString(CultureInfo.InvariantCulture)).Append("mm;margin:0}")
            .Append("body{margin:0;padding:4mm;font-family:Arial,sans-serif;direction:")
            .Append(direction).Append("}.line{margin:.5mm 0}.barcode{font-family:monospace;letter-spacing:.12em;border:1px solid #111;padding:1mm;margin-top:2mm}</style></head><body>");
        foreach (var line in lines)
        {
            body.Append("<div class=\"line\">")
                .Append(WebUtility.HtmlEncode(line))
                .Append("</div>");
        }

        foreach (var barcode in barcodes)
        {
            body.Append("<div class=\"barcode\" data-symbology=\"")
                .Append(WebUtility.HtmlEncode(barcode.Definition.Symbology.ToString()))
                .Append("\">")
                .Append(WebUtility.HtmlEncode(barcode.Value))
                .Append("</div>");
        }

        return body.Append("</body></html>").ToString();
    }

    private static string EscapeZpl(string value) =>
        value.Replace("\\", " ", StringComparison.Ordinal)
            .Replace("^", " ", StringComparison.Ordinal)
            .Replace("~", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

internal static class SimplePdfWriter
{
    public static byte[] Create(
        IEnumerable<string> lines,
        decimal widthMillimeters,
        decimal heightMillimeters)
    {
        var widthPoints = Math.Clamp((double)widthMillimeters * 72d / 25.4d, 40d, 5_000d);
        var heightPoints = Math.Clamp((double)heightMillimeters * 72d / 25.4d, 40d, 5_000d);
        var content = new StringBuilder()
            .Append("BT /F1 10 Tf 20 ")
            .Append(Math.Max(20d, heightPoints - 24d).ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" Td ");
        var first = true;
        foreach (var line in lines.Take(WmsLabelLimits.MaximumTemplateLines))
        {
            if (!first)
            {
                content.Append(" 0 -14 Td ");
            }

            content.Append('(').Append(EscapePdf(line)).Append(") Tj ");
            first = false;
        }

        content.Append("ET");
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPoints:0.##} {heightPoints:0.##}] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream"
        };

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true);
        writer.Write("%PDF-1.4\n");
        writer.Flush();
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            writer.Write($"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
            writer.Flush();
        }

        var xrefOffset = stream.Position;
        writer.Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        for (var index = 1; index < offsets.Count; index++)
        {
            writer.Write($"{offsets[index]:D10} 00000 n \n");
        }

        writer.Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();
        return stream.ToArray();
    }

    private static string EscapePdf(string value) =>
        string.Concat(value.Select(character => character switch
        {
            '\\' => "\\\\",
            '(' => "\\(",
            ')' => "\\)",
            >= ' ' and <= '~' => character.ToString(),
            _ => "?"
        }));
}
