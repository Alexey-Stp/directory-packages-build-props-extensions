using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Scans a solution root to find all .csproj files and determine their project style.
/// </summary>
public interface IProjectScanner
{
    /// <summary>
    /// Discovers all .csproj files under the solution root and classifies them.
    /// </summary>
    IReadOnlyList<ProjectInfo> ScanProjects(string solutionRoot);
}
