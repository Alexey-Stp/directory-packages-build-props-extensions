using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Extracts PackageReference elements from project files.
/// </summary>
public interface IPackageCollector
{
    /// <summary>
    /// Parses a .csproj file and extracts all PackageReference elements
    /// with their metadata (Version, Condition, PrivateAssets, etc.).
    /// </summary>
    IReadOnlyList<PackageReference> CollectFromProject(string projectPath, string solutionRoot);
}
