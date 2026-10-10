namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Aggregate statistics about a migration plan.
/// </summary>
public sealed record MigrationStats
{
    /// <summary>Buildable projects found (import files are not counted).</summary>
    public int TotalProjects { get; init; }
    public int SdkStyleProjects { get; init; }
    public int LegacyProjects { get; init; }
    public int TotalPackageReferences { get; init; }

    /// <summary>Packages that will have a central version after migration.</summary>
    public int UniquePackages { get; init; }
    public int ConflictCount { get; init; }
    public int FilesCreated { get; init; }
    public int FilesModified { get; init; }
    public int VersionsRemoved { get; init; }
}
