namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Represents a discovered .csproj project and its package references.
/// </summary>
public sealed record ProjectInfo
{
    /// <summary>Absolute path to the .csproj file.</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>Path relative to the solution root.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Project name (file name without extension).</summary>
    public required string Name { get; init; }

    /// <summary>True if this is an SDK-style project.</summary>
    public bool IsSdkStyle { get; init; }

    /// <summary>Target framework(s), if detected.</summary>
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];

    /// <summary>All PackageReference elements found in this project.</summary>
    public IReadOnlyList<PackageReference> PackageReferences { get; init; } = [];

    /// <summary>True if the project already uses VersionOverride or ManagePackageVersionsCentrally.</summary>
    public bool AlreadyUsesCentralManagement { get; init; }
}
