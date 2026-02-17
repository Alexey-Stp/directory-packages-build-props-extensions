namespace CentralPackageMigrator.Cli;

/// <summary>
/// Well-known process exit codes for the cpm-migrate CLI.
/// </summary>
public static class ExitCodes
{
    /// <summary>Operation completed successfully.</summary>
    public const int Success = 0;

    /// <summary>Analysis completed with warnings (e.g., legacy projects).</summary>
    public const int AnalysisWarnings = 1;

    /// <summary>Migration plan has unresolved version conflicts.</summary>
    public const int UnresolvedConflicts = 2;

    /// <summary>An unexpected error occurred.</summary>
    public const int Error = 3;

    /// <summary>Invalid command-line arguments.</summary>
    public const int InvalidArguments = 4;

    /// <summary>File system error (e.g., missing directory, permission denied).</summary>
    public const int FileSystemError = 5;
}
