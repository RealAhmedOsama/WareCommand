namespace Wms.Infrastructure.Connectors;

public sealed class WmsConnectorMappingProfileEntity
{
    public long Id { get; set; }

    public string ConnectorType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public string ExternalIdField { get; set; } = string.Empty;

    public string RulesJson { get; set; } = "[]";

    public string CultureName { get; set; } = "en-US";

    public string ConflictPolicy { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<WmsConnectorInstanceEntity> ConnectorInstances { get; set; } =
        new List<WmsConnectorInstanceEntity>();
}

public sealed class WmsConnectorInstanceEntity
{
    public long Id { get; set; }

    public string ConnectorType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string AllowedWarehouseIdsJson { get; set; } = "[]";

    public string CredentialReference { get; set; } = string.Empty;

    public int CredentialVersion { get; set; }

    public string ModesJson { get; set; } = "[]";

    public string? Schedule { get; set; }

    public string MappingProfileName { get; set; } = string.Empty;

    public int MappingProfileVersion { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Cursor { get; set; } = string.Empty;

    public string HealthStatus { get; set; } = string.Empty;

    public string? HealthSummary { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastRunAtUtc { get; set; }

    public DateTimeOffset? LastSuccessfulRunAtUtc { get; set; }

    public DateTimeOffset? LastHealthCheckAtUtc { get; set; }

    public int ConsecutiveFailureCount { get; set; }

    public WmsConnectorMappingProfileEntity? MappingProfile { get; set; }

    public ICollection<WmsConnectorRunEntity> Runs { get; set; } =
        new List<WmsConnectorRunEntity>();

    public ICollection<WmsConnectorExternalRecordEntity> ExternalRecords { get; set; } =
        new List<WmsConnectorExternalRecordEntity>();
}

public sealed class WmsConnectorRunEntity
{
    public long Id { get; set; }

    public long ConnectorInstanceId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? CursorBefore { get; set; }

    public string? CursorAfter { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public int RecordsSeen { get; set; }

    public int RecordsCreated { get; set; }

    public int RecordsUpdated { get; set; }

    public int RecordsSkipped { get; set; }

    public int Conflicts { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public WmsConnectorInstanceEntity ConnectorInstance { get; set; } = null!;
}

public sealed class WmsConnectorExternalRecordEntity
{
    public long Id { get; set; }

    public long ConnectorInstanceId { get; set; }

    public string ExternalKey { get; set; } = string.Empty;

    public string RecordType { get; set; } = string.Empty;

    public string ExternalId { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public string PayloadHash { get; set; } = string.Empty;

    public string? ExternalVersion { get; set; }

    public string Status { get; set; } = string.Empty;

    public long LastRunId { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public WmsConnectorInstanceEntity ConnectorInstance { get; set; } = null!;
}
