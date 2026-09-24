using System.Diagnostics;
using Wms.Infrastructure.Backups;

namespace Wms.ASP.Tests;

internal sealed class DockerPostgreSqlToolProcessRunner : IWmsBackupToolProcessRunner
{
    private readonly string _containerName;

    public DockerPostgreSqlToolProcessRunner(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName) ||
            containerName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("A disposable PostgreSQL container identity is required.", nameof(containerName));
        }

        _containerName = containerName;
    }

    public async Task RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var toolName = Path.GetFileNameWithoutExtension(startInfo.FileName);
        if (!string.Equals(toolName, "pg_dump", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(toolName, "pg_restore", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only PostgreSQL backup utilities may run in the test container.");
        }

        var arguments = startInfo.ArgumentList.ToList();
        var remoteArchivePath = $"/tmp/warecommand-backup-tool-{Guid.NewGuid():N}.dump";
        var remotePassFilePath = $"/tmp/warecommand-backup-tool-{Guid.NewGuid():N}.pgpass";
        string? outputPath = null;
        string? inputPath = null;
        var outputArgumentIndex = arguments.FindIndex(value => value == "--file");
        if (outputArgumentIndex >= 0)
        {
            if (outputArgumentIndex + 1 >= arguments.Count)
            {
                throw new InvalidOperationException("The PostgreSQL dump command has no output path.");
            }

            outputPath = arguments[outputArgumentIndex + 1];
            arguments[outputArgumentIndex + 1] = remoteArchivePath;
        }
        else if (arguments.Count > 0 && File.Exists(arguments[^1]))
        {
            inputPath = arguments[^1];
            arguments[^1] = remoteArchivePath;
        }

        var passFilePath = GetEnvironment(startInfo, "PGPASSFILE");
        if (string.IsNullOrWhiteSpace(passFilePath) || !File.Exists(passFilePath))
        {
            throw new InvalidOperationException("The backup utility did not receive its private PostgreSQL password file.");
        }

        try
        {
            if (inputPath is not null)
            {
                await RunDockerAsync(
                    ["cp", inputPath, $"{_containerName}:{remoteArchivePath}"],
                    cancellationToken);
            }

            var passFileContent = await BuildContainerPassFileAsync(
                passFilePath,
                GetEnvironment(startInfo, "PGDATABASE"),
                GetEnvironment(startInfo, "PGUSER"),
                cancellationToken);
            await RunDockerAsync(
                [
                    "exec", "-i", _containerName, "sh", "-c",
                    $"umask 077; cat > {remotePassFilePath}; chmod 600 {remotePassFilePath}"
                ],
                cancellationToken,
                passFileContent);

            var dockerArguments = new List<string> { "exec" };
            AddEnvironment(dockerArguments, "PGHOST", "127.0.0.1");
            AddEnvironment(dockerArguments, "PGPORT", "5432");
            AddEnvironment(dockerArguments, "PGUSER", GetEnvironment(startInfo, "PGUSER"));
            AddEnvironment(dockerArguments, "PGDATABASE", GetEnvironment(startInfo, "PGDATABASE"));
            AddEnvironment(dockerArguments, "PGAPPNAME", GetEnvironment(startInfo, "PGAPPNAME"));
            AddEnvironment(dockerArguments, "PGSSLMODE", GetEnvironment(startInfo, "PGSSLMODE"));
            AddEnvironment(dockerArguments, "PGPASSFILE", remotePassFilePath);
            dockerArguments.Add(_containerName);
            dockerArguments.Add(toolName);
            dockerArguments.AddRange(arguments);
            try
            {
                await RunDockerAsync(dockerArguments, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"The disposable {toolName} utility failed inside the PostgreSQL test container.",
                    exception);
            }

            if (outputPath is not null)
            {
                await RunDockerAsync(
                    ["cp", $"{_containerName}:{remoteArchivePath}", outputPath],
                    cancellationToken);
            }
        }
        finally
        {
            try
            {
                await RunDockerAsync(
                    ["exec", _containerName, "rm", "-f", remoteArchivePath, remotePassFilePath],
                    CancellationToken.None);
            }
            catch
            {
                // The verification script owns removal of the exact disposable container.
            }
        }
    }

    private static async Task<string> BuildContainerPassFileAsync(
        string passFilePath,
        string? databaseName,
        string? userName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("The backup utility is missing its PostgreSQL identity.");
        }

        var line = (await File.ReadAllTextAsync(passFilePath, cancellationToken))
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (line is null)
        {
            throw new InvalidOperationException("The private PostgreSQL password file is empty.");
        }

        var fields = ParsePassFileEntry(line);
        if (fields.Count != 5)
        {
            throw new InvalidOperationException("The private PostgreSQL password file is malformed.");
        }

        return string.Join(
            ":",
            EscapePassFileField("127.0.0.1"),
            "5432",
            EscapePassFileField(databaseName),
            EscapePassFileField(userName),
            EscapePassFileField(fields[4])) + "\n";
    }

    private static List<string> ParsePassFileEntry(string line)
    {
        var fields = new List<string>(5);
        var field = new System.Text.StringBuilder();
        var escaped = false;
        foreach (var character in line)
        {
            if (escaped)
            {
                field.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else if (character == ':')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        if (escaped)
        {
            field.Append('\\');
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static string EscapePassFileField(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal);

    private static string? GetEnvironment(ProcessStartInfo startInfo, string name) =>
        startInfo.Environment.TryGetValue(name, out var value) ? value : null;

    private static void AddEnvironment(List<string> arguments, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            arguments.Add("--env");
            arguments.Add($"{name}={value}");
        }
    }

    private static async Task RunDockerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The disposable PostgreSQL tool container could not be reached.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
            }

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
                // The Docker client already exited while cancellation was being observed.
            }

            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"A disposable PostgreSQL tool-container command exited with code {process.ExitCode}.");
        }
    }
}
