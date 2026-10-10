using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Cli;

/// <summary>
/// Well-known process exit codes for the cpm-migrate CLI.
/// The same rule applies to analyze, plan, resolve and apply:
/// unresolved conflicts (2) take precedence over warnings (1).
/// Informational warnings never change the exit code.
/// </summary>
public static class ExitCodes
{
    /// <summary>Operation completed successfully.</summary>
    public const int Success = 0;

    /// <summary>Completed, but the plan has warnings with severity "warning" (e.g., legacy projects).</summary>
    public const int AnalysisWarnings = 1;

    /// <summary>Migration plan has unresolved version conflicts.</summary>
    public const int UnresolvedConflicts = 2;

    /// <summary>An error occurred (stale or invalid plan, failed verification, unexpected failure).</summary>
    public const int Error = 3;

    /// <summary>Invalid command-line arguments.</summary>
    public const int InvalidArguments = 4;

    /// <summary>File system error (e.g., missing directory, permission denied).</summary>
    public const int FileSystemError = 5;

    /// <summary>The check command ran, but dotnet restore or build failed.</summary>
    public const int CheckFailed = 6;

    /// <summary>Exit code for a completed analyze/plan/resolve/apply.</summary>
    public static int ForPlan(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.HasUnresolvedConflicts)
        {
            return UnresolvedConflicts;
        }

        return plan.HasWarnings ? AnalysisWarnings : Success;
    }

    /// <summary>Exit code for a failure with the given <see cref="ErrorCodes"/> value.</summary>
    public static int ForError(string errorCode) => errorCode switch
    {
        ErrorCodes.InvalidArguments => InvalidArguments,
        ErrorCodes.UnresolvedConflicts => UnresolvedConflicts,
        ErrorCodes.SolutionNotFound or
        ErrorCodes.PlanFileNotFound or
        ErrorCodes.FileWriteFailed or
        ErrorCodes.IoFailed => FileSystemError,
        _ => Error
    };
}
