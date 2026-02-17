namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Describes a file to be created during migration.
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
/// Describes all edits to be applied to a single file.
/// </summary>
public sealed record FileEdit
{
    /// <summary>Path relative to the solution root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Ordered list of changes to apply to this file.</summary>
    public required IReadOnlyList<EditOperation> Operations { get; init; }
}

/// <summary>
/// A single edit operation within a file.
/// </summary>
public sealed record EditOperation
{
    /// <summary>Type of edit.</summary>
    public required EditOperationType Type { get; init; }

    /// <summary>Line number (1-based) where the edit starts.</summary>
    public int Line { get; init; }

    /// <summary>NuGet package ID this edit relates to.</summary>
    public string? PackageId { get; init; }

    /// <summary>The original text being replaced/removed.</summary>
    public string? OriginalText { get; init; }

    /// <summary>The replacement text (for Replace operations).</summary>
    public string? ReplacementText { get; init; }

    /// <summary>Human-readable description of the edit.</summary>
    public required string Description { get; init; }
}

public enum EditOperationType
{
    /// <summary>Remove the Version attribute or element from a PackageReference.</summary>
    RemoveVersion,

    /// <summary>Replace an entire line or element.</summary>
    ReplaceLine,

    /// <summary>Insert a new line or element.</summary>
    InsertLine,

    /// <summary>Remove an entire line or element.</summary>
    RemoveLine
}
