using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Detects the solution root and gathers top-level metadata.
/// </summary>
public interface ISolutionDetector
{
    /// <summary>
    /// Detects the solution root starting from the given path.
    /// Walks up the directory tree looking for .sln files, then .git directories.
    /// Falls back to the starting path.
    /// </summary>
    SolutionInfo Detect(string startPath);
}
