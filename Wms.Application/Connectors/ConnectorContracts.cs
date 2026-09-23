using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Application.Common;

namespace Wms.Application.Connectors;

public static class WmsConnectorTypes
{
    public const string GenericErpV1 = "generic-erp.v1";
    public const string EcommerceOrdersV1 = "ecommerce-orders.v1";
    public const string MarketplaceV1 = "marketplace.v1";
    public const string CarrierV1 = "carrier.v1";

    public static IReadOnlyList<string> All { get; } =
    [
        GenericErpV1,
        EcommerceOrdersV1,
        MarketplaceV1,
        CarrierV1
    ];

    public static bool IsKnown(string value) => value switch
    {
        GenericErpV1 or EcommerceOrdersV1 or MarketplaceV1 or CarrierV1 => true,
        _ => false
    };
}

public static class WmsConnectorModes
{
    public const string Pull = "pull";
    public const string Push = "push";
    public const string Webhook = "webhook";
    public const string File = "file";

    public static bool IsKnown(string value) => value switch
    {
        Pull or Push or Webhook or File => true,
        _ => false
    };
}

public static class WmsConnectorOperations
{
    public const string MasterDataSync = "master-data-sync";
    public const string InboundOrders = "inbound-orders";
    public const string AdvanceShippingNotices = "advance-shipping-notices";
    public const string OutboundOrders = "outbound-orders";
    public const string InventoryAvailability = "inventory-availability";
    public const string ShipmentConfirmation = "shipment-confirmation";
    public const string Returns = "returns";
    public const string CarrierLabels = "carrier-labels";
    public const string Tracking = "tracking";
    public const string Acknowledgements = "acknowledgements";

    public static bool IsKnown(string value) => value switch
    {
        MasterDataSync or
        InboundOrders or
        AdvanceShippingNotices or
        OutboundOrders or
        InventoryAvailability or
        ShipmentConfirmation or
        Returns or
        CarrierLabels or
        Tracking or
        Acknowledgements => true,
        _ => false
    };
}

public static class WmsConnectorStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Paused = "Paused";
    public const string Disabled = "Disabled";
    public const string Failed = "Failed";
}

public static class WmsConnectorHealthStatuses
{
    public const string Unknown = "Unknown";
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";
}

public static class WmsConnectorImplementationStatuses
{
    public const string Implemented = "Implemented";
    public const string Reference = "Reference";
    public const string ContractOnly = "ContractOnly";
    public const string Disabled = "Disabled";
}

public static class WmsConnectorConfigurationStatuses
{
    public const string Disabled = "Disabled";
    public const string Unconfigured = "Unconfigured";
    public const string Configured = "Configured";
}

public static class WmsConnectorVerificationStatuses
{
    public const string Unverified = "Unverified";
    public const string Verified = "Verified";
    public const string Failed = "Failed";
}

public static class WmsConnectorCapabilityStatuses
{
    public const string Disabled = "Disabled";
    public const string Reference = "Reference";
    public const string ContractOnly = "ContractOnly";
    public const string Unconfigured = "Unconfigured";
    public const string Configured = "Configured";
    public const string Unverified = "Unverified";
    public const string Healthy = "Healthy";
    public const string Failed = "Failed";
}

public static class WmsConnectorRunStatuses
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}

public static class WmsConnectorConflictPolicies
{
    public const string Reject = "Reject";
    public const string PreferExternal = "PreferExternal";
    public const string PreferWms = "PreferWms";

    public static bool IsKnown(string value) => value switch
    {
        Reject or PreferExternal or PreferWms => true,
        _ => false
    };
}

public sealed record ConnectorMappingRule(
    string ExternalField,
    string WmsField,
    string? Transformation = null,
    IReadOnlyDictionary<string, string>? ValueMap = null,
    string? UnitOfMeasure = null);

public sealed record ConnectorMappingProfileRequest(
    string ConnectorType,
    string Name,
    int Version,
    string ExternalIdField,
    IReadOnlyCollection<ConnectorMappingRule> Rules,
    string CultureName = "en-US",
    string ConflictPolicy = WmsConnectorConflictPolicies.Reject);

public sealed record ConnectorMappingProfileDto(
    long Id,
    string ConnectorType,
    string Name,
    int Version,
    string ExternalIdField,
    IReadOnlyList<ConnectorMappingRule> Rules,
    string CultureName,
    string ConflictPolicy,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConnectorInstanceCreateRequest(
    string ConnectorType,
    string Name,
    IReadOnlyCollection<int> AllowedWarehouseIds,
    string CredentialReference,
    IReadOnlyCollection<string> Modes,
    string? Schedule,
    string MappingProfileName,
    int MappingProfileVersion,
    bool Activate = false);

public sealed record ConnectorCredentialRotationRequest(string CredentialReference);

public sealed record ConnectorInstanceDto(
    long Id,
    string ConnectorType,
    string Name,
    IReadOnlyList<int> AllowedWarehouseIds,
    string CredentialReference,
    int CredentialVersion,
    IReadOnlyList<string> Modes,
    string? Schedule,
    string MappingProfileName,
    int MappingProfileVersion,
    string Status,
    string Cursor,
    string HealthStatus,
    string? HealthSummary,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    DateTimeOffset? LastSuccessfulRunAtUtc,
    DateTimeOffset? LastHealthCheckAtUtc,
    int ConsecutiveFailureCount,
    string ImplementationStatus,
    string CapabilityStatus,
    string ConfigurationStatus,
    string VerificationStatus);

public sealed record ConnectorCapabilityDto(
    string ConnectorType,
    string ImplementationStatus,
    bool CanActivate,
    IReadOnlyList<string> SupportedModes,
    IReadOnlyList<string> SupportedOperations,
    string ConfigurationStatus,
    string VerificationStatus,
    string HealthStatus,
    string CredentialRequirement,
    string TransportRequirement,
    bool LiveAcceptanceRequired);

public sealed record ConnectorRunRequest(
    long ConnectorId,
    string Operation,
    string Mode,
    int? WarehouseId = null,
    string? Cursor = null,
    string? IdempotencyKey = null,
    int BatchSize = 100);

public sealed record ConnectorRunDto(
    long Id,
    long ConnectorId,
    string Operation,
    string Mode,
    string Status,
    string IdempotencyKey,
    string? CursorBefore,
    string? CursorAfter,
    int AttemptCount,
    int RecordsSeen,
    int RecordsCreated,
    int RecordsUpdated,
    int RecordsSkipped,
    int Conflicts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record ConnectorExternalRecord(
    string RecordType,
    string ExternalId,
    int? WarehouseId,
    IReadOnlyDictionary<string, string?> Fields,
    string? ExternalVersion = null);

public sealed record ConnectorPullRequest(
    ConnectorInstanceContext Connector,
    string Operation,
    string? Cursor,
    int BatchSize,
    string CorrelationId);

public sealed record ConnectorPullResult(
    IReadOnlyList<ConnectorExternalRecord> Records,
    string? NextCursor,
    bool HasMore,
    string? ProviderReference = null);

public sealed record ConnectorPushRequest(
    ConnectorInstanceContext Connector,
    string Operation,
    string CorrelationId,
    int BatchSize);

public sealed record ConnectorPushResult(
    int RecordsSent,
    string? ProviderReference = null);

public sealed record ConnectorHealthResult(
    bool Succeeded,
    bool Retryable,
    string HealthStatus,
    string Summary,
    string? ProviderReference = null);

public sealed record ConnectorInstanceContext(
    long Id,
    string ConnectorType,
    string Name,
    IReadOnlySet<int> AllowedWarehouseIds,
    string CredentialReference,
    int CredentialVersion,
    IReadOnlySet<string> Modes,
    string MappingProfileName,
    int MappingProfileVersion,
    string? Cursor,
    string CorrelationId);

public interface IConnectorAdapter
{
    string ConnectorType { get; }

    // Adapters are contract-only until they explicitly identify an implemented provider transport.
    string ImplementationStatus => WmsConnectorImplementationStatuses.ContractOnly;

    IReadOnlySet<string> SupportedModes { get; }

    IReadOnlySet<string> SupportedOperations { get; }

    Task<ConnectorHealthResult> TestConnectionAsync(
        ConnectorInstanceContext connector,
        CancellationToken cancellationToken = default);

    Task<ConnectorPullResult> PullAsync(
        ConnectorPullRequest request,
        CancellationToken cancellationToken = default);

    Task<ConnectorPushResult> PushAsync(
        ConnectorPushRequest request,
        CancellationToken cancellationToken = default);
}

public interface IConnectorService
{
    Task<Result<IReadOnlyList<ConnectorCapabilityDto>>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ConnectorMappingProfileDto>> SaveMappingProfileAsync(
        ConnectorMappingProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ConnectorInstanceDto>> CreateAsync(
        ConnectorInstanceCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ConnectorInstanceDto>>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ConnectorInstanceDto>> RotateCredentialAsync(
        long connectorId,
        ConnectorCredentialRotationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ConnectorRunDto>> TestConnectionAsync(
        long connectorId,
        CancellationToken cancellationToken = default);

    Task<Result<ConnectorRunDto>> RunAsync(
        ConnectorRunRequest request,
        CancellationToken cancellationToken = default);
}

public static class ConnectorSecurityPolicy
{
    private static readonly string[] SensitiveMarkers =
    [
        "password=",
        "secret=",
        "token=",
        "api_key=",
        "apikey=",
        "authorization:",
        "bearer "
    ];

    public static string NormalizeCredentialReference(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 4 or > 250 ||
            normalized.Any(char.IsWhiteSpace) ||
            SensitiveMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "A connector must reference a deployment-managed credential without embedding a secret.",
                nameof(value));
        }

        return normalized;
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return SensitiveMarkers.Aggregate(
            value.Trim(),
            (current, marker) => current.Contains(marker, StringComparison.OrdinalIgnoreCase)
                ? marker + "[redacted]"
                : current);
    }
}

public static class ConnectorIdentity
{
    public static string BuildExternalKey(long connectorId, string recordType, string externalId)
    {
        var normalized = $"{connectorId}:{Normalize(recordType)}:{Normalize(externalId)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string BuildIdempotencyKey(
        long connectorId,
        string operation,
        string mode,
        string? cursor)
    {
        var normalized = $"{connectorId}:{Normalize(operation)}:{Normalize(mode)}:{Normalize(cursor ?? "initial")}";
        return "connector:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "_" : value.Trim().ToUpperInvariant();
}

public static class ConnectorMappingEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyDictionary<string, string?> Apply(
        IReadOnlyDictionary<string, string?> source,
        IEnumerable<ConnectorMappingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rules);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            source.TryGetValue(rule.ExternalField, out var value);
            if (value is not null && rule.ValueMap is { Count: > 0 } &&
                rule.ValueMap.TryGetValue(value, out var mapped))
            {
                value = mapped;
            }

            result[rule.WmsField] = ApplyTransformation(value, rule.Transformation);
        }

        return result;
    }

    public static string SerializeRules(IReadOnlyCollection<ConnectorMappingRule> rules) =>
        JsonSerializer.Serialize(rules, JsonOptions);

    public static IReadOnlyList<ConnectorMappingRule> DeserializeRules(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ConnectorMappingRule>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ApplyTransformation(string? value, string? transformation) =>
        transformation?.Trim().ToLowerInvariant() switch
        {
            "trim" => value?.Trim(),
            "upper" => value?.Trim().ToUpperInvariant(),
            "lower" => value?.Trim().ToLowerInvariant(),
            _ => value
        };
}
