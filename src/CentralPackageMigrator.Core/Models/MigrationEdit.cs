namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Describes a new file to be created during migration. The file must not exist when the plan is applied.
/// </summary>
public sealed record FileCreate
{
    /// <summary>Path relative to the solution root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Full generated content of the file.</summary>
    public required string Content { get; init; }

    /// <summary>Description for display purposes.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// Describes all edits to be applied to a single existing file.
/// </summary>
public sealed record FileEdit
{
    /// <summary>Path relative to the solution root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// SHA-256 (lowercase hex) of the file content the plan was computed from.
    /// Applying the plan fails if the file has changed since.
    /// </summary>
    public string? OriginalSha256 { get; init; }

    /// <summary>Changes to apply to this file.</summary>
    public required IReadOnlyList<EditOperation> Operations { get; init; }
}

/// <summary>
/// A single edit operation within a file.
/// </summary>
public sealed record EditOperation
{
    /// <summary>Type of edit.</summary>
    public required EditOperationType Type { get; init; }

    /// <summary>Line number (1-based) of the element the edit applies to; 0 when not applicable.</summary>
    public int Line { get; init; }

    /// <summary>NuGet package ID this edit relates to.</summary>
    public string? PackageId { get; init; }

    /// <summary>The original version being removed or replaced.</summary>
    public string? OriginalText { get; init; }

    /// <summary>The version written by <see cref="EditOperationType.AddPackageVersion"/> and <see cref="EditOperationType.UpdatePackageVersion"/>.</summary>
    public string? Version { get; init; }

    /// <summary>MSBuild condition for an added PackageVersion, if any.</summary>
    public string? Condition { get; init; }

    /// <summary>Human-readable description of the edit.</summary>
    public required string Description { get; init; }
}

public enum EditOperationType
{
    /// <summary>Remove the Version attribute or element from a PackageReference.</summary>
    RemoveVersion,

    /// <summary>Add a PackageVersion item to an existing Directory.Packages.props.</summary>
    AddPackageVersion,

    /// <summary>Change the version of an existing PackageVersion item.</summary>
    UpdatePackageVersion,

    /// <summary>Set ManagePackageVersionsCentrally to true in an existing Directory.Packages.props.</summary>
    EnableCentralPackageManagement
}
