using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.BulkExchange;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Common;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.BulkExchange;

public sealed class RequiredColumnBulkImportValidator : IBulkImportRowValidator
{
    public string ImportType => "*";

    public IReadOnlyList<BulkImportValidationError> Validate(
        BulkImportRow row,
        BulkImportMappingProfile mapping)
    {
        var errors = new List<BulkImportValidationError>();
        var mappedColumns = mapping.Columns
            .Select(column => column.SourceColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in mapping.Columns)
        {
            row.Values.TryGetValue(column.SourceColumn, out var value);
            value ??= column.DefaultValue;
            if (column.Required && string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new BulkImportValidationError(
                    row.RowNumber,
                    column.TargetField,
                    "import.required",
                    $"The required field '{column.SourceColumn}' is missing."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var transformation = column.Transformation?.Trim().ToLowerInvariant();
            if (transformation == "decimal" &&
                !decimal.TryParse(
                    value,
                    NumberStyles.Number,
                    mapping.Culture,
                    out _))
            {
                errors.Add(new BulkImportValidationError(
                    row.RowNumber,
                    column.TargetField,
                    "import.decimal_invalid",
                    $"'{value}' is not a valid {mapping.Culture.Name} decimal."));
            }
            else if (transformation == "date" &&
                     !DateTime.TryParse(
                         value,
                         mapping.Culture,
                         DateTimeStyles.AllowWhiteSpaces,
                         out _))
            {
                errors.Add(new BulkImportValidationError(
                    row.RowNumber,
                    column.TargetField,
                    "import.date_invalid",
                    $"'{value}' is not a valid {mapping.Culture.Name} date."));
            }
            else if (transformation == "bool" && !bool.TryParse(value, out _))
            {
                errors.Add(new BulkImportValidationError(
                    row.RowNumber,
                    column.TargetField,
                    "import.boolean_invalid",
                    $"'{value}' must be true or false."));
            }
        }

        if (!mapping.AllowUnknownColumns)
        {
            errors.AddRange(row.Values.Keys
                .Where(key => !mappedColumns.Contains(key))
                .Select(key => new BulkImportValidationError(
                    row.RowNumber,
                    key,
                    "import.column_unknown",
                    $"Column '{key}' is not present in mapping '{mapping.Name}'.")));
        }

        return errors;
    }
}

public sealed class BulkImportService(
    WmsDbContext context,
    IBulkCsvParser csvParser,
    IEnumerable<IBulkImportRowValidator> validators,
    IEnumerable<IBulkImportHandler> handlers,
    IWarehouseAccessService warehouseAccessService,
    IClock clock) : IBulkImportService
{
    private const int MaximumSourceCharacters = 5_000_000;
    private const int MaximumPreviewRows = 10_000;
    private const int MaximumSampleRows = 25;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BulkImportPreview> PreviewAsync(
        BulkImportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var authorization = await warehouseAccessService.AuthorizeAsync(
            PermissionFor(request.ImportType),
            request.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            throw new InvalidOperationException(authorization.Error);
        }

        var sourceHash = ComputeHash(request.Source.Content);
        var parsed = request.Source.Format.Equals(WmsBulkImportFormats.Csv, StringComparison.OrdinalIgnoreCase)
            ? csvParser.Parse(
                request.Source.Content,
                new CsvParseOptions(MaximumRows: MaximumPreviewRows))
            : throw new NotSupportedException(
                "Excel parsing is a provider gate; use the versioned CSV profile until an approved XLSX adapter is configured.");
        var mappingErrors = ValidateMapping(request.ImportType, parsed.Headers, request.Mapping);
        var rowErrors = new Dictionary<int, IReadOnlyList<BulkImportValidationError>>();
        foreach (var row in parsed.Rows)
        {
            var errors = validators
                .Where(validator => validator.ImportType == "*" ||
                    string.Equals(validator.ImportType, request.ImportType, StringComparison.OrdinalIgnoreCase))
                .SelectMany(validator => validator.Validate(row, request.Mapping))
                .ToArray();
            if (errors.Length > 0)
            {
                rowErrors[row.RowNumber] = errors;
            }
        }

        var allErrors = parsed.Errors
            .Concat(mappingErrors)
            .Concat(rowErrors.Values.SelectMany(errors => errors))
            .ToArray();
        var now = clock.UtcNow;
        var profile = await GetOrCreateProfileAsync(request.Mapping, now, cancellationToken);
        var source = new WmsBulkImportSourceFileEntity
        {
            FileName = NormalizeFileName(request.Source.FileName),
            ContentType = NormalizeContentType(request.Source.ContentType),
            Format = request.Source.Format.Trim().ToLowerInvariant(),
            Length = Encoding.UTF8.GetByteCount(request.Source.Content),
            Sha256 = sourceHash,
            Content = request.Source.Content,
            CreatedAtUtc = now
        };
        var execution = new WmsBulkImportExecutionEntity
        {
            ImportType = request.ImportType,
            Format = request.Source.Format.Trim().ToLowerInvariant(),
            Mode = WmsBulkImportModes.DryRun,
            Status = WmsBulkImportStatuses.Previewed,
            MappingName = profile.Name,
            MappingVersion = profile.Version,
            CultureName = request.Mapping.CultureName,
            TimeZone = request.Mapping.TimeZone,
            DuplicatePolicy = request.Mapping.DuplicatePolicy,
            UserId = request.UserId.Trim(),
            WarehouseId = request.WarehouseId,
            IdempotencyKey = NormalizeOptional(request.IdempotencyKey),
            CorrelationId = WmsExecutionIdentifiers.Normalize(request.CorrelationId),
            TotalRows = parsed.Rows.Count,
            ValidRows = parsed.Rows.Count - rowErrors.Count,
            InvalidRows = rowErrors.Count + parsed.Errors.Count,
            CreatedAtUtc = now,
            SourceFile = source
        };
        execution.Rows = parsed.Rows
            .Select(row => new WmsBulkImportRowResultEntity
            {
                RowNumber = row.RowNumber,
                Status = rowErrors.ContainsKey(row.RowNumber) ? "Invalid" : "Valid",
                ValuesJson = JsonSerializer.Serialize(row.Values, JsonOptions),
                ValidationErrorsJson = JsonSerializer.Serialize(
                    rowErrors.TryGetValue(row.RowNumber, out var errors) ? errors : [],
                    JsonOptions)
            })
            .ToList();
        context.BulkImportExecutions.Add(execution);
        await context.SaveChangesAsync(cancellationToken);
        return ToPreview(execution, allErrors, parsed.Rows);
    }

    public async Task<BulkImportExecutionDto> ExecuteAsync(
        BulkImportExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        var execution = await context.BulkImportExecutions
            .Include(item => item.SourceFile)
            .Include(item => item.Rows)
            .SingleOrDefaultAsync(item => item.Id == request.ExecutionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bulk import execution {request.ExecutionId} was not found.");
        if (!string.Equals(execution.UserId, request.UserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Only the import owner can execute this import.");
        }

        if (request.DryRun)
        {
            return ToDto(execution);
        }

        if (execution.InvalidRows > 0)
        {
            execution.Status = WmsBulkImportStatuses.Failed;
            execution.CompletedAtUtc = clock.UtcNow;
            execution.Summary = "Execution was blocked because the preview contains validation errors.";
            await context.SaveChangesAsync(cancellationToken);
            return ToDto(execution);
        }

        var handler = handlers.FirstOrDefault(item =>
            string.Equals(item.ImportType, execution.ImportType, StringComparison.OrdinalIgnoreCase));
        if (handler is null)
        {
            execution.Status = WmsBulkImportStatuses.Failed;
            execution.CompletedAtUtc = clock.UtcNow;
            execution.Summary = "No module handler is registered for this import type.";
            await context.SaveChangesAsync(cancellationToken);
            return ToDto(execution);
        }

        execution.Mode = WmsBulkImportModes.Execute;
        execution.Status = WmsBulkImportStatuses.Running;
        execution.StartedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        var rows = execution.Rows
            .Where(row => row.Status == "Valid")
            .Select(row => new BulkImportRow(
                row.RowNumber,
                JsonSerializer.Deserialize<Dictionary<string, string?>>(row.ValuesJson, JsonOptions) ?? []))
            .ToArray();
        try
        {
            var result = await handler.ExecuteAsync(
                ToDto(execution),
                rows,
                request.UserId,
                cancellationToken);
            execution.SucceededRows = result.SucceededRows;
            execution.FailedRows = result.FailedRows;
            execution.Status = result.FailedRows == 0
                ? WmsBulkImportStatuses.Succeeded
                : result.SucceededRows == 0
                    ? WmsBulkImportStatuses.Failed
                    : WmsBulkImportStatuses.PartiallyFailed;
            execution.Summary = result.Summary;
        }
        catch (OperationCanceledException)
        {
            execution.Status = WmsBulkImportStatuses.Canceled;
            throw;
        }
        catch (Exception exception)
        {
            execution.Status = WmsBulkImportStatuses.Failed;
            execution.Summary = exception.Message.Length <= 2_000
                ? exception.Message
                : exception.Message[..2_000];
        }

        execution.CompletedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(execution);
    }

    public async Task<BulkImportExecutionDto?> GetAsync(
        long executionId,
        CancellationToken cancellationToken = default) =>
        ToDtoOrNull(await context.BulkImportExecutions
            .AsNoTracking()
            .Include(item => item.SourceFile)
            .SingleOrDefaultAsync(item => item.Id == executionId, cancellationToken));

    public async Task<bool> CancelAsync(
        long executionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var execution = await context.BulkImportExecutions.SingleOrDefaultAsync(
            item => item.Id == executionId && item.UserId == userId,
            cancellationToken);
        if (execution is null || execution.Status is WmsBulkImportStatuses.Succeeded or WmsBulkImportStatuses.Failed)
        {
            return false;
        }

        execution.Status = WmsBulkImportStatuses.Canceled;
        execution.CompletedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<WmsBulkImportMappingProfileEntity> GetOrCreateProfileAsync(
        BulkImportMappingProfile mapping,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await context.BulkImportMappingProfiles.SingleOrDefaultAsync(
            profile => profile.ImportType == mapping.ImportType &&
                profile.Name == mapping.Name &&
                profile.Version == mapping.Version,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = new WmsBulkImportMappingProfileEntity
        {
            ImportType = mapping.ImportType,
            Version = mapping.Version,
            Name = mapping.Name,
            ColumnsJson = JsonSerializer.Serialize(mapping.Columns, JsonOptions),
            CultureName = mapping.CultureName,
            TimeZone = mapping.TimeZone,
            DuplicatePolicy = mapping.DuplicatePolicy,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.BulkImportMappingProfiles.Add(profile);
        return profile;
    }

    private static List<BulkImportValidationError> ValidateMapping(
        string importType,
        IReadOnlyList<string> headers,
        BulkImportMappingProfile mapping)
    {
        var errors = new List<BulkImportValidationError>();
        if (!string.Equals(importType, mapping.ImportType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new BulkImportValidationError(1, null, "import.type_mismatch", "The mapping import type does not match the request."));
        }

        var headerSet = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var column in mapping.Columns.Where(column => column.Required && !headerSet.Contains(column.SourceColumn)))
        {
            errors.Add(new BulkImportValidationError(1, column.TargetField, "import.column_missing", $"Required column '{column.SourceColumn}' is missing."));
        }

        if (!mapping.AllowUnknownColumns)
        {
            errors.AddRange(headers
                .Where(header => !mapping.Columns.Any(column => string.Equals(column.SourceColumn, header, StringComparison.OrdinalIgnoreCase)))
                .Select(header => new BulkImportValidationError(1, header, "import.column_unknown", $"Column '{header}' is not present in the mapping.")));
        }

        return errors;
    }

    private static BulkImportPreview ToPreview(
        WmsBulkImportExecutionEntity execution,
        IReadOnlyList<BulkImportValidationError> errors,
        IReadOnlyList<BulkImportRow> rows) =>
        new(
            execution.Id,
            execution.Status,
            execution.TotalRows,
            execution.ValidRows,
            execution.InvalidRows,
            errors,
            rows.Take(MaximumSampleRows).ToArray());

    private static BulkImportExecutionDto? ToDtoOrNull(WmsBulkImportExecutionEntity? entity) =>
        entity is null ? null : ToDto(entity);

    private static BulkImportExecutionDto ToDto(WmsBulkImportExecutionEntity execution) =>
        new(
            execution.Id,
            execution.ImportType,
            execution.Format,
            execution.Mode,
            execution.Status,
            execution.SourceFile?.FileName ?? string.Empty,
            execution.SourceFile?.Sha256 ?? string.Empty,
            execution.MappingName,
            execution.MappingVersion,
            execution.CultureName,
            execution.DuplicatePolicy,
            execution.UserId,
            execution.WarehouseId,
            execution.TotalRows,
            execution.ValidRows,
            execution.InvalidRows,
            execution.SucceededRows,
            execution.FailedRows,
            execution.CreatedAtUtc,
            execution.CompletedAtUtc,
            execution.Summary);

    private static void ValidateRequest(BulkImportPreviewRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ImportType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.Format);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source.Content);
        ArgumentNullException.ThrowIfNull(request.Mapping);
        if (request.Source.Content.Length > MaximumSourceCharacters)
        {
            throw new ArgumentException(
                $"Bulk import content cannot exceed {MaximumSourceCharacters} characters.",
                nameof(request));
        }
    }

    private static string PermissionFor(string importType) => importType switch
    {
        WmsBulkImportTypes.ItemsV1 => WmsPermissions.ItemsManage,
        WmsBulkImportTypes.SuppliersV1 => WmsPermissions.SuppliersManage,
        WmsBulkImportTypes.CustomersV1 => WmsPermissions.CustomersManage,
        WmsBulkImportTypes.SalesOrdersV1 => WmsPermissions.SalesOrdersManage,
        WmsBulkImportTypes.PurchaseOrdersV1 => WmsPermissions.PurchaseOrdersManage,
        WmsBulkImportTypes.AdvanceShippingNoticesV1 => WmsPermissions.AdvanceShippingNoticesManage,
        _ => throw new ArgumentException($"Unsupported bulk import type '{importType}'.", nameof(importType))
    };

    private static string NormalizeFileName(string fileName) =>
        Path.GetFileName(fileName.Trim());

    private static string NormalizeContentType(string contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "text/csv" : contentType.Trim()[..Math.Min(200, contentType.Trim().Length)];

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComputeHash(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();
}

public sealed class BulkExportService : IBulkExportService
{
    public BulkExportDocument CreateCsv(
        string fileName,
        BulkExportRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(request);
        var culture = CultureInfo.GetCultureInfo(request.CultureName);
        var builder = new StringBuilder();
        if (request.IncludeUtf8Bom)
        {
            builder.Append('\uFEFF');
        }

        builder.AppendLine(string.Join(',', request.Columns.Select(column => BulkCsvSafety.Escape(column.Header))));
        foreach (var row in request.Rows)
        {
            builder.AppendLine(string.Join(',', request.Columns.Select(column =>
            {
                row.TryGetValue(column.Name, out var value);
                return BulkCsvSafety.Escape(Format(value, culture));
            })));
        }

        var normalizedName = Path.GetFileNameWithoutExtension(fileName) + ".csv";
        return new BulkExportDocument(
            normalizedName,
            "text/csv; charset=utf-8",
            BulkCsvSafety.EncodeUtf8(builder.ToString(), includeBom: false),
            request.Rows.Count);
    }

    private static string? Format(object? value, CultureInfo culture) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, culture),
        _ => value.ToString()
    };
}
