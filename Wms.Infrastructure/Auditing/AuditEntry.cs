namespace Wms.Infrastructure.Auditing;

/// <summary>
/// Append-only audit record. Application code may create entries through
/// IAuditWriter, but there are no update/delete operations for this model.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public long OccurredAtUnixMilliseconds { get; private set; }

    public string? ActorUserId { get; private set; }

    public string? ActorUserName { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string? EntityId { get; private set; }

    public int? WarehouseId { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string SourceClient { get; private set; } = string.Empty;

    public string? RemoteIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Details { get; private set; }

    public string? BeforeJson { get; private set; }

    public string? AfterJson { get; private set; }

    internal static AuditEntry Create(
        DateTimeOffset occurredAtUtc,
        string? actorUserId,
        string? actorUserName,
        string action,
        string entityType,
        string? entityId,
        int? warehouseId,
        string correlationId,
        string sourceClient,
        string? remoteIpAddress,
        string? userAgent,
        bool succeeded,
        string? details,
        string? beforeJson,
        string? afterJson)
    {
        return new AuditEntry
        {
            OccurredAtUtc = occurredAtUtc,
            OccurredAtUnixMilliseconds = occurredAtUtc.ToUnixTimeMilliseconds(),
            ActorUserId = actorUserId,
            ActorUserName = actorUserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            WarehouseId = warehouseId,
            CorrelationId = correlationId,
            SourceClient = sourceClient,
            RemoteIpAddress = remoteIpAddress,
            UserAgent = userAgent,
            Succeeded = succeeded,
            Details = details,
            BeforeJson = beforeJson,
            AfterJson = afterJson
        };
    }
}
