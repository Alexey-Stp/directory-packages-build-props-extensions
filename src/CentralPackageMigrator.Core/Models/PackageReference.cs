namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Represents a single PackageReference element found in a .csproj file.
/// Immutable — captures all metadata needed for migration planning.
/// </summary>
public sealed record PackageReference
{
    /// <summary>The NuGet package identity (Include= or Update= value).</summary>
    public required PackageIdentity Package { get; init; }

    /// <summary>The version string as declared. Null if version is inherited or missing.</summary>
    public string? Version { get; init; }

    /// <summary>True if version was specified via attribute (Version="x"), false if nested element.</summary>
    public bool IsVersionAttribute { get; init; }

    /// <summary>True if this is an Update= reference rather than Include=.</summary>
    public bool IsUpdate { get; init; }

    /// <summary>MSBuild Condition attribute, if present.</summary>
    public string? Condition { get; init; }

    /// <summary>PrivateAssets metadata value, if present.</summary>
    public string? PrivateAssets { get; init; }

    /// <summary>IncludeAssets metadata value, if present.</summary>
    public string? IncludeAssets { get; init; }

    /// <summary>ExcludeAssets metadata value, if present.</summary>
    public string? ExcludeAssets { get; init; }

    /// <summary>Line number (1-based) of the PackageReference element in the source file.</summary>
    public int Line { get; init; }

    /// <summary>Column number (1-based) of the PackageReference element in the source file.</summary>
    public int Column { get; init; }

    /// <summary>Relative path to the project file containing this reference.</summary>
    public required string ProjectRelativePath { get; init; }
}
