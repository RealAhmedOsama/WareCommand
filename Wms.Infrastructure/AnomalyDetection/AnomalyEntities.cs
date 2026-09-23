namespace Wms.Infrastructure.AnomalyDetection;

public sealed class AnomalyRuleConfigurationEntity
{
    public int Id { get; set; }
    public string RuleKind { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public decimal Threshold { get; set; }
    public bool IsEnabled { get; set; }
    public bool UseExternalNotifications { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public Wms.Domain.Entities.Warehouse? Warehouse { get; set; }
}

public sealed class AnomalyDetectionRunEntity
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public DateTimeOffset SourceWindowFromUtc { get; set; }
    public DateTimeOffset SourceWindowToUtc { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public string RuleVersionsJson { get; set; } = "{}";
    public string DataQualityFlagsJson { get; set; } = "[]";
    public int SourceSignals { get; set; }
    public int FindingsCreated { get; set; }
    public int FindingsReused { get; set; }
    public string? StartedByUserId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }

    public Wms.Domain.Entities.Warehouse Warehouse { get; set; } = null!;
    public ICollection<AnomalyFindingObservationEntity> Observations { get; set; } =
        new List<AnomalyFindingObservationEntity>();
}

public sealed class AnomalyFindingEntity
{
    public int Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string RuleKind { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public int FirstDetectionRunId { get; set; }
    public DateTimeOffset SourceWindowFromUtc { get; set; }
    public DateTimeOffset SourceWindowToUtc { get; set; }
    public decimal ObservedValue { get; set; }
    public decimal ExpectedValue { get; set; }
    public decimal Threshold { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTimeOffset ObservedAtUtc { get; set; }
    public DateTimeOffset FirstDetectedAtUtc { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? AssignedTeamCode { get; set; }
    public string? AssignedByUserId { get; set; }
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public DateTimeOffset? SuppressionExpiresAtUtc { get; set; }
    public string? SuppressedFromStatus { get; set; }
    public long Revision { get; set; }

    public Wms.Domain.Entities.Warehouse Warehouse { get; set; } = null!;
    public AnomalyDetectionRunEntity FirstDetectionRun { get; set; } = null!;
    public ICollection<AnomalyFindingObservationEntity> Observations { get; set; } =
        new List<AnomalyFindingObservationEntity>();
    public ICollection<AnomalyFindingHistoryEntity> History { get; set; } =
        new List<AnomalyFindingHistoryEntity>();
}

public sealed class AnomalyFindingObservationEntity
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public int FindingId { get; set; }
    public DateTimeOffset SourceWindowFromUtc { get; set; }
    public DateTimeOffset SourceWindowToUtc { get; set; }
    public bool SourcePresent { get; set; }
    public bool IsAnomalous { get; set; }
    public decimal? ObservedValue { get; set; }
    public decimal? ExpectedValue { get; set; }
    public decimal? Threshold { get; set; }
    public string? Explanation { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }

    public AnomalyDetectionRunEntity Run { get; set; } = null!;
    public AnomalyFindingEntity Finding { get; set; } = null!;
}

public sealed class AnomalyFindingHistoryEntity
{
    public int Id { get; set; }
    public int FindingId { get; set; }
    public int Sequence { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? AssignedToUserId { get; set; }
    public string? AssignedTeamCode { get; set; }
    public string? Comment { get; set; }
    public string EvidenceReferencesJson { get; set; } = "[]";
    public DateTimeOffset? SuppressionExpiresAtUtc { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public AnomalyFindingEntity Finding { get; set; } = null!;
}
