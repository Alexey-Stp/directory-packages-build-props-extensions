using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Discovers project files (.csproj, .fsproj, .vbproj) and MSBuild import files
/// (Directory.Build.props/.targets) under a solution root.
/// </summary>
public interface IProjectScanner
{
    /// <summary>
    /// Scans every project and import file under <paramref name="solutionRoot"/>, skipping
    /// bin, obj, node_modules, packages, TestResults and hidden directories. When
    /// <paramref name="solutionFilePath"/> is given, projects it lists outside the root are
    /// also returned, flagged with <see cref="ProjectInfo.IsOutsideRoot"/>.
    /// </summary>
    public IReadOnlyList<ProjectInfo> ScanProjects(
        string solutionRoot,
        string? solutionFilePath = null,
        CancellationToken cancellationToken = default);
}
