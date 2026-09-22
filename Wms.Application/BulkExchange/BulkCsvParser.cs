using System.Text;

namespace Wms.Application.BulkExchange;

public sealed class BulkCsvParser : IBulkCsvParser
{
    public CsvParseResult Parse(
        string content,
        CsvParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = options ?? new CsvParseOptions();
        if (settings.Delimiter == '"' || settings.MaximumRows is < 1 || settings.MaximumColumns is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "CSV parser limits or delimiter are invalid.");
        }

        var parsedRows = ParseRows(content, settings.Delimiter);
        var errors = new List<BulkImportValidationError>();
        if (parsedRows.Count == 0)
        {
            errors.Add(new BulkImportValidationError(1, null, "csv.empty", "The CSV contains no rows."));
            return new CsvParseResult([], [], errors);
        }

        var headers = settings.RequireHeader
            ? parsedRows[0].Select(NormalizeHeader).ToArray()
            : Enumerable.Range(1, parsedRows.Max(row => row.Count))
                .Select(index => $"Column{index}")
                .ToArray();
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new BulkImportValidationError(1, null, "csv.header_missing", "A non-empty header row is required."));
        }

        var duplicateHeader = headers
            .GroupBy(header => header, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicateHeader is not null)
        {
            errors.Add(new BulkImportValidationError(
                1,
                duplicateHeader.Key,
                "csv.header_duplicate",
                $"The header '{duplicateHeader.Key}' appears more than once."));
        }

        var dataStart = settings.RequireHeader ? 1 : 0;
        var rows = new List<BulkImportRow>();
        for (var index = dataStart; index < parsedRows.Count; index++)
        {
            var rowNumber = index + 1;
            var values = parsedRows[index];
            if (rows.Count >= settings.MaximumRows)
            {
                errors.Add(new BulkImportValidationError(
                    rowNumber,
                    null,
                    "csv.row_limit",
                    $"The CSV exceeds the {settings.MaximumRows} row limit."));
                break;
            }

            if (values.Count > settings.MaximumColumns)
            {
                errors.Add(new BulkImportValidationError(
                    rowNumber,
                    null,
                    "csv.column_limit",
                    $"The CSV exceeds the {settings.MaximumColumns} column limit."));
                continue;
            }

            if (values.Count != headers.Length)
            {
                errors.Add(new BulkImportValidationError(
                    rowNumber,
                    null,
                    "csv.column_count",
                    $"Expected {headers.Length} columns but received {values.Count}."));
                continue;
            }

            rows.Add(new BulkImportRow(
                rowNumber,
                headers
                    .Select((header, columnIndex) =>
                        new KeyValuePair<string, string?>(header, NullIfEmpty(values[columnIndex])))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)));
        }

        return new CsvParseResult(headers, rows, errors);
    }

    private static List<List<string>> ParseRows(string content, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if (!inQuotes && character == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                if (row.Any(value => value.Length > 0))
                {
                    rows.Add(row);
                }

                row = [];
                continue;
            }

            field.Append(character);
        }

        if (inQuotes)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        else if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            if (row.Any(value => value.Length > 0))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static string NormalizeHeader(string value) => value.Trim();

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
