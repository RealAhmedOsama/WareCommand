namespace Wms.DataMigration;

public sealed record MigrationOptions(
    string SourcePath,
    string TargetConnectionString,
    bool Apply,
    bool AllowExistingTarget,
    string? BackupDirectory,
    string? ReportPath)
{
    public bool DryRun => !Apply;
}
