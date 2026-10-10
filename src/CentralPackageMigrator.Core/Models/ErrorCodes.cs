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
    public const string IoFailed = "ERR_IO_FAILED";
    public const string RestoreFailed = "ERR_RESTORE_FAILED";
    public const string BuildFailed = "ERR_BUILD_FAILED";
    public const string InvalidPath = "ERR_INVALID_PATH";
    public const string PlanFileNotFound = "ERR_PLAN_FILE_NOT_FOUND";
    public const string InvalidPlanFile = "ERR_INVALID_PLAN_FILE";
    public const string StalePlan = "ERR_STALE_PLAN";
    public const string InvalidArguments = "ERR_INVALID_ARGUMENTS";
    public const string VerificationFailed = "ERR_VERIFICATION_FAILED";
    public const string Cancelled = "ERR_CANCELLED";
    public const string Unexpected = "ERR_UNEXPECTED";
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

/// <summary>
/// An expected failure of a migration operation, carrying a stable <see cref="ErrorCodes"/> value.
/// </summary>
public sealed class MigrationException : Exception
{
    public MigrationException(string code, string message, string? filePath = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        FilePath = filePath;
    }

    /// <summary>One of the <see cref="ErrorCodes"/> constants.</summary>
    public string Code { get; }

    /// <summary>The file the error relates to, if any.</summary>
    public string? FilePath { get; }

    public MigrationError ToError() => new()
    {
        Code = Code,
        Message = Message,
        Detail = InnerException?.Message,
        FilePath = FilePath
    };
}
