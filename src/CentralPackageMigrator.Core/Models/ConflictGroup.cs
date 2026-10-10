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

    /// <summary>
    /// The highest version among the entries, offered as a default resolution.
    /// Null when the versions cannot all be parsed as NuGet versions.
    /// </summary>
    public string? SuggestedVersion { get; init; }
}

/// <summary>
/// A single version occurrence within a conflict group.
/// </summary>
public sealed record ConflictEntry
{
    /// <summary>The version string.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Files that use this version (relative paths). An existing central version
    /// is reported with the path of Directory.Packages.props.
    /// </summary>
    public required IReadOnlyList<string> Projects { get; init; }
}
