using Wms.Application.Common;
using Wms.Application.Devices;
using Wms.Application.Localization;
using Wms.Domain.Enums;

namespace Wms.Application.Labels;

public static class WmsLabelLimits
{
    public const int MaximumTemplateNameLength = 100;
    public const int MaximumTemplateBodyLength = 50_000;
    public const int MaximumTemplateLines = 100;
    public const int MaximumLineLength = 250;
    public const int MaximumFields = 100;
    public const int MaximumBarcodes = 20;
    public const int MaximumCopies = 100;
    public const int MaximumPayloadBytes = 2_000_000;
}

public enum WmsLabelDocumentType
{
    Item = 1,
    Location = 2,
    Lot = 3,
    Serial = 4,
    LicensePlate = 5,
    Receipt = 6,
    Pallet = 7,
    Package = 8,
    Shipment = 9,
    Return = 10
}

public enum WmsLabelFieldKind
{
    Text = 1,
    Number = 2,
    Date = 3,
    ProductCode = 4,
    Gs1 = 5,
    Sscc = 6
}

public enum WmsPrintJobStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4
}

public sealed record WmsLabelFieldDefinition
{
    public required string Key { get; init; }

    public WmsLabelFieldKind Kind { get; init; } = WmsLabelFieldKind.Text;

    public bool Required { get; init; } = true;

    public int MaximumLength { get; init; } = 250;
}

public sealed record WmsLabelBarcodeDefinition
{
    public required string Name { get; init; }

    public required string SourceKey { get; init; }

    public BarcodeSymbology Symbology { get; init; } = BarcodeSymbology.Code128;

    public bool HumanReadable { get; init; } = true;

    public int HeightDots { get; init; } = 80;
}

public sealed record WmsLabelTemplateDefinition
{
    public required string Name { get; init; }

    public int Version { get; init; } = 1;

    public WmsLabelDocumentType DocumentType { get; init; } = WmsLabelDocumentType.Item;

    public string Language { get; init; } = WmsLocaleCatalog.DefaultLocale;

    public decimal WidthMillimeters { get; init; } = 100m;

    public decimal HeightMillimeters { get; init; } = 50m;

    public WmsPrintFormat Format { get; init; } = WmsPrintFormat.Pdf;

    /// <summary>
    /// A deliberately small declarative layout: one safe text line per row.
    /// It is not executable ZPL, HTML, Razor, JavaScript, or a scripting language.
    /// </summary>
    public string Body { get; init; } = string.Empty;

    public IReadOnlyList<WmsLabelFieldDefinition> Fields { get; init; } = [];

    public IReadOnlyList<WmsLabelBarcodeDefinition> Barcodes { get; init; } = [];

    public IReadOnlyList<WmsPrintRoute> Routes { get; init; } = [];

    public int? WarehouseId { get; init; }

    public string? CustomerCode { get; init; }

    public string? SupplierCode { get; init; }

    public bool IsActive { get; init; }
}

public sealed record WmsLabelTemplateSaveRequest(
    WmsLabelTemplateDefinition Definition,
    string ActorUserId,
    string? ActorUserName = null);

public sealed record WmsLabelTemplateQuery(
    string? Name = null,
    WmsLabelDocumentType? DocumentType = null,
    string? Language = null,
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record WmsLabelTemplateResolutionRequest(
    string Name,
    WmsLabelDocumentType DocumentType,
    string Language,
    WmsPrintFormat Format,
    int? WarehouseId = null,
    string? CustomerCode = null,
    string? SupplierCode = null);

public sealed record WmsLabelTemplateDto(
    long Id,
    WmsLabelTemplateDefinition Definition,
    string ContentHash,
    DateTimeOffset CreatedAtUtc,
    string CreatedByUserId,
    DateTimeOffset? ActivatedAtUtc,
    string? ActivatedByUserId);

public sealed record WmsLabelPreviewRequest(
    WmsLabelTemplateResolutionRequest Template,
    IReadOnlyDictionary<string, string?> Data,
    int Copies = 1);

public sealed record WmsLabelPrintRequest(
    WmsLabelTemplateResolutionRequest Template,
    IReadOnlyDictionary<string, string?> Data,
    int Copies = 1,
    int? WarehouseId = null,
    string? StationCode = null,
    string? SourceReference = null,
    string? IdempotencyKey = null,
    bool IsReprint = false,
    string? ReprintReason = null,
    string? ActorUserId = null,
    string? ActorUserName = null);

public sealed record WmsRenderedLabel(
    byte[] Payload,
    string ContentType,
    string TextPreview,
    string BrowserHtml,
    bool PreferBrowserPdf,
    string TemplateName,
    int TemplateVersion,
    WmsPrintFormat Format);

public sealed record WmsLabelPrintResult(
    long JobId,
    WmsPrintJobStatus Status,
    string TemplateName,
    int TemplateVersion,
    string? RouteName,
    WmsPrintFormat Format,
    WmsPrintTransport? Transport,
    int AttemptCount,
    string? ErrorCode,
    string? Error,
    WmsRenderedLabel? Rendered = null);

public interface ILabelTemplateService
{
    Task<Result<WmsLabelTemplateDto>> SaveVersionAsync(
        WmsLabelTemplateSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<WmsLabelTemplateDto>> ActivateAsync(
        string name,
        int version,
        string language,
        int? warehouseId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default);

    Task<Result<WmsLabelTemplateDto>> RollbackAsync(
        string name,
        int version,
        string language,
        int? warehouseId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default);

    Task<Result<WmsLabelTemplateDto>> ResolveActiveAsync(
        WmsLabelTemplateResolutionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WmsLabelTemplateDto>>> ListAsync(
        WmsLabelTemplateQuery query,
        CancellationToken cancellationToken = default);
}

public interface ILabelPrintService
{
    Task<Result<WmsRenderedLabel>> PreviewAsync(
        WmsLabelPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<WmsLabelPrintResult>> PrintAsync(
        WmsLabelPrintRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<WmsLabelPrintResult>> RetryAsync(
        long jobId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default);
}
