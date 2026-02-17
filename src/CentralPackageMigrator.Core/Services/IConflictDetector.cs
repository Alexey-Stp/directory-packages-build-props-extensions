using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Detects version conflicts across all projects.
/// </summary>
public interface IConflictDetector
{
    /// <summary>
    /// Analyzes all package references across projects and identifies
    /// packages that have multiple distinct versions.
    /// </summary>
    IReadOnlyList<ConflictGroup> DetectConflicts(IReadOnlyList<ProjectInfo> projects);
}
