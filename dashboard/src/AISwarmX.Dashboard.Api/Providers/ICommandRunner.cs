namespace AISwarmX.Dashboard.Api.Providers;

/// <summary>
/// Result of executing a read-only external CLI command (such as git or gh).
/// </summary>
public sealed record CommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut = false,
    bool NotFound = false)
{
    public bool Succeeded => !TimedOut && !NotFound && ExitCode == 0;
}

/// <summary>
/// Abstraction for running read-only external commands so providers are cleanly testable.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
