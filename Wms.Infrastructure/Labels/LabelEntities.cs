namespace Wms.Infrastructure.Labels;

public sealed class WmsLabelTemplateEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public int DocumentType { get; set; }

    public string Language { get; set; } = string.Empty;

    public decimal WidthMillimeters { get; set; }

    public decimal HeightMillimeters { get; set; }

    public int Format { get; set; }

    public string Body { get; set; } = string.Empty;

    public string FieldsJson { get; set; } = "[]";

    public string BarcodesJson { get; set; } = "[]";

    public string RoutesJson { get; set; } = "[]";

    public int? WarehouseId { get; set; }

    public string? CustomerCode { get; set; }

    public string? SupplierCode { get; set; }

    public bool IsActive { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset? ActivatedAtUtc { get; set; }

    public string? ActivatedByUserId { get; set; }
}

public sealed class WmsPrintJobEntity
{
    public long Id { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public int TemplateVersion { get; set; }

    public int DocumentType { get; set; }

    public string Language { get; set; } = string.Empty;

    public int Format { get; set; }

    public int? WarehouseId { get; set; }

    public string? StationCode { get; set; }

    public string? RouteName { get; set; }

    public int? Transport { get; set; }

    public string? AdapterKey { get; set; }

    public int Copies { get; set; }

    public bool IsReprint { get; set; }

    public string? ReprintReason { get; set; }

    public string? SourceReference { get; set; }

    public string ActorUserId { get; set; } = string.Empty;

    public string? ActorUserName { get; set; }

    public string DataJson { get; set; } = "{}";

    public byte[] Payload { get; set; } = [];

    public string ContentType { get; set; } = string.Empty;

    public string TextPreview { get; set; } = string.Empty;

    public string BrowserHtml { get; set; } = string.Empty;

    public bool PreferBrowserPdf { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastAttemptAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
