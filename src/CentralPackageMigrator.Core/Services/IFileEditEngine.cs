using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Applies a migration plan to the file system.
/// Creates new files and edits existing project files.
/// </summary>
public interface IFileEditEngine
{
    /// <summary>
    /// Applies the migration plan. Creates Directory.Packages.props
    /// and removes Version attributes from .csproj files.
    /// </summary>
    /// <param name="plan">The migration plan to apply.</param>
    /// <param name="dryRun">If true, validates but does not write files.</param>
    /// <returns>List of files that were (or would be) written.</returns>
    IReadOnlyList<string> Apply(MigrationPlan plan, bool dryRun = false);
}
