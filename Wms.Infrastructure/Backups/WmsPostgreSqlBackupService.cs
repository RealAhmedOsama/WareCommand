using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wms.Application.Backups;
using Wms.Application.Telemetry;

namespace Wms.Infrastructure.Backups;

public sealed class WmsPostgreSqlBackupService(
    WmsBackupOptions options,
    string connectionString,
    WmsBackupHealthState healthState,
    ILogger<WmsPostgreSqlBackupService> logger) : IWmsBackupService
{
    private const string ArtifactExtension = ".wcbak";
    private const string StatusFileName = "backup-status.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<WmsBackupArtifact> CreateAsync(
        string? sourceRevision = null,
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var createdAtUtc = DateTimeOffset.UtcNow;
        var stagingId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(
            options.RootPath,
            ".staging",
            stagingId);
        var dumpPath = Path.Combine(stagingDirectory, "database.dump");
        var zipPath = Path.Combine(
            options.RootPath,
            ".staging",
            stagingId + ".zip");
        var artifactPath = Path.Combine(
            options.RootPath,
            $"warecommand-postgresql-{createdAtUtc:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ArtifactExtension}");
        var completed = false;

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            Directory.CreateDirectory(options.RootPath);
            var databaseSummary = await ReadSummaryAsync(connectionString, cancellationToken);
            await RunDumpAsync(dumpPath, cancellationToken);
            var includesKeys = CopyOptionalDirectory(
                options.DataProtectionKeysPath,
                Path.Combine(stagingDirectory, "data-protection-keys"));
            var includesAssets = CopyOptionalDirectory(
                options.AssetsPath,
                Path.Combine(stagingDirectory, "assets"));

            var files = await BuildFileEntriesAsync(stagingDirectory, cancellationToken);
            var manifest = new WmsBackupManifest(
                "1",
                createdAtUtc,
                RequireDatabaseName(new NpgsqlConnectionStringBuilder(connectionString)),
                NormalizeRevision(sourceRevision),
                databaseSummary,
                files,
                includesKeys,
                includesAssets);
            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, JsonOptions),
                cancellationToken);
            await ZipFile.CreateFromDirectoryAsync(
                stagingDirectory,
                zipPath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false,
                cancellationToken);

            var key = WmsBackupArchive.LoadKey(options.EncryptionKeyFile);
            await WmsBackupArchive.EncryptAsync(zipPath, artifactPath, key, cancellationToken);
            var sha256 = await ComputeSha256Async(artifactPath, cancellationToken);
            await WriteChecksumAsync(artifactPath, sha256, cancellationToken);
            var offsiteArtifactPath = await ReplicateOffsiteAsync(
                artifactPath,
                sha256,
                cancellationToken);
            var localVerification = await VerifyAsync(artifactPath, cancellationToken);
            if (!localVerification.IsValid)
            {
                throw new InvalidOperationException(
                    "The newly created backup failed post-create verification.");
            }

            if (offsiteArtifactPath is not null)
            {
                var offsiteVerification = await VerifyAsync(
                    offsiteArtifactPath,
                    cancellationToken);
                if (!offsiteVerification.IsValid)
                {
                    throw new InvalidOperationException(
                        "The off-server backup copy failed post-create verification.");
                }
            }

            var artifact = new WmsBackupArtifact(
                artifactPath,
                offsiteArtifactPath,
                createdAtUtc,
                new FileInfo(artifactPath).Length,
                sha256,
                manifest);
            healthState.MarkSuccess(createdAtUtc, Path.GetFileName(artifactPath), artifact.SizeBytes);
            WriteStatus("succeeded", artifact, failureReason: null);
            logger.LogInformation(
                "PostgreSQL backup succeeded with {SizeBytes} bytes and SHA-256 {Sha256}",
                artifact.SizeBytes,
                sha256);
            completed = true;
            return artifact;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            healthState.MarkFailure(DateTimeOffset.UtcNow);
            WmsTelemetry.RecordBackupFailure("postgresql");
            WriteStatus("failed", artifact: null, exception.GetType().Name);
            logger.LogError(
                "PostgreSQL backup failed with {ExceptionType}; sensitive provider details were suppressed",
                exception.GetType().Name);
            throw;
        }
        finally
        {
            DeleteDirectory(stagingDirectory);
            DeleteFile(zipPath);
            if (!completed)
            {
                DeleteFile(artifactPath);
                DeleteFile(artifactPath + ".sha256");
            }
        }
    }

    public async Task<WmsBackupVerification> VerifyAsync(
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var package = await ExtractPackageAsync(artifactPath, cancellationToken);
            try
            {
                await ValidateExtractedPackageAsync(package, cancellationToken);
                return new WmsBackupVerification(true, artifactPath, null, package.Manifest);
            }
            finally
            {
                DeleteDirectory(package.DirectoryPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new WmsBackupVerification(
                false,
                artifactPath,
                $"Verification failed with {exception.GetType().Name}.",
                null);
        }
    }

    public async Task<WmsBackupPruneResult> PruneAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var removed = new List<string>();
        var retainedCount = 0;
        foreach (var directory in BackupDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            var valid = new List<(string Path, WmsBackupManifest Manifest)>();
            foreach (var path in Directory.EnumerateFiles(directory, $"*{ArtifactExtension}"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var verification = await VerifyAsync(path, cancellationToken);
                if (verification.IsValid && verification.Manifest is not null)
                {
                    valid.Add((path, verification.Manifest));
                }
            }

            var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RetentionDays);
            var removedForDirectory = 0;
            foreach (var candidate in valid.OrderBy(item => item.Manifest.CreatedAtUtc))
            {
                if (valid.Count - removedForDirectory <= options.MinimumRetainedBackups ||
                    candidate.Manifest.CreatedAtUtc >= cutoff)
                {
                    continue;
                }

                File.Delete(candidate.Path);
                var checksumPath = candidate.Path + ".sha256";
                if (File.Exists(checksumPath))
                {
                    File.Delete(checksumPath);
                }

                removed.Add(candidate.Path);
                removedForDirectory++;
            }

            retainedCount += valid.Count - removedForDirectory;
        }

        return new WmsBackupPruneResult(retainedCount, removed.Count, removed);
    }

    public async Task<WmsBackupRestoreResult> RestoreAsync(
        string artifactPath,
        string targetConnectionString,
        bool allowNonEmptyTarget,
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var package = await ExtractPackageAsync(artifactPath, cancellationToken);
        try
        {
            await ValidateExtractedPackageAsync(package, cancellationToken);
            await using var targetConnection = new NpgsqlConnection(targetConnectionString);
            await targetConnection.OpenAsync(cancellationToken);
            if (!allowNonEmptyTarget && await HasUserObjectsAsync(targetConnection, cancellationToken))
            {
                throw new InvalidOperationException(
                    "The restore target is not empty. Use the explicit non-empty-target confirmation only in an approved restore window.");
            }

            var dumpPath = Path.Combine(package.DirectoryPath, "database.dump");
            var restoreArguments = new List<string>
            {
                "--exit-on-error",
                "--no-owner",
                "--no-privileges",
                "--single-transaction"
            };
            if (allowNonEmptyTarget)
            {
                restoreArguments.Add("--clean");
                restoreArguments.Add("--if-exists");
            }

            restoreArguments.Add("--dbname");
            restoreArguments.Add(RequireDatabaseName(new NpgsqlConnectionStringBuilder(targetConnectionString)));
            restoreArguments.Add(dumpPath);
            await RunToolAsync(options.PgRestoreExecutable, targetConnectionString, restoreArguments, cancellationToken);

            var restoredSummary = await ReadSummaryAsync(targetConnection, cancellationToken);
            EnsureSummaryMatches(package.Manifest.DatabaseSummary, restoredSummary);
            await EnsureReferencesAreValidAsync(targetConnection, cancellationToken);
            return new WmsBackupRestoreResult(
                package.Manifest,
                restoredSummary,
                RequireDatabaseName(new NpgsqlConnectionStringBuilder(targetConnectionString)));
        }
        finally
        {
            DeleteDirectory(package.DirectoryPath);
        }
    }

    private async Task<ExtractedPackage> ExtractPackageAsync(
        string artifactPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException("The backup artifact was not found.", artifactPath);
        }

        var key = WmsBackupArchive.LoadKey(options.EncryptionKeyFile);
        var directory = Path.Combine(
            Path.GetTempPath(),
            "warecommand-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var zipPath = await WmsBackupArchive.DecryptAsync(
                artifactPath,
                directory,
                key,
                cancellationToken);
            ValidateZipEntries(zipPath, directory);
            ZipFile.ExtractToDirectory(zipPath, directory, overwriteFiles: true);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonSerializer.Deserialize<WmsBackupManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken),
                JsonOptions) ?? throw new InvalidOperationException("The backup manifest is invalid.");
            return new ExtractedPackage(directory, manifest);
        }
        catch
        {
            DeleteDirectory(directory);
            throw;
        }
    }

    private async Task ValidateExtractedPackageAsync(
        ExtractedPackage package,
        CancellationToken cancellationToken)
    {
        if (package.Manifest.FormatVersion != "1")
        {
            throw new InvalidOperationException("The backup manifest version is not supported.");
        }

        var dumpPath = Path.Combine(package.DirectoryPath, "database.dump");
        if (!File.Exists(dumpPath))
        {
            throw new InvalidOperationException("The backup does not contain a database dump.");
        }

        foreach (var file in package.Manifest.Files)
        {
            var fullPath = SafeCombine(package.DirectoryPath, file.RelativePath);
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length != file.Length)
            {
                throw new InvalidOperationException("A backup manifest file is missing or has an unexpected size.");
            }

            var sha256 = await ComputeSha256Async(fullPath, cancellationToken);
            if (!string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A backup manifest file failed its SHA-256 check.");
            }
        }

        await RunToolAsync(
            options.PgRestoreExecutable,
            connectionString,
            ["--list", dumpPath],
            cancellationToken);
    }

    private async Task RunDumpAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        await RunToolAsync(
            options.PgDumpExecutable,
            connectionString,
            [
                "--format", "custom",
                "--compress", "6",
                "--no-owner",
                "--no-privileges",
                "--file", dumpPath,
                "--dbname", RequireDatabaseName(builder)
            ],
            cancellationToken);
    }

    private async Task<string?> ReplicateOffsiteAsync(
        string artifactPath,
        string sha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.OffsitePath) ||
            PathsEqual(options.OffsitePath, options.RootPath))
        {
            return null;
        }

        Directory.CreateDirectory(options.OffsitePath);
        var destination = Path.Combine(options.OffsitePath, Path.GetFileName(artifactPath));
        File.Copy(artifactPath, destination, overwrite: false);
        var destinationSha256 = await ComputeSha256Async(destination, cancellationToken);
        if (!string.Equals(destinationSha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidOperationException("The off-server backup copy failed its SHA-256 verification.");
        }

        var sourceChecksum = artifactPath + ".sha256";
        File.Copy(sourceChecksum, destination + ".sha256", overwrite: false);
        return destination;
    }

    private static async Task RunToolAsync(
        string executable,
        string toolConnectionString,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(toolConnectionString);
        var passFile = await CreatePassFileAsync(builder, cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["PGHOST"] = RequireHostName(builder);
            startInfo.Environment["PGPORT"] = builder.Port.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["PGUSER"] = RequireUsername(builder);
            startInfo.Environment["PGDATABASE"] = RequireDatabaseName(builder);
            startInfo.Environment["PGAPPNAME"] = "WareCommandBackup";
            startInfo.Environment["PGSSLMODE"] = ToPgSslMode(builder.SslMode);
            if (!string.IsNullOrWhiteSpace(builder.RootCertificate))
            {
                startInfo.Environment["PGSSLROOTCERT"] = builder.RootCertificate;
            }

            if (!string.IsNullOrWhiteSpace(builder.SslCertificate))
            {
                startInfo.Environment["PGSSLCERT"] = builder.SslCertificate;
            }

            if (!string.IsNullOrWhiteSpace(builder.SslKey))
            {
                startInfo.Environment["PGSSLKEY"] = builder.SslKey;
            }

            if (!string.IsNullOrWhiteSpace(passFile))
            {
                startInfo.Environment["PGPASSFILE"] = passFile;
            }

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Could not start the configured PostgreSQL tool '{executable}'.");
            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process already exited while cancellation was being observed.
                }

                throw;
            }

            await Task.WhenAll(standardOutput, standardError);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The configured PostgreSQL tool '{executable}' exited with code {process.ExitCode}.");
            }
        }
        finally
        {
            if (passFile is not null)
            {
                File.Delete(passFile);
            }
        }
    }

    private static async Task<string?> CreatePassFileAsync(
        NpgsqlConnectionStringBuilder builder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(builder.Password))
        {
            return null;
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            "warecommand-pgpass-" + Guid.NewGuid().ToString("N"));
        var content = string.Join(
            ":",
            EscapePgPass(RequireHostName(builder)),
            builder.Port.ToString(CultureInfo.InvariantCulture),
            EscapePgPass(RequireDatabaseName(builder)),
            EscapePgPass(RequireUsername(builder)),
            EscapePgPass(builder.Password ?? string.Empty)) + Environment.NewLine;
        await File.WriteAllTextAsync(path, content, cancellationToken);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (PlatformNotSupportedException)
        {
            // Windows uses the private temporary directory and inherited ACLs.
        }

        return path;
    }

    private static async Task<WmsBackupDatabaseSummary> ReadSummaryAsync(
        string connection,
        CancellationToken cancellationToken)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(cancellationToken);
        return await ReadSummaryAsync(database, cancellationToken);
    }

    private static async Task<WmsBackupDatabaseSummary> ReadSummaryAsync(
        NpgsqlConnection database,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM "Items"),
                (SELECT COUNT(*) FROM "Warehouses"),
                (SELECT COUNT(*) FROM "Locations"),
                (SELECT COUNT(*) FROM "Lots"),
                (SELECT COUNT(*) FROM "Stock"),
                (SELECT COUNT(*) FROM "Movements"),
                COALESCE((SELECT SUM("QuantityAvailable") FROM "Stock"), 0),
                COALESCE((SELECT SUM("QuantityReserved") FROM "Stock"), 0),
                COALESCE((SELECT SUM("Quantity") FROM "Movements"), 0);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The backup summary query returned no row.");
        }

        return new WmsBackupDatabaseSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8));
    }

    private static async Task<bool> HasUserObjectsAsync(
        NpgsqlConnection database,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM pg_class AS relation
                INNER JOIN pg_namespace AS schema_name ON schema_name.oid = relation.relnamespace
                WHERE schema_name.nspname NOT IN ('pg_catalog', 'information_schema')
                  AND relation.relkind IN ('r', 'p', 'v', 'm', 'S'));
            """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task EnsureReferencesAreValidAsync(
        NpgsqlConnection database,
        CancellationToken cancellationToken)
    {
        await using var command = database.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM "Locations" AS child LEFT JOIN "Warehouses" AS parent ON parent."Id" = child."WarehouseId" WHERE parent."Id" IS NULL),
                (SELECT COUNT(*) FROM "Locations" AS child LEFT JOIN "Locations" AS parent ON parent."Id" = child."ParentLocationId" WHERE child."ParentLocationId" IS NOT NULL AND parent."Id" IS NULL),
                (SELECT COUNT(*) FROM "Lots" AS lot LEFT JOIN "Items" AS item ON item."Id" = lot."ItemId" WHERE item."Id" IS NULL),
                (SELECT COUNT(*) FROM "ItemBarcodes" AS barcode LEFT JOIN "Items" AS item ON item."Id" = barcode."ItemId" WHERE item."Id" IS NULL),
                (SELECT COUNT(*) FROM "Movements" AS movement LEFT JOIN "Items" AS item ON item."Id" = movement."ItemId" WHERE item."Id" IS NULL),
                (SELECT COUNT(*) FROM "Movements" AS movement LEFT JOIN "Locations" AS location ON location."Id" = movement."FromLocationId" WHERE movement."FromLocationId" IS NOT NULL AND location."Id" IS NULL),
                (SELECT COUNT(*) FROM "Movements" AS movement LEFT JOIN "Locations" AS location ON location."Id" = movement."ToLocationId" WHERE movement."ToLocationId" IS NOT NULL AND location."Id" IS NULL),
                (SELECT COUNT(*) FROM "Movements" AS movement LEFT JOIN "Lots" AS lot ON lot."Id" = movement."LotId" WHERE movement."LotId" IS NOT NULL AND lot."Id" IS NULL),
                (SELECT COUNT(*) FROM "Stock" AS stock LEFT JOIN "Items" AS item ON item."Id" = stock."ItemId" WHERE item."Id" IS NULL),
                (SELECT COUNT(*) FROM "Stock" AS stock LEFT JOIN "Locations" AS location ON location."Id" = stock."LocationId" WHERE location."Id" IS NULL),
                (SELECT COUNT(*) FROM "Stock" AS stock LEFT JOIN "Lots" AS lot ON lot."Id" = stock."LotId" WHERE stock."LotId" IS NOT NULL AND lot."Id" IS NULL);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            Enumerable.Range(0, 11).Any(index => reader.GetInt64(index) != 0))
        {
            throw new InvalidOperationException("The restored database contains invalid critical references.");
        }
    }

    private static void EnsureSummaryMatches(
        WmsBackupDatabaseSummary expected,
        WmsBackupDatabaseSummary actual)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException("The restored database summary does not match the backup manifest.");
        }
    }

    private static async Task<IReadOnlyList<WmsBackupFileEntry>> BuildFileEntriesAsync(
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var entries = new List<WmsBackupFileEntry>();
        foreach (var path in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingDirectory, path).Replace('\\', '/');
            entries.Add(new WmsBackupFileEntry(
                relative,
                new FileInfo(path).Length,
                await ComputeSha256Async(path, cancellationToken)));
        }

        return entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static bool CopyOptionalDirectory(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            return false;
        }

        CopyDirectory(source, destination);
        return true;
    }

    private static void CopyDirectory(string source, string destination)
    {
        EnsureNotReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            EnsureNotReparsePoint(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            EnsureNotReparsePoint(directory);
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "Backup source directories and files must not be symbolic links or reparse points.");
        }
    }

    private static void ValidateZipEntries(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The backup archive contains an unsafe path.");
            }
        }
    }

    private static async Task WriteChecksumAsync(
        string artifactPath,
        string sha256,
        CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(
            artifactPath + ".sha256",
            $"{sha256} *{Path.GetFileName(artifactPath)}{Environment.NewLine}",
            cancellationToken);

    private void WriteStatus(
        string status,
        WmsBackupArtifact? artifact,
        string? failureReason)
    {
        try
        {
            Directory.CreateDirectory(options.RootPath);
            var statusPath = Path.Combine(options.RootPath, StatusFileName);
            var temporaryPath = statusPath + ".tmp";
            var payload = new
            {
                status,
                updatedAtUtc = DateTimeOffset.UtcNow,
                artifactPath = artifact?.ArtifactPath,
                offsiteArtifactPath = artifact?.OffsiteArtifactPath,
                artifactSizeBytes = artifact?.SizeBytes,
                sha256 = artifact?.Sha256,
                failureReason
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporaryPath, statusPath, overwrite: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Could not persist backup status with {ExceptionType}",
                exception.GetType().Name);
        }
    }

    private IEnumerable<string> BackupDirectories()
    {
        yield return options.RootPath;
        if (!string.IsNullOrWhiteSpace(options.OffsitePath) &&
            !PathsEqual(options.OffsitePath, options.RootPath))
        {
            yield return options.OffsitePath;
        }
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException(
                "Automated backups are disabled. Set Wms:Backups:Enabled=true in an approved PostgreSQL host.");
        }
    }

    private static string? NormalizeRevision(string? revision) =>
        string.IsNullOrWhiteSpace(revision)
            ? null
            : revision.Trim().Length <= 200
                ? revision.Trim()
                : revision.Trim()[..200];

    private static string EscapePgPass(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal);

    private static string ToPgSslMode(Npgsql.SslMode sslMode) =>
        sslMode switch
        {
            Npgsql.SslMode.Disable => "disable",
            Npgsql.SslMode.Allow => "allow",
            Npgsql.SslMode.Prefer => "prefer",
            Npgsql.SslMode.Require => "require",
            Npgsql.SslMode.VerifyCA => "verify-ca",
            Npgsql.SslMode.VerifyFull => "verify-full",
            _ => "prefer"
        };

    private static string RequireDatabaseName(NpgsqlConnectionStringBuilder builder) =>
        string.IsNullOrWhiteSpace(builder.Database)
            ? throw new InvalidOperationException("The PostgreSQL connection string has no database name.")
            : builder.Database;

    private static string RequireHostName(NpgsqlConnectionStringBuilder builder) =>
        string.IsNullOrWhiteSpace(builder.Host)
            ? throw new InvalidOperationException("The PostgreSQL connection string has no host name.")
            : builder.Host;

    private static string RequireUsername(NpgsqlConnectionStringBuilder builder) =>
        string.IsNullOrWhiteSpace(builder.Username)
            ? throw new InvalidOperationException("The PostgreSQL connection string has no username.")
            : builder.Username;

    private static string SafeCombine(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : throw new InvalidOperationException("The backup manifest contains an unsafe path.");
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record ExtractedPackage(
        string DirectoryPath,
        WmsBackupManifest Manifest);
}

public sealed class WmsDisabledBackupService : IWmsBackupService
{
    public Task<WmsBackupArtifact> CreateAsync(
        string? sourceRevision = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Automated backups are disabled for this host.");

    public Task<WmsBackupVerification> VerifyAsync(
        string artifactPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WmsBackupVerification(
            false,
            artifactPath,
            "Automated backups are disabled for this host.",
            null));

    public Task<WmsBackupPruneResult> PruneAsync(
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Automated backups are disabled for this host.");

    public Task<WmsBackupRestoreResult> RestoreAsync(
        string artifactPath,
        string targetConnectionString,
        bool allowNonEmptyTarget,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Automated backups are disabled for this host.");
}
