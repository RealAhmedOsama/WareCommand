using System.Diagnostics;

namespace Wms.Infrastructure.Backups;

/// <summary>
/// Executes a PostgreSQL backup utility using an already configured process
/// start description. Hosts normally use the system process runner; qualification
/// harnesses can run the same pg_dump and pg_restore commands in a disposable
/// PostgreSQL container.
/// </summary>
public interface IWmsBackupToolProcessRunner
{
    Task RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default);
}

internal sealed class SystemWmsBackupToolProcessRunner : IWmsBackupToolProcessRunner
{
    public static SystemWmsBackupToolProcessRunner Instance { get; } = new();

    private SystemWmsBackupToolProcessRunner()
    {
    }

    public async Task RunAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"Could not start the configured PostgreSQL tool '{startInfo.FileName}'.");
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
                $"The configured PostgreSQL tool '{startInfo.FileName}' exited with code {process.ExitCode}.");
        }
    }
}
