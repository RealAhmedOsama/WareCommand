namespace Wms.Infrastructure.Integrations;

public sealed class WmsIntegrationOutboxEntity
{
    public long Id { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int Version { get; set; }

    public string AggregateType { get; set; } = string.Empty;

    public string AggregateKey { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? CausationId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public DateTimeOffset? DeliveredAtUtc { get; set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; set; }

    public string? LastError { get; set; }

    public ICollection<WmsWebhookDeliveryEntity> Deliveries { get; set; } =
        new List<WmsWebhookDeliveryEntity>();
}

public sealed class WmsIntegrationInboxEntity
{
    public long Id { get; set; }

    public string SourceSystem { get; set; } = string.Empty;

    public string ExternalMessageId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string PayloadHash { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTimeOffset ReceivedAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public string? LastError { get; set; }
}

public sealed class WmsWebhookSubscriptionEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string EndpointUrl { get; set; } = string.Empty;

    public string EventTypesJson { get; set; } = "[]";

    public string WarehouseIdsJson { get; set; } = "[]";

    public string SecretCiphertext { get; set; } = string.Empty;

    public int SecretVersion { get; set; }

    public string? PreviousSecretCiphertext { get; set; }

    public int? PreviousSecretVersion { get; set; }

    public DateTimeOffset? PreviousSecretValidUntilUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public int MaximumAttempts { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastDeliveryAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }

    public ICollection<WmsWebhookDeliveryEntity> Deliveries { get; set; } =
        new List<WmsWebhookDeliveryEntity>();
}

public sealed class WmsWebhookDeliveryEntity
{
    public long Id { get; set; }

    public long OutboxMessageId { get; set; }

    public long SubscriptionId { get; set; }

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }

    public DateTimeOffset? LeaseUntilUtc { get; set; }

    public DateTimeOffset? DeliveredAtUtc { get; set; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public string? LastError { get; set; }

    public WmsIntegrationOutboxEntity OutboxMessage { get; set; } = null!;

    public WmsWebhookSubscriptionEntity Subscription { get; set; } = null!;
}
