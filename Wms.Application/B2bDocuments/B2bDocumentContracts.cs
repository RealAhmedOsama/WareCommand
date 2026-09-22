using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Application.Common;
using Wms.Application.Connectors;

namespace Wms.Application.B2bDocuments;

public static class WmsB2bDocumentTypes
{
    public const string ItemMaster = "item-master";
    public const string LocationMaster = "location-master";
    public const string PurchaseOrder = "purchase-order";
    public const string AdvanceShippingNotice = "advance-shipping-notice";
    public const string Receipt = "receipt";
    public const string SalesOrder = "sales-order";
    public const string WarehouseOrder = "warehouse-order";
    public const string InventoryStatus = "inventory-status";
    public const string ShipmentConfirmation = "shipment-confirmation";
    public const string Return = "return";

    public static bool IsKnown(string value) => value switch
    {
        ItemMaster or LocationMaster or PurchaseOrder or AdvanceShippingNotice or Receipt or
        SalesOrder or WarehouseOrder or InventoryStatus or ShipmentConfirmation or Return => true,
        _ => false
    };
}

public static class WmsB2bStandards
{
    public const string Canonical = "canonical";
    public const string X12 = "x12";
    public const string Edifact = "edifact";
}

public static class WmsB2bDirections
{
    public const string Inbound = "Inbound";
    public const string Outbound = "Outbound";
}

public static class WmsB2bTransportModes
{
    public const string Sftp = "sftp";
    public const string FileDrop = "file-drop";
    public const string Api = "api";
    public const string Webhook = "webhook";
}

public static class WmsB2bDocumentStatuses
{
    public const string Received = "Received";
    public const string Validated = "Validated";
    public const string Quarantined = "Quarantined";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Replayed = "Replayed";
}

public static class WmsB2bAcknowledgementStatuses
{
    public const string Pending = "Pending";
    public const string Generated = "Generated";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

public sealed record B2bMappingRule(
    string ExternalField,
    string CanonicalField,
    string? Transformation = null,
    IReadOnlyDictionary<string, string>? ValueMap = null);

public sealed record B2bMappingProfileRequest(
    string DocumentType,
    string Name,
    int Version,
    IReadOnlyCollection<B2bMappingRule> Rules,
    string Standard = WmsB2bStandards.Canonical);

public sealed record B2bMappingProfileDto(
    long Id,
    string DocumentType,
    string Name,
    int Version,
    IReadOnlyList<B2bMappingRule> Rules,
    string Standard,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TradingPartnerCreateRequest(
    string Code,
    string Name,
    string Standard,
    string CredentialReference,
    IReadOnlyCollection<int> AllowedWarehouseIds,
    IReadOnlyCollection<string> DocumentTypes,
    string MappingProfileName,
    int MappingProfileVersion,
    bool RequireAcknowledgement = true,
    bool Activate = false);

public sealed record TradingPartnerDto(
    long Id,
    string Code,
    string Name,
    string Standard,
    string CredentialReference,
    int CredentialVersion,
    IReadOnlyList<int> AllowedWarehouseIds,
    IReadOnlyList<string> DocumentTypes,
    string MappingProfileName,
    int MappingProfileVersion,
    bool RequireAcknowledgement,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record B2bDocumentEnvelope(
    Guid MessageId,
    string DocumentType,
    string Version,
    string Direction,
    string TransportMode,
    string InterchangeControlNumber,
    string GroupControlNumber,
    string DocumentControlNumber,
    int WarehouseId,
    IReadOnlyDictionary<string, string?> Fields,
    int LineCount,
    int? DeclaredLineCount = null,
    DateTimeOffset? OccurredAtUtc = null,
    string? CorrelationId = null);

public sealed record B2bDocumentSubmitRequest(
    long TradingPartnerId,
    B2bDocumentEnvelope Envelope,
    string? IdempotencyKey = null);

public sealed record B2bDocumentValidationError(
    string Code,
    string Message,
    string? Field = null);

public sealed record B2bDocumentDto(
    long Id,
    long TradingPartnerId,
    Guid MessageId,
    string DocumentType,
    string Version,
    string Direction,
    string TransportMode,
    string InterchangeControlNumber,
    string GroupControlNumber,
    string DocumentControlNumber,
    int WarehouseId,
    string Status,
    string PayloadHash,
    int LineCount,
    int? DeclaredLineCount,
    IReadOnlyList<B2bDocumentValidationError> ValidationErrors,
    string AcknowledgementStatus,
    int ReplayCount,
    string CorrelationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record B2bDocumentSubmitResult(B2bDocumentDto Document, bool WasDuplicate);

public sealed record B2bAcknowledgementRequest(
    long DocumentId,
    string AcknowledgementType,
    string Status,
    string? ReasonCode = null,
    string? ReasonMessage = null);

public sealed record B2bAcknowledgementDto(
    long Id,
    long DocumentId,
    string AcknowledgementType,
    string Status,
    string ControlNumber,
    string? ReasonCode,
    string? ReasonMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SentAtUtc);

public sealed record B2bDocumentReplayRequest(long DocumentId, string? Reason = null);

public interface IB2bDocumentService
{
    Task<Result<B2bMappingProfileDto>> SaveMappingProfileAsync(
        B2bMappingProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TradingPartnerDto>> CreateTradingPartnerAsync(
        TradingPartnerCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<B2bDocumentSubmitResult>> SubmitAsync(
        B2bDocumentSubmitRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<B2bAcknowledgementDto>> AcknowledgeAsync(
        B2bAcknowledgementRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<B2bDocumentDto>> ReplayAsync(
        B2bDocumentReplayRequest request,
        CancellationToken cancellationToken = default);
}

public interface IB2bDocumentAdapter
{
    string Standard { get; }

    IReadOnlySet<string> SupportedDocumentTypes { get; }

    Task<Result<B2bDocumentEnvelope>> ParseAsync(
        string payload,
        CancellationToken cancellationToken = default);

    Task<Result<string>> SerializeAsync(
        B2bDocumentEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public interface IB2bDocumentHandler
{
    string DocumentType { get; }

    Task<Result> ProcessAsync(
        B2bDocumentDto document,
        CancellationToken cancellationToken = default);
}

public interface IB2bDocumentTransport
{
    string Mode { get; }

    Task<Result> SendAsync(
        B2bDocumentDto document,
        string credentialReference,
        CancellationToken cancellationToken = default);
}

public static class B2bDocumentIdentity
{
    public static string BuildExternalKey(
        long tradingPartnerId,
        string interchangeControlNumber,
        string documentControlNumber) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{tradingPartnerId}:{Normalize(interchangeControlNumber)}:{Normalize(documentControlNumber)}")))
            .ToLowerInvariant();

    public static string ComputePayloadHash(IReadOnlyDictionary<string, string?> fields)
    {
        var canonical = fields
            .OrderBy(field => field.Key, StringComparer.Ordinal)
            .Select(field => $"{field.Key}={field.Value}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", canonical)))).ToLowerInvariant();
    }

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "_" : value.Trim().ToUpperInvariant();
}

public static class B2bCanonicalPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyDictionary<string, string?> fields) =>
        JsonSerializer.Serialize(fields, JsonOptions);

    public static IReadOnlyDictionary<string, string?> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions) ??
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string SerializeErrors(IReadOnlyCollection<B2bDocumentValidationError> errors) =>
        JsonSerializer.Serialize(errors, JsonOptions);

    public static IReadOnlyList<B2bDocumentValidationError> DeserializeErrors(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<B2bDocumentValidationError>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeRules(IReadOnlyCollection<B2bMappingRule> rules) =>
        JsonSerializer.Serialize(rules, JsonOptions);

    public static IReadOnlyList<B2bMappingRule> DeserializeRules(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<B2bMappingRule>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
