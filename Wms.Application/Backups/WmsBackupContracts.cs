namespace Wms.Application.Backups;

public sealed record WmsBackupOptions(
    bool Enabled,
    string RootPath,
    string? OffsitePath,
    string EncryptionKeyFile,
    int RetentionDays,
    int MinimumRetainedBackups,
    int MaximumAgeHours,
    string? DataProtectionKeysPath,
    string? AssetsPath,
    string PgDumpExecutable = "pg_dump",
    string PgRestoreExecutable = "pg_restore");

public sealed record WmsBackupDatabaseSummary(
    long Items,
    long Warehouses,
    long Locations,
    long Lots,
    long StockRows,
    long Movements,
    decimal QuantityAvailable,
    decimal QuantityReserved,
    decimal MovementQuantity);

public sealed record WmsBackupFileEntry(
    string RelativePath,
    long Length,
    string Sha256);

public sealed record WmsBackupManifest(
    string FormatVersion,
    DateTimeOffset CreatedAtUtc,
    string DatabaseName,
    string? SourceRevision,
    WmsBackupDatabaseSummary DatabaseSummary,
    IReadOnlyList<WmsBackupFileEntry> Files,
    bool IncludesDataProtectionKeys,
    bool IncludesAssets);

public sealed record WmsBackupArtifact(
    string ArtifactPath,
    string? OffsiteArtifactPath,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    string Sha256,
    WmsBackupManifest Manifest);

public sealed record WmsBackupVerification(
    bool IsValid,
    string ArtifactPath,
    string? FailureReason,
    WmsBackupManifest? Manifest);

public sealed record WmsBackupPruneResult(
    int RetainedCount,
    int RemovedCount,
    IReadOnlyList<string> RemovedArtifacts);

public sealed record WmsBackupRestoreResult(
    WmsBackupManifest Manifest,
    WmsBackupDatabaseSummary RestoredSummary,
    string TargetDatabase);

public interface IWmsBackupService
{
    Task<WmsBackupArtifact> CreateAsync(
        string? sourceRevision = null,
        CancellationToken cancellationToken = default);

    Task<WmsBackupVerification> VerifyAsync(
        string artifactPath,
        CancellationToken cancellationToken = default);

    Task<WmsBackupPruneResult> PruneAsync(
        CancellationToken cancellationToken = default);

    Task<WmsBackupRestoreResult> RestoreAsync(
        string artifactPath,
        string targetConnectionString,
        bool allowNonEmptyTarget,
        CancellationToken cancellationToken = default);
}
