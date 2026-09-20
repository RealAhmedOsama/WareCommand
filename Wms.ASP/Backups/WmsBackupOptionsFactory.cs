using Wms.Application.Backups;

namespace Wms.ASP.Backups;

public static class WmsBackupOptionsFactory
{
    public static WmsBackupOptions From(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var storagePath = configuration["Wms:Health:StoragePath"] ??
            Path.Combine(environment.ContentRootPath, "data");
        var rootPath = ResolvePath(
            configuration["Wms:Backups:RootPath"],
            Path.Combine(storagePath, "backups"),
            environment.ContentRootPath);
        var dataProtectionPath = ResolveOptionalPath(
            configuration["DataProtection:KeyDirectory"],
            environment.ContentRootPath);
        var assetsPath = ResolveOptionalPath(
            configuration["Wms:Backups:AssetsPath"],
            environment.ContentRootPath);
        var configuredKeyFile = configuration["Wms:Backups:EncryptionKeyFile"];
        var keyFile = string.IsNullOrWhiteSpace(configuredKeyFile)
            ? Path.Combine(rootPath, "backup-encryption.key")
            : ResolvePath(configuredKeyFile, configuredKeyFile, environment.ContentRootPath);
        var retentionDays = Math.Clamp(
            configuration.GetValue("Wms:Backups:RetentionDays", 30),
            1,
            3_650);
        var minimumRetained = Math.Clamp(
            configuration.GetValue("Wms:Backups:MinimumRetainedBackups", 2),
            2,
            1_000);
        var maximumAgeHours = Math.Clamp(
            configuration.GetValue("Wms:Backups:MaximumAgeHours", 36),
            1,
            8_760);

        return new WmsBackupOptions(
            configuration.GetValue("Wms:Backups:Enabled", false),
            rootPath,
            ResolveOptionalPath(
                configuration["Wms:Backups:OffsitePath"],
                environment.ContentRootPath),
            keyFile,
            retentionDays,
            minimumRetained,
            maximumAgeHours,
            dataProtectionPath,
            assetsPath,
            configuration["Wms:Backups:PgDumpExecutable"] ?? "pg_dump",
            configuration["Wms:Backups:PgRestoreExecutable"] ?? "pg_restore");
    }

    private static string ResolvePath(
        string? configured,
        string fallback,
        string contentRoot)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(contentRoot, value));
    }

    private static string? ResolveOptionalPath(string? configured, string contentRoot) =>
        string.IsNullOrWhiteSpace(configured)
            ? null
            : ResolvePath(configured, configured, contentRoot);
}
