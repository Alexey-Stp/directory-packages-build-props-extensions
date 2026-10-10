namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Represents a discovered project (or MSBuild import file) and its package references.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>Absolute path to the file.</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>Path relative to the solution root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Project name (file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>Whether this is a buildable project or a shared import file.</summary>
    public ProjectKind Kind { get; init; } = ProjectKind.Project;

    /// <summary>True if this is an SDK-style project (always true for import files).</summary>
    public bool IsSdkStyle { get; init; }

    /// <summary>Target framework(s), if detected.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>All PackageReference elements found in this file.</summary>
    public IReadOnlyList<PackageReference> PackageReferences { get; init; } = [];

    /// <summary>True if the project already uses VersionOverride or ManagePackageVersionsCentrally.</summary>
    public bool AlreadyUsesCentralManagement { get; init; }

    /// <summary>XML parse error message if the file could not be parsed; null otherwise.</summary>
    public string? ParseError { get; init; }

    /// <summary>
    /// True if the project is listed in the solution file but lives outside the solution root,
    /// where the generated Directory.Packages.props does not apply.
    /// </summary>
    public bool IsOutsideRoot { get; init; }

    /// <summary>SHA-256 (lowercase hex) of the file content at scan time; used to detect stale plans.</summary>
    public string? ContentSha256 { get; init; }

    /// <summary>The newline sequence used by the file ("\n" or "\r\n").</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>True if this entry should take part in migration.</summary>
    public bool IsMigratable => IsSdkStyle && ParseError is null && !IsOutsideRoot;
}

public enum ProjectKind
{
    /// <summary>A buildable project file (.csproj, .fsproj, .vbproj).</summary>
    Project,

    /// <summary>A shared MSBuild import file (Directory.Build.props / Directory.Build.targets).</summary>
    ImportFile
}
