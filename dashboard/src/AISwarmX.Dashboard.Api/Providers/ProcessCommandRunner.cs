using System.ComponentModel;
using System.Diagnostics;

namespace AISwarmX.Dashboard.Api.Providers;

/// <summary>
/// Executes read-only CLI tools safely without shell interpolation.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new CommandResult(-1, string.Empty, "Process could not be started.");
            }
        }
        catch (Win32Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message, NotFound: true);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);
            return new CommandResult(process.ExitCode, stdOut, stdErr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new CommandResult(-1, string.Empty, "Command timed out.", TimedOut: true);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore cleanup failure when process exits concurrently.
        }
    }
}
