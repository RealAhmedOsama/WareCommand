using System.Globalization;
using System.Text;

namespace Wms.Application.BulkExchange;

public static class WmsBulkImportTypes
{
    public const string ItemsV1 = "items.v1";
    public const string SuppliersV1 = "suppliers.v1";
    public const string CustomersV1 = "customers.v1";
    public const string SalesOrdersV1 = "sales-orders.v1";
    public const string PurchaseOrdersV1 = "purchase-orders.v1";
    public const string AdvanceShippingNoticesV1 = "asns.v1";
}

public static class WmsBulkImportFormats
{
    public const string Csv = "csv";
    public const string Excel = "xlsx";
}

public static class WmsBulkImportModes
{
    public const string DryRun = "DryRun";
    public const string Execute = "Execute";
}

public static class WmsBulkImportStatuses
{
    public const string Previewed = "Previewed";
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string PartiallyFailed = "PartiallyFailed";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}

public static class WmsBulkImportDuplicatePolicies
{
    public const string Reject = "Reject";
    public const string Skip = "Skip";
    public const string Update = "Update";
}

public sealed record BulkImportColumnMapping(
    string SourceColumn,
    string TargetField,
    bool Required = false,
    string? DefaultValue = null,
    string? Transformation = null);

public sealed record BulkImportMappingProfile(
    string ImportType,
    int Version,
    string Name,
    IReadOnlyCollection<BulkImportColumnMapping> Columns,
    string CultureName = "en-US",
    string TimeZone = "UTC",
    string DuplicatePolicy = WmsBulkImportDuplicatePolicies.Reject,
    bool AllowUnknownColumns = false)
{
    public CultureInfo Culture => CultureInfo.GetCultureInfo(CultureName);
}

public sealed record BulkImportSource(
    string FileName,
    string ContentType,
    string Format,
    string Content,
    string? Sha256 = null);

public sealed record BulkImportPreviewRequest(
    string ImportType,
    BulkImportSource Source,
    BulkImportMappingProfile Mapping,
    string UserId,
    int? WarehouseId = null,
    string? IdempotencyKey = null,
    string? CorrelationId = null);

public sealed record BulkImportExecuteRequest(
    long ExecutionId,
    string UserId,
    bool DryRun = true);

public sealed record BulkImportValidationError(
    int RowNumber,
    string? Field,
    string Code,
    string Message,
    string Severity = "Error");

public sealed record BulkImportRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values);

public sealed record BulkImportPreview(
    long ExecutionId,
    string Status,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    IReadOnlyList<BulkImportValidationError> Errors,
    IReadOnlyList<BulkImportRow> SampleRows);

public sealed record BulkImportExecutionDto(
    long Id,
    string ImportType,
    string Format,
    string Mode,
    string Status,
    string FileName,
    string SourceSha256,
    string MappingName,
    int MappingVersion,
    string CultureName,
    string DuplicatePolicy,
    string UserId,
    int? WarehouseId,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int SucceededRows,
    int FailedRows,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? Summary);

public sealed record BulkExportColumn(string Name, string Header);

public sealed record BulkExportRequest(
    IReadOnlyCollection<BulkExportColumn> Columns,
    IReadOnlyCollection<IReadOnlyDictionary<string, object?>> Rows,
    string CultureName = "en-US",
    bool IncludeUtf8Bom = true);

public sealed record BulkExportDocument(
    string FileName,
    string ContentType,
    byte[] Content,
    int RowCount);

public interface IBulkImportService
{
    Task<BulkImportPreview> PreviewAsync(
        BulkImportPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<BulkImportExecutionDto> ExecuteAsync(
        BulkImportExecuteRequest request,
        CancellationToken cancellationToken = default);

    Task<BulkImportExecutionDto?> GetAsync(
        long executionId,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(
        long executionId,
        string userId,
        CancellationToken cancellationToken = default);
}

public interface IBulkImportRowValidator
{
    string ImportType { get; }

    IReadOnlyList<BulkImportValidationError> Validate(
        BulkImportRow row,
        BulkImportMappingProfile mapping);
}

public interface IBulkImportHandler
{
    string ImportType { get; }

    Task<BulkImportHandlerResult> ExecuteAsync(
        BulkImportExecutionDto execution,
        IReadOnlyList<BulkImportRow> rows,
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record BulkImportHandlerResult(
    int SucceededRows,
    int FailedRows,
    string? Summary = null);

public interface IBulkExportService
{
    BulkExportDocument CreateCsv(
        string fileName,
        BulkExportRequest request);
}

public sealed record CsvParseOptions(
    char Delimiter = ',',
    int MaximumRows = 10_000,
    int MaximumColumns = 200,
    bool RequireHeader = true);

public sealed record CsvParseResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<BulkImportRow> Rows,
    IReadOnlyList<BulkImportValidationError> Errors);

public interface IBulkCsvParser
{
    CsvParseResult Parse(
        string content,
        CsvParseOptions? options = null);
}

public static class BulkCsvSafety
{
    public static string SanitizeForSpreadsheet(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '@' ||
               value[0] == '-' && (value.Length == 1 || !char.IsDigit(value[1]))
            ? "'" + value
            : value;
    }

    public static string Escape(string? value)
    {
        var safe = SanitizeForSpreadsheet(value);
        return safe.Contains(',', StringComparison.Ordinal) ||
               safe.Contains('"', StringComparison.Ordinal) ||
               safe.Contains('\n', StringComparison.Ordinal) ||
               safe.Contains('\r', StringComparison.Ordinal)
            ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : safe;
    }

    public static byte[] EncodeUtf8(string content, bool includeBom) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: includeBom).GetBytes(content);
}
