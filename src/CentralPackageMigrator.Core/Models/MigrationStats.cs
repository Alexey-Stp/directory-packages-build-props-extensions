namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Aggregate statistics about a migration plan.
/// </summary>
public sealed record MigrationStats
{
    public int TotalProjects { get; init; }
    public int SdkStyleProjects { get; init; }
    public int LegacyProjects { get; init; }
    public int TotalPackageReferences { get; init; }
    public int UniquePackages { get; init; }
    public int ConflictCount { get; init; }
    public int FilesCreated { get; init; }
    public int FilesModified { get; init; }
    public int VersionsRemoved { get; init; }
}
