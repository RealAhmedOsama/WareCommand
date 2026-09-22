namespace Wms.Infrastructure.BulkExchange;

public sealed class WmsBulkImportMappingProfileEntity
{
    public long Id { get; set; }

    public string ImportType { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ColumnsJson { get; set; } = "[]";

    public string CultureName { get; set; } = "en-US";

    public string TimeZone { get; set; } = "UTC";

    public string DuplicatePolicy { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class WmsBulkImportExecutionEntity
{
    public long Id { get; set; }

    public string ImportType { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public long SourceFileId { get; set; }

    public string MappingName { get; set; } = string.Empty;

    public int MappingVersion { get; set; }

    public string CultureName { get; set; } = "en-US";

    public string TimeZone { get; set; } = "UTC";

    public string DuplicatePolicy { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public string? IdempotencyKey { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int ValidRows { get; set; }

    public int InvalidRows { get; set; }

    public int SucceededRows { get; set; }

    public int FailedRows { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? Summary { get; set; }

    public WmsBulkImportSourceFileEntity SourceFile { get; set; } = null!;

    public ICollection<WmsBulkImportRowResultEntity> Rows { get; set; } =
        new List<WmsBulkImportRowResultEntity>();
}

public sealed class WmsBulkImportSourceFileEntity
{
    public long Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public long Length { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public WmsBulkImportExecutionEntity Execution { get; set; } = null!;
}

public sealed class WmsBulkImportRowResultEntity
{
    public long Id { get; set; }

    public long ExecutionId { get; set; }

    public int RowNumber { get; set; }

    public string Status { get; set; } = string.Empty;

    public string ValuesJson { get; set; } = "{}";

    public string ValidationErrorsJson { get; set; } = "[]";

    public string? OutputReference { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public WmsBulkImportExecutionEntity Execution { get; set; } = null!;
}
