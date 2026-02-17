namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Well-known error codes returned by the CLI and in JSON output.
/// These form part of the stable IDE integration contract.
/// </summary>
public static class ErrorCodes
{
    public const string SolutionNotFound = "ERR_SOLUTION_NOT_FOUND";
    public const string NoProjectsFound = "ERR_NO_PROJECTS_FOUND";
    public const string UnresolvedConflicts = "ERR_UNRESOLVED_CONFLICTS";
    public const string MalformedProject = "ERR_MALFORMED_PROJECT";
    public const string FileWriteFailed = "ERR_FILE_WRITE_FAILED";
    public const string RestoreFailed = "ERR_RESTORE_FAILED";
    public const string BuildFailed = "ERR_BUILD_FAILED";
    public const string InvalidPath = "ERR_INVALID_PATH";
    public const string AlreadyMigrated = "ERR_ALREADY_MIGRATED";
    public const string PlanFileNotFound = "ERR_PLAN_FILE_NOT_FOUND";
    public const string InvalidPlanFile = "ERR_INVALID_PLAN_FILE";
}

/// <summary>
/// Represents a structured error in JSON output mode.
/// </summary>
public sealed record MigrationError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public string? FilePath { get; init; }
}
