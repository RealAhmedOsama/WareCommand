using Wms.Application.Common;

namespace Wms.Application.Retention;

public enum RetentionClass
{
    ImmutableOperationalHistory = 1,
    SecurityAudit = 2,
    Documents = 3,
    IntegrationPayloads = 4,
    Notifications = 5,
    Idempotency = 6,
    GeneratedReports = 7,
    Telemetry = 8,
    Temporary = 9
}

public enum RetentionRunStatus
{
    Preview = 1,
    Running = 2,
    Succeeded = 3,
    Cancelled = 4,
    Failed = 5
}

public sealed record RetentionClassDescriptor(
    RetentionClass Class,
    string Key,
    int? DefaultRetentionDays,
    int? MinimumRetentionDays,
    bool IsPurgeAllowed,
    bool IsImplemented,
    string Description);

public static class RetentionCatalog
{
    public static IReadOnlyList<RetentionClassDescriptor> Classes { get; } =
    [
        new(
            RetentionClass.ImmutableOperationalHistory,
            "immutable-operational-history",
            null,
            null,
            false,
            true,
            "Inventory ledger, lot/serial history, movements, reservations, and other traceability facts are immutable."),
        new(
            RetentionClass.SecurityAudit,
            "security-audit",
            null,
            null,
            false,
            true,
            "Authentication and audit history is append-only and is never purged by retention jobs."),
        new(
            RetentionClass.Documents,
            "documents",
            3_650,
            365,
            true,
            true,
            "Attachment metadata and bytes explicitly marked pending deletion after their retention boundary."),
        new(
            RetentionClass.IntegrationPayloads,
            "integration-payloads",
            365,
            30,
            true,
            false,
            "Integration payload retention is registered but waits for the integration payload store."),
        new(
            RetentionClass.Notifications,
            "notifications",
            365,
            30,
            true,
            true,
            "Resolved or acknowledged non-active notifications and their recipient rows."),
        new(
            RetentionClass.Idempotency,
            "idempotency",
            90,
            7,
            true,
            true,
            "Completed, failed, or expired inventory command replay records; in-progress records are protected."),
        new(
            RetentionClass.GeneratedReports,
            "generated-reports",
            365,
            30,
            true,
            false,
            "Generated report retention is registered but waits for the report artifact store."),
        new(
            RetentionClass.Telemetry,
            "telemetry",
            180,
            30,
            true,
            false,
            "Telemetry retention is registered but waits for the configured telemetry sink."),
        new(
            RetentionClass.Temporary,
            "temporary",
            30,
            1,
            true,
            true,
            "Temporary job execution and resolved job notification records."),
    ];

    public static RetentionClassDescriptor Get(RetentionClass retentionClass) =>
        Classes.SingleOrDefault(item => item.Class == retentionClass)
        ?? throw new ArgumentOutOfRangeException(nameof(retentionClass));

    public static bool TryGet(string? key, out RetentionClassDescriptor descriptor)
    {
        descriptor = Classes.FirstOrDefault(item =>
            string.Equals(item.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return descriptor is not null;
    }
}

public sealed record RetentionPolicyInput(
    RetentionClass Class,
    int RetentionDays,
    int? WarehouseId = null,
    string? CompanyCode = null,
    bool ArchiveBeforePurge = true,
    bool ExportBeforePurge = true,
    bool Enabled = true);

public sealed record RetentionPolicyDto(
    long? Id,
    RetentionClass Class,
    string Key,
    int RetentionDays,
    int? MinimumRetentionDays,
    int? WarehouseId,
    string? CompanyCode,
    bool ArchiveBeforePurge,
    bool ExportBeforePurge,
    bool Enabled,
    bool IsConfigured,
    bool IsPurgeAllowed,
    bool IsImplemented,
    DateTimeOffset? UpdatedAtUtc);

public sealed record RetentionHoldInput(
    RetentionClass Class,
    string TargetType,
    string TargetId,
    string Reason,
    int? WarehouseId = null,
    string? CaseReference = null);

public sealed record RetentionHoldDto(
    long Id,
    RetentionClass Class,
    string TargetType,
    string TargetId,
    string Reason,
    int? WarehouseId,
    string? CaseReference,
    string CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleasedByUserId,
    bool IsActive);

public sealed record RetentionPreviewInput(
    DateTimeOffset? AsOfUtc = null,
    int? WarehouseId = null,
    int BatchSize = 100,
    string? CompanyCode = null);

public sealed record RetentionRunInput(
    bool DryRun = true,
    bool AllowDestructive = false,
    bool BackupVerified = false,
    string? AuthorizationReference = null,
    DateTimeOffset? AsOfUtc = null,
    int? WarehouseId = null,
    int BatchSize = 100,
    Guid? RunId = null,
    Guid? PreviewRunId = null,
    string? CompanyCode = null);

public sealed record RetentionClassCountDto(
    RetentionClass Class,
    string Key,
    bool IsImplemented,
    bool IsPurgeAllowed,
    long Examined,
    long Eligible,
    long Held,
    long Skipped);

public sealed record RetentionRunDto(
    Guid RunId,
    RetentionRunStatus Status,
    bool DryRun,
    bool AllowDestructive,
    bool BackupVerified,
    string? AuthorizationReference,
    DateTimeOffset AsOfUtc,
    int? WarehouseId,
    string? CompanyCode,
    int BatchSize,
    long ItemsExamined,
    long EligibleItems,
    long HeldItems,
    long SkippedItems,
    long ArchivedItems,
    long PurgedItems,
    int BatchCount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError,
    IReadOnlyList<RetentionClassCountDto> Counts);

public interface IRetentionService
{
    Task<Result<IReadOnlyList<RetentionClassDescriptor>>> GetCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RetentionPolicyDto>>> ListPoliciesAsync(
        int? warehouseId = null,
        string? companyCode = null,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionPolicyDto>> SavePolicyAsync(
        RetentionPolicyInput input,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RetentionHoldDto>>> ListHoldsAsync(
        bool includeReleased = false,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionHoldDto>> CreateHoldAsync(
        RetentionHoldInput input,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionHoldDto>> ReleaseHoldAsync(
        long holdId,
        string? releaseReason = null,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionRunDto>> PreviewAsync(
        RetentionPreviewInput input,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionRunDto>> RunAsync(
        RetentionRunInput input,
        CancellationToken cancellationToken = default);

    Task<Result<RetentionRunDto>> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
