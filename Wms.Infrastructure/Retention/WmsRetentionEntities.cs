using Wms.Application.Retention;

namespace Wms.Infrastructure.Retention;

public sealed class WmsRetentionPolicyEntity
{
    public long Id { get; set; }

    public RetentionClass Class { get; set; }

    public string PolicyKey { get; set; } = string.Empty;

    public int RetentionDays { get; set; }

    public int? MinimumRetentionDays { get; set; }

    public int? WarehouseId { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public bool ArchiveBeforePurge { get; set; }

    public bool ExportBeforePurge { get; set; }

    public bool Enabled { get; set; }

    public int Revision { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class WmsRetentionHoldEntity
{
    public long Id { get; set; }

    public RetentionClass Class { get; set; }

    public string TargetType { get; set; } = string.Empty;

    public string TargetId { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? CaseReference { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? ReleasedAtUtc { get; set; }

    public string? ReleasedByUserId { get; set; }

    public string? ReleaseReason { get; set; }
}

public sealed class WmsRetentionRunEntity
{
    public Guid RunId { get; set; }

    public RetentionRunStatus Status { get; set; }

    public bool DryRun { get; set; }

    public bool AllowDestructive { get; set; }

    public bool BackupVerified { get; set; }

    public string? AuthorizationReference { get; set; }

    public DateTimeOffset AsOfUtc { get; set; }

    public int? WarehouseId { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public int BatchSize { get; set; }

    public long ItemsExamined { get; set; }

    public long EligibleItems { get; set; }

    public long HeldItems { get; set; }

    public long SkippedItems { get; set; }

    public long ArchivedItems { get; set; }

    public long PurgedItems { get; set; }

    public int BatchCount { get; set; }

    public string? CursorClass { get; set; }

    public string? CursorId { get; set; }

    public string? RequestedByUserId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? LastError { get; set; }

    public ICollection<WmsRetentionRunCountEntity> Counts { get; set; } =
        new List<WmsRetentionRunCountEntity>();
}

public sealed class WmsRetentionRunCountEntity
{
    public Guid RunId { get; set; }

    public RetentionClass Class { get; set; }

    public bool IsImplemented { get; set; }

    public bool IsPurgeAllowed { get; set; }

    public long Examined { get; set; }

    public long Eligible { get; set; }

    public long Held { get; set; }

    public long Skipped { get; set; }

    public WmsRetentionRunEntity Run { get; set; } = null!;
}

public sealed class WmsRetentionArchiveReferenceEntity
{
    public long Id { get; set; }

    public RetentionClass Class { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }

    public string ArchiveLocator { get; set; } = string.Empty;

    public string? SourceHash { get; set; }

    public string? MetadataJson { get; set; }

    public Guid RunId { get; set; }

    public DateTimeOffset ArchivedAtUtc { get; set; }

    public DateTimeOffset? PurgedAtUtc { get; set; }
}
