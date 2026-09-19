using System.Text.Json;

namespace Wms.DataMigration;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(MigrationCommandLine.Usage);
                return 0;
            }

            var options = MigrationCommandLine.Parse(args);
            var report = await MigrationRunner.RunAsync(options);
            var json = JsonSerializer.Serialize(report, JsonOptions);

            if (!string.IsNullOrWhiteSpace(options.ReportPath))
            {
                var reportPath = Path.GetFullPath(options.ReportPath);
                var reportDirectory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrWhiteSpace(reportDirectory))
                {
                    Directory.CreateDirectory(reportDirectory);
                }

                await File.WriteAllTextAsync(reportPath, json);
            }

            Console.WriteLine(json);
            return report.Succeeded ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(MigrationCommandLine.Usage);
            return 2;
        }
    }
}

internal static class MigrationCommandLine
{
    public const string Usage = """
        WareCommand SQLite-to-PostgreSQL data migration

        Required:
          --source <path>       SQLite source database file
          --target <connection> PostgreSQL connection string; defaults to WARECOMMAND_POSTGRES_CONNECTION

        Modes:
          (default)              Dry-run: read, validate, and print a reconciliation report
          --apply                Back up the source, import in one transaction, validate, and commit
          --allow-existing-target
                                 Allow idempotent identifier-based reconciliation of a non-empty target

        Output:
          --backup-directory <path>
                                 Required with --apply; destination for the immutable SQLite backup
          --report <path>         Also write the JSON report to this path
          --help                 Show this help

        Apply mode never changes the SQLite source. A failed target write or validation leaves the
        PostgreSQL transaction rolled back. Do not point either database at an unapproved production
        target; apply the checked-in EF migrations separately before running this tool.
        """;

    public static MigrationOptions Parse(string[] args)
    {
        string? sourcePath = null;
        string? targetConnectionString = null;
        string? backupDirectory = null;
        string? reportPath = null;
        var apply = false;
        var dryRun = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--source":
                    sourcePath = ReadValue(args, ref index, "--source");
                    break;
                case "--target":
                    targetConnectionString = ReadValue(args, ref index, "--target");
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--allow-existing-target":
                    // This is meaningful only for --apply, but retaining the flag in the options keeps
                    // command-line validation explicit and prevents accidental implicit reconciliation.
                    break;
                case "--backup-directory":
                    backupDirectory = ReadValue(args, ref index, "--backup-directory");
                    break;
                case "--report":
                    reportPath = ReadValue(args, ref index, "--report");
                    break;
                default:
                    throw new ArgumentException($"Unknown migration argument '{args[index]}'.");
            }
        }

        if (apply && dryRun)
        {
            throw new ArgumentException("--apply and --dry-run cannot be used together.");
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("--source is required.");
        }

        targetConnectionString ??= Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(targetConnectionString))
        {
            throw new ArgumentException(
                "--target is required unless WARECOMMAND_POSTGRES_CONNECTION is set.");
        }

        if (apply && string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new ArgumentException("--backup-directory is required with --apply.");
        }

        return new MigrationOptions(
            sourcePath,
            targetConnectionString,
            apply,
            args.Any(argument => string.Equals(argument, "--allow-existing-target", StringComparison.OrdinalIgnoreCase)),
            backupDirectory,
            reportPath);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}
