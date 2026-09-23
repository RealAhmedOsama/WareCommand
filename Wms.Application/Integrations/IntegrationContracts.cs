using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Wms.Application.Integrations;

public static class WmsIntegrationEventTypes
{
    public const string InventoryMovementRecorded = "inventory.movement-recorded.v1";
    public const string InventoryStockAdjusted = "inventory.stock-adjusted.v1";
    public const string WarehouseChanged = "warehouse.changed.v1";
    public const string ItemChanged = "item.changed.v1";
    public const string WorkLifecycleChanged = "work.lifecycle-changed.v1";
    public const string NotificationPublished = "notification.published.v1";

    public static bool IsSupported(string eventType) => eventType switch
    {
        InventoryMovementRecorded or
        InventoryStockAdjusted or
        WarehouseChanged or
        ItemChanged or
        WorkLifecycleChanged or
        NotificationPublished => true,
        _ => false
    };
}

public static class WmsIntegrationEventStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string DeadLettered = "DeadLettered";
    public const string NoSubscribers = "NoSubscribers";
}

public static class WmsIntegrationInboxStatuses
{
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string DeadLettered = "DeadLettered";
}

public static class WmsWebhookSubscriptionStatuses
{
    public const string Active = "Active";
    public const string Disabled = "Disabled";
    public const string Revoked = "Revoked";
}

public static class WmsWebhookDeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string DeadLettered = "DeadLettered";
}

public sealed record IntegrationEventDraft(
    string EventType,
    string AggregateType,
    string AggregateKey,
    object Payload,
    int? WarehouseId = null,
    string? CorrelationId = null,
    string? CausationId = null,
    DateTimeOffset? OccurredAtUtc = null,
    Guid? EventId = null);

public sealed record IntegrationEventPublishResult(
    Guid EventId,
    bool WasAlreadyEnqueued);

public sealed record IntegrationDispatchResult(
    int EventsClaimed,
    int DeliveriesAttempted,
    int DeliveriesSucceeded,
    int DeliveriesFailed,
    int EventsDeadLettered);

public sealed record IntegrationInboxRequest(
    string SourceSystem,
    string ExternalMessageId,
    string EventType,
    string PayloadJson,
    string? CorrelationId = null);

public sealed record IntegrationInboxClaim(
    long MessageId,
    bool ShouldProcess,
    bool WasDuplicate,
    string? Reason = null);

public sealed record WebhookSubscriptionCreateRequest(
    string Name,
    string EndpointUrl,
    IReadOnlyCollection<string>? EventTypes = null,
    IReadOnlyCollection<int>? WarehouseIds = null,
    int MaximumAttempts = 8);

public sealed record WebhookSubscriptionDto(
    long Id,
    string Name,
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    IReadOnlyList<int> WarehouseIds,
    string Status,
    int SecretVersion,
    int MaximumAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastDeliveryAtUtc,
    DateTimeOffset? DisabledAtUtc);

public sealed record WebhookSubscriptionIssue(
    WebhookSubscriptionDto Subscription,
    string Secret);

public sealed record WebhookSubscriptionRotation(
    string Secret,
    DateTimeOffset PreviousSecretValidUntilUtc,
    WebhookSubscriptionDto Subscription);

public sealed record WebhookDeliveryRequest(
    long SubscriptionId,
    string EndpointUrl,
    string EventType,
    Guid EventId,
    string PayloadJson,
    IReadOnlyDictionary<string, string> Headers,
    long DeliveryId = 0,
    int PayloadVersion = 1);

public sealed record WebhookDeliveryResult(
    bool Succeeded,
    bool Retryable,
    int? ResponseStatusCode = null,
    string? ResponseBody = null,
    string? Error = null,
    DateTimeOffset? RetryAfterUtc = null);

public sealed record WebhookDeliveryTransportCapability(
    bool Enabled,
    bool Configured);

public sealed record WebhookDeliveryVerificationSummary(
    int ActiveSubscriptions,
    int VerifiedSubscriptions);

public interface IIntegrationEventWriter
{
    Task<IntegrationEventPublishResult> EnqueueAsync(
        IntegrationEventDraft draft,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationInboxService
{
    Task<IntegrationInboxClaim> TryBeginAsync(
        IntegrationInboxRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        long messageId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        long messageId,
        Exception exception,
        DateTimeOffset failedAtUtc,
        bool deadLetter,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationOutboxDispatcher
{
    Task<IntegrationDispatchResult> DispatchAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default);
}

public interface IWebhookDeliveryTransport
{
    WebhookDeliveryTransportCapability Capability { get; }

    Task<WebhookDeliveryResult> SendAsync(
        WebhookDeliveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IWebhookSubscriptionService
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<WebhookSubscriptionIssue> CreateAsync(
        WebhookSubscriptionCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<WebhookSubscriptionRotation> RotateSecretAsync(
        long subscriptionId,
        TimeSpan overlap,
        CancellationToken cancellationToken = default);

    Task<bool> SetStatusAsync(
        long subscriptionId,
        string status,
        CancellationToken cancellationToken = default);

    Task<WebhookDeliveryVerificationSummary> GetDeliveryVerificationSummaryAsync(
        CancellationToken cancellationToken = default);
}

public interface IWebhookSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

public static class WebhookSignature
{
    public const string HeaderName = "X-WareCommand-Signature";
    public const string TimestampHeaderName = "X-WareCommand-Timestamp";
    public const string EventIdHeaderName = "X-WareCommand-Event-Id";
    public const string DeliveryIdHeaderName = "X-WareCommand-Delivery-Id";
    public const string EventTypeHeaderName = "X-WareCommand-Event-Type";
    public const string VersionHeaderName = "X-WareCommand-Event-Version";

    public static string Create(string secret, string payloadJson, DateTimeOffset timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentNullException.ThrowIfNull(payloadJson);

        var timestamp = timestampUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signedPayload = $"{timestamp}.{payloadJson}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signedPayload));
        return $"t={timestamp};v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static bool Verify(
        string secret,
        string payloadJson,
        string signature,
        DateTimeOffset nowUtc,
        TimeSpan replayWindow = default)
    {
        if (string.IsNullOrWhiteSpace(secret) ||
            string.IsNullOrWhiteSpace(payloadJson) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var parts = signature.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var timestampPart = parts.FirstOrDefault(part => part.StartsWith("t=", StringComparison.Ordinal));
        var valuePart = parts.FirstOrDefault(part => part.StartsWith("v1=", StringComparison.Ordinal));
        if (timestampPart is null || valuePart is null ||
            !long.TryParse(timestampPart[2..], out var unixTimestamp) ||
            string.IsNullOrWhiteSpace(valuePart[3..]))
        {
            return false;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        var window = replayWindow == default ? TimeSpan.FromMinutes(5) : replayWindow;
        if (timestamp < nowUtc.Subtract(window) || timestamp > nowUtc.Add(window))
        {
            return false;
        }

        var expected = Create(secret, payloadJson, timestamp)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.StartsWith("v1=", StringComparison.Ordinal))[3..];
        var actual = valuePart[3..];
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static IReadOnlyDictionary<string, string> CreateHeaders(
        string secret,
        string payloadJson,
        DateTimeOffset timestampUtc) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HeaderName] = Create(secret, payloadJson, timestampUtc),
            [TimestampHeaderName] = timestampUtc.ToUnixTimeSeconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["Content-Type"] = "application/json"
        };

    public static IReadOnlyDictionary<string, string> CreateHeaders(
        string secret,
        string payloadJson,
        DateTimeOffset timestampUtc,
        Guid eventId,
        long deliveryId,
        string eventType,
        int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deliveryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        var headers = new Dictionary<string, string>(
            CreateHeaders(secret, payloadJson, timestampUtc),
            StringComparer.OrdinalIgnoreCase)
        {
            [EventIdHeaderName] = eventId.ToString("D"),
            [DeliveryIdHeaderName] = deliveryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [EventTypeHeaderName] = eventType,
            [VersionHeaderName] = version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return headers;
    }
}

public static class IntegrationPayload
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(object payload) =>
        JsonSerializer.Serialize(payload, Options);
}
