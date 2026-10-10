namespace CentralPackageMigrator.Core.Abstractions;

/// <summary>
/// Abstraction for running external processes (dotnet restore, dotnet build).
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> with <paramref name="arguments"/> passed as individual
    /// arguments (no shell parsing or quoting). The process is killed when
    /// <paramref name="timeout"/> elapses or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public Task<ProcessResult> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool TimedOut { get; init; }
    public TimeSpan Duration { get; init; }
    public bool Success => ExitCode == 0 && !TimedOut;
}
