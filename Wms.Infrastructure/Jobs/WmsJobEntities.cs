namespace Wms.Infrastructure.Jobs;

public sealed class WmsJobExecutionEntity
{
    public long Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Queue { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? ActorUserId { get; set; }

    public string? ActorUserName { get; set; }

    public int? WarehouseId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? LastErrorType { get; set; }

    public string? LastErrorMessage { get; set; }

    public string? ResultSummary { get; set; }
}

public sealed class WmsJobNotificationEntity
{
    public long Id { get; set; }

    public string DeduplicationKey { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string JobName { get; set; } = string.Empty;

    public string JobIdempotencyKey { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }
}
