using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Backups;
using Wms.Infrastructure.Backups;

namespace Wms.Backup;

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
            if (args.Length == 0 || Has(args, "--help") || Has(args, "-h"))
            {
                Console.WriteLine(Usage);
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = CreateOptions(args);
            var connectionString = command == "backup"
                ? RequiredEnvironment(
                    "WARECOMMAND_POSTGRES_CONNECTION",
                    "a PostgreSQL connection string")
                : Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_CONNECTION") ??
                  Environment.GetEnvironmentVariable("WARECOMMAND_RESTORE_TARGET_CONNECTION") ??
                  "Host=localhost;Database=warecommand;Username=warecommand";
            var service = new WmsPostgreSqlBackupService(
                options,
                connectionString,
                new WmsBackupHealthState(true, TimeSpan.FromHours(options.MaximumAgeHours)),
                NullLogger<WmsPostgreSqlBackupService>.Instance);

            switch (command)
            {
                case "backup":
                    var artifact = await service.CreateAsync(
                        GetValue(args, "--source-revision") ??
                        Environment.GetEnvironmentVariable("WARECOMMAND_BUILD_REVISION"));
                    Console.WriteLine(JsonSerializer.Serialize(artifact, JsonOptions));
                    return 0;
                case "verify":
                    var verification = await service.VerifyAsync(
                        RequiredValue(args, "--artifact"));
                    Console.WriteLine(JsonSerializer.Serialize(verification, JsonOptions));
                    return verification.IsValid ? 0 : 1;
                case "prune":
                    var prune = await service.PruneAsync();
                    Console.WriteLine(JsonSerializer.Serialize(prune, JsonOptions));
                    return 0;
                case "restore":
                    var allowNonEmpty = Has(args, "--allow-non-empty-target");
                    if (allowNonEmpty &&
                        !string.Equals(
                            GetValue(args, "--confirm"),
                            "RESTORE-WARECOMMAND-DATA",
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Non-empty restore requires --confirm RESTORE-WARECOMMAND-DATA.");
                    }

                    var target = RequiredEnvironment(
                        "WARECOMMAND_RESTORE_TARGET_CONNECTION",
                        "a restore target connection string");
                    var restore = await service.RestoreAsync(
                        RequiredValue(args, "--artifact"),
                        target,
                        allowNonEmpty);
                    Console.WriteLine(JsonSerializer.Serialize(restore, JsonOptions));
                    return 0;
                default:
                    throw new ArgumentException($"Unknown backup command '{command}'.");
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    private static WmsBackupOptions CreateOptions(string[] args)
    {
        var rootPath = GetValue(args, "--root") ??
            Environment.GetEnvironmentVariable("WARECOMMAND_BACKUP_ROOT_PATH") ??
            Path.Combine(AppContext.BaseDirectory, "backups");
        var offsitePath = GetValue(args, "--offsite") ??
            Environment.GetEnvironmentVariable("WARECOMMAND_BACKUP_OFFSITE_PATH");
        var keyFile = GetValue(args, "--key-file") ??
            Environment.GetEnvironmentVariable("WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE") ??
            throw new ArgumentException(
                "A backup encryption key file is required through --key-file or WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE.");
        var retentionDays = ParseInt(args, "--retention-days", 30, 1, 3_650);
        var minimumRetained = ParseInt(args, "--minimum-retained", 2, 2, 1_000);
        var maximumAgeHours = ParseInt(args, "--maximum-age-hours", 36, 1, 8_760);

        return new WmsBackupOptions(
            true,
            Path.GetFullPath(rootPath),
            string.IsNullOrWhiteSpace(offsitePath) ? null : Path.GetFullPath(offsitePath),
            Path.GetFullPath(keyFile),
            retentionDays,
            minimumRetained,
            maximumAgeHours,
            GetValue(args, "--data-protection-path") ??
            Environment.GetEnvironmentVariable("WARECOMMAND_DATA_PROTECTION_PATH"),
            GetValue(args, "--assets-path") ??
            Environment.GetEnvironmentVariable("WARECOMMAND_BACKUP_ASSETS_PATH"),
            GetValue(args, "--pg-dump") ?? "pg_dump",
            GetValue(args, "--pg-restore") ?? "pg_restore");
    }

    private static int ParseInt(
        string[] args,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = GetValue(args, name);
        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : throw new ArgumentException($"{name} must be an integer.");
    }

    private static string RequiredEnvironment(string name, string description) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"Set {name} to provide {description}.");

    private static string RequiredValue(string[] args, string name) =>
        GetValue(args, name) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"{name} is required.");

    private static string? GetValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument =>
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static bool Has(string[] args, string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private const string Usage = """
        WareCommand PostgreSQL backup utility

        Commands:
          backup   Create an encrypted, compressed backup artifact.
          verify   Decrypt, authenticate, checksum, and pg_restore-list an artifact.
          prune    Remove only verified artifacts older than retention while keeping the minimum.
          restore  Restore into an isolated target and reconcile critical counts/balances.

        Required environment:
          WARECOMMAND_POSTGRES_CONNECTION
          WARECOMMAND_BACKUP_ENCRYPTION_KEY_FILE (32 raw bytes, 64 hex, or 32-byte Base64)

        Restore environment:
          WARECOMMAND_RESTORE_TARGET_CONNECTION

        Common options:
          --root <path> --offsite <path> --key-file <path>
          --retention-days <n> --minimum-retained <n> --maximum-age-hours <n>
          --data-protection-path <path> --assets-path <path>
          --pg-dump <path> --pg-restore <path>

        Restore options:
          --artifact <path>
          --allow-non-empty-target --confirm RESTORE-WARECOMMAND-DATA

        Connection strings and backup keys are read from the environment/key file and never added to
        PostgreSQL tool arguments or normal output. Non-empty restore requires explicit authorization.
        """;
}
