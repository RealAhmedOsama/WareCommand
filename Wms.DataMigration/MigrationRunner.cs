using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wms.Infrastructure.Data;

namespace Wms.DataMigration;

public sealed class MigrationRunner
{
    public static async Task<DataMigrationReport> RunAsync(
        MigrationOptions options,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTime.UtcNow;
        var sourceSummary = DataMigrationSummary.Empty;
        var targetBefore = DataMigrationSummary.Empty;
        var targetAfter = DataMigrationSummary.Empty;
        var validationErrors = new List<string>();
        var warnings = new List<string>();
        string? backupPath = null;

        try
        {
            ValidateOptions(options);
            var sourcePath = Path.GetFullPath(options.SourcePath);
            var source = await new SqliteSourceReader(sourcePath).ReadAsync(cancellationToken);
            sourceSummary = source.Summary;
            validationErrors.AddRange(source.ValidationErrors);
            if (source.LegacySerialConflicts.Count > 0)
            {
                warnings.Add(
                    $"{source.LegacySerialConflicts.Count} legacy serial rows were preserved in the serial migration conflict report and require reconciliation.");
            }

            if (validationErrors.Count > 0)
            {
                return CreateReport(
                    "Failed",
                    options,
                    sourcePath,
                    startedAtUtc,
                    backupPath,
                    sourceSummary,
                    targetBefore,
                    targetAfter,
                    validationErrors,
                    warnings,
                    "SQLite source validation failed.");
            }

            var targetOptions = new DbContextOptionsBuilder<WmsDbContext>()
                .UseNpgsql(
                    options.TargetConnectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName))
                .Options;

            await using var targetContext = new WmsDbContext(targetOptions);
            await targetContext.Database.OpenConnectionAsync(cancellationToken);

            var pendingMigrations = (await targetContext.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                .ToArray();
            if (pendingMigrations.Length > 0)
            {
                throw new InvalidOperationException(
                    "Target PostgreSQL schema is not current. Apply migrations explicitly before importing data. " +
                    $"Pending migrations: {string.Join(", ", pendingMigrations)}.");
            }

            var targetConnection = targetContext.Database.GetDbConnection();
            targetBefore = await PostgreSqlTargetWriter.ReadSummaryAsync(
                targetConnection,
                transaction: null,
                cancellationToken);

            if (!options.Apply)
            {
                if (targetBefore.HasRows)
                {
                    warnings.Add(
                        "Target already contains rows. Apply mode requires --allow-existing-target and will reconcile by identifier.");
                }

                warnings.AddRange(
                    await PostgreSqlTargetWriter.ValidateRelationshipsAsync(
                        targetConnection,
                        transaction: null,
                        cancellationToken));

                return CreateReport(
                    "DryRun",
                    options,
                    sourcePath,
                    startedAtUtc,
                    backupPath,
                    sourceSummary,
                    targetBefore,
                    targetBefore,
                    validationErrors,
                    warnings,
                    error: null);
            }

            if (!options.AllowExistingTarget && targetBefore.HasRows)
            {
                throw new InvalidOperationException(
                    "Target PostgreSQL database is not empty. Use a fresh database or explicitly pass --allow-existing-target for an idempotent reconciliation.");
            }

            backupPath = await SqliteBackup.CreateAsync(
                sourcePath,
                options.BackupDirectory!,
                cancellationToken);

            await using var transaction = await targetContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var transactionHandle = transaction.GetDbTransaction();

            await PostgreSqlTargetWriter.WriteAsync(
                targetConnection,
                transactionHandle,
                source,
                cancellationToken);

            targetAfter = await PostgreSqlTargetWriter.ReadSummaryAsync(
                targetConnection,
                transactionHandle,
                cancellationToken);
            validationErrors.AddRange(CompareSummaries(sourceSummary, targetAfter));
            validationErrors.AddRange(
                await PostgreSqlTargetWriter.ValidateRelationshipsAsync(
                    targetConnection,
                    transactionHandle,
                    cancellationToken));

            if (validationErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "PostgreSQL validation failed; the target transaction was rolled back.");
            }

            await transaction.CommitAsync(cancellationToken);

            return CreateReport(
                "Applied",
                options,
                sourcePath,
                startedAtUtc,
                backupPath,
                sourceSummary,
                targetBefore,
                targetAfter,
                validationErrors,
                warnings,
                error: null);
        }
        catch (Exception exception)
        {
            return CreateReport(
                "Failed",
                options,
                Path.GetFullPath(options.SourcePath),
                startedAtUtc,
                backupPath,
                sourceSummary,
                targetBefore,
                targetAfter,
                validationErrors,
                warnings,
                exception.Message);
        }
    }

    private static void ValidateOptions(MigrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourcePath))
        {
            throw new ArgumentException("A SQLite source path is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.TargetConnectionString))
        {
            throw new ArgumentException("A PostgreSQL target connection string is required.", nameof(options));
        }

        if (options.Apply && string.IsNullOrWhiteSpace(options.BackupDirectory))
        {
            throw new ArgumentException(
                "Apply mode requires --backup-directory so the SQLite source is backed up before import.",
                nameof(options));
        }
    }

    private static List<string> CompareSummaries(
        DataMigrationSummary source,
        DataMigrationSummary target)
    {
        var errors = new List<string>();
        foreach (var table in source.RowCounts.Keys.Union(target.RowCounts.Keys, StringComparer.Ordinal))
        {
            source.RowCounts.TryGetValue(table, out var sourceCount);
            target.RowCounts.TryGetValue(table, out var targetCount);
            if (sourceCount != targetCount)
            {
                errors.Add($"{table} row count differs: source={sourceCount}, target={targetCount}");
            }
        }

        CompareDecimal("Stock QuantityAvailable", source.StockQuantityAvailable, target.StockQuantityAvailable, errors);
        CompareDecimal("Stock QuantityReserved", source.StockQuantityReserved, target.StockQuantityReserved, errors);
        CompareDecimal("Movement Quantity", source.MovementQuantity, target.MovementQuantity, errors);
        return errors;
    }

    private static void CompareDecimal(
        string label,
        decimal source,
        decimal target,
        List<string> errors)
    {
        if (source != target)
        {
            errors.Add($"{label} differs: source={source}, target={target}");
        }
    }

    private static DataMigrationReport CreateReport(
        string status,
        MigrationOptions options,
        string sourcePath,
        DateTime startedAtUtc,
        string? backupPath,
        DataMigrationSummary source,
        DataMigrationSummary targetBefore,
        DataMigrationSummary targetAfter,
        IReadOnlyList<string> validationErrors,
        IReadOnlyList<string> warnings,
        string? error)
    {
        return new DataMigrationReport(
            status,
            options.DryRun,
            sourcePath,
            startedAtUtc,
            DateTime.UtcNow,
            backupPath,
            source,
            targetBefore,
            targetAfter,
            validationErrors,
            warnings,
            error);
    }
}

internal static class SqliteBackup
{
    public static async Task<string> CreateAsync(
        string sourcePath,
        string backupDirectory,
        CancellationToken cancellationToken)
    {
        var fullBackupDirectory = Path.GetFullPath(backupDirectory);
        Directory.CreateDirectory(fullBackupDirectory);
        var fileName = $"{Path.GetFileNameWithoutExtension(sourcePath)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.sqlite";
        var backupPath = Path.Combine(fullBackupDirectory, fileName);

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(sourcePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var backupConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var sourceConnection = new SqliteConnection(sourceConnectionString);
        await using var backupConnection = new SqliteConnection(backupConnectionString);
        await sourceConnection.OpenAsync(cancellationToken);
        await backupConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(backupConnection);
        return backupPath;
    }
}
