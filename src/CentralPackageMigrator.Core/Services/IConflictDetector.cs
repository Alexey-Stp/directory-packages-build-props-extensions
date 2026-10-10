using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Detects packages whose versions cannot be centralized without an explicit resolution.
/// </summary>
public interface IConflictDetector
{
    /// <summary>
    /// Finds packages referenced with incompatible versions across migratable projects.
    /// Versions are compared semantically (1.0 equals 1.0.0). Conditional references whose
    /// conditions each map to a single version are not conflicts. When
    /// <paramref name="existingCentralVersions"/> is given, a mismatch with an existing
    /// Directory.Packages.props entry is also a conflict.
    /// </summary>
    public IReadOnlyList<ConflictGroup> DetectConflicts(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<PackageIdentity, string>? existingCentralVersions = null);
}
