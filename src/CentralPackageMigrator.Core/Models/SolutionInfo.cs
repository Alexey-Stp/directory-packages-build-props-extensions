namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Represents the detected solution root and all discovered projects.
/// </summary>
public sealed record SolutionInfo
{
    /// <summary>Absolute path to the solution root directory.</summary>
    public required string RootPath { get; init; }

    /// <summary>Path to the .sln file if one was found.</summary>
    public string? SolutionFilePath { get; init; }

    /// <summary>How the root was detected.</summary>
    public required RootDetectionMethod DetectionMethod { get; init; }

    /// <summary>All discovered projects.</summary>
    public IReadOnlyList<ProjectInfo> Projects { get; init; } = [];

    /// <summary>Whether a Directory.Packages.props already exists.</summary>
    public bool HasExistingDirectoryPackagesProps { get; init; }

    /// <summary>Whether a Directory.Build.props already exists.</summary>
    public bool HasExistingDirectoryBuildProps { get; init; }
}

public enum RootDetectionMethod
{
    SolutionFile,
    GitRoot,
    CurrentDirectory,
    ExplicitPath
}
