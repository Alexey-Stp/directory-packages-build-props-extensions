using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Cli.Output;

/// <summary>JSON written to stdout when a command fails in --json mode.</summary>
public sealed record ErrorOutput
{
    public string SchemaVersion { get; init; } = MigrationPlan.CurrentSchemaVersion;
    public bool Success { get; init; }
    public required MigrationError Error { get; init; }
}

/// <summary>JSON written by <c>check --json</c>.</summary>
public sealed record CheckResult
{
    public string SchemaVersion { get; init; } = MigrationPlan.CurrentSchemaVersion;
    public required bool Success { get; init; }
    public required IReadOnlyList<CheckStep> Steps { get; init; }
}

/// <summary>One <c>dotnet</c> invocation run by <c>check</c>.</summary>
public sealed record CheckStep
{
    /// <summary>"restore" or "build".</summary>
    public required string Name { get; init; }

    public required bool Success { get; init; }

    /// <summary>True if the step did not run because an earlier step failed.</summary>
    public bool Skipped { get; init; }

    public int? ExitCode { get; init; }

    public bool TimedOut { get; init; }

    public long DurationMs { get; init; }

    /// <summary><see cref="ErrorCodes.RestoreFailed"/> or <see cref="ErrorCodes.BuildFailed"/> when the step failed.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Process output; included when the step failed or with --verbose.</summary>
    public string? StandardOutput { get; init; }

    public string? StandardError { get; init; }
}
