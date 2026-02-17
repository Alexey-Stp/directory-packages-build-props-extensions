namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// A user-provided resolution for a version conflict.
/// </summary>
public sealed record ConflictResolution
{
    /// <summary>The package ID to resolve.</summary>
    public required string PackageId { get; init; }

    /// <summary>The version to use for the centralized reference.</summary>
    public required string Version { get; init; }
}
