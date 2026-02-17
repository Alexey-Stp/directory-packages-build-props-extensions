namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Represents a version conflict: a package referenced with multiple different versions.
/// </summary>
public sealed record ConflictGroup
{
    /// <summary>The conflicting package.</summary>
    public required PackageIdentity Package { get; init; }

    /// <summary>All distinct version entries found across projects.</summary>
    public required IReadOnlyList<ConflictEntry> Entries { get; init; }

    /// <summary>The resolved version, if a resolution has been provided.</summary>
    public string? ResolvedVersion { get; init; }
}

/// <summary>
/// A single version occurrence within a conflict group.
/// </summary>
public sealed record ConflictEntry
{
    /// <summary>The version string.</summary>
    public required string Version { get; init; }

    /// <summary>Projects that use this version (relative paths).</summary>
    public required IReadOnlyList<string> Projects { get; init; }
}
