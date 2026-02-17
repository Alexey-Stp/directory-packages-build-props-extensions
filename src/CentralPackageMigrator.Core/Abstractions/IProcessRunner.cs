namespace CentralPackageMigrator.Core.Abstractions;

/// <summary>
/// Abstraction for running external processes (dotnet restore, dotnet build).
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string command,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool Success => ExitCode == 0;
}
