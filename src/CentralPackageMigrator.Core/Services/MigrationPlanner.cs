using System.Text;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class MigrationPlanner : IMigrationPlanner
{
    private const string SchemaVersionValue = "1.0";
    private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";

    private readonly IConflictDetector _conflictDetector;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<MigrationPlanner> _logger;

    public MigrationPlanner(
        IConflictDetector conflictDetector,
        IFileSystem fileSystem,
        ILogger<MigrationPlanner> logger)
    {
        _conflictDetector = conflictDetector;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public MigrationPlan CreatePlan(
        SolutionInfo solution,
        IReadOnlyList<ConflictResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(resolutions);

        _logger.LogInformation("Creating migration plan for: {Root}", solution.RootPath);

        var sdkProjects = solution.Projects.Where(p => p.IsSdkStyle).ToList();
        var legacyProjects = solution.Projects.Where(p => !p.IsSdkStyle).ToList();

        // Detect conflicts
        var rawConflicts = _conflictDetector.DetectConflicts(solution.Projects);
        var conflicts = ApplyResolutions(rawConflicts, resolutions);

        // Collect warnings
        var warnings = CollectWarnings(solution, legacyProjects, sdkProjects);

        // Build the package version map (package -> version)
        var packageVersionMap = BuildPackageVersionMap(sdkProjects, conflicts);

        // Generate creates and edits
        var creates = GenerateCreates(solution, packageVersionMap);
        var edits = GenerateEdits(sdkProjects, solution.RootPath);

        // Calculate stats
        int totalPackageRefs = sdkProjects.Sum(p => p.PackageReferences.Count);
        int versionsRemoved = sdkProjects
            .SelectMany(p => p.PackageReferences)
            .Count(r => r.Version is not null && !r.IsUpdate);

        var stats = new MigrationStats
        {
            TotalProjects = solution.Projects.Count,
            SdkStyleProjects = sdkProjects.Count,
            LegacyProjects = legacyProjects.Count,
            TotalPackageReferences = totalPackageRefs,
            UniquePackages = packageVersionMap.Count,
            ConflictCount = rawConflicts.Count,
            FilesCreated = creates.Count,
            FilesModified = edits.Count,
            VersionsRemoved = versionsRemoved
        };

        var plan = new MigrationPlan
        {
            SchemaVersion = SchemaVersionValue,
            Timestamp = DateTimeOffset.UtcNow,
            SolutionRoot = solution.RootPath,
            Creates = creates,
            Edits = edits,
            Conflicts = conflicts,
            Warnings = warnings,
            Stats = stats
        };

        _logger.LogInformation(
            "Plan created: {Creates} file(s) to create, {Edits} file(s) to edit, {Conflicts} conflict(s)",
            creates.Count, edits.Count, conflicts.Count(c => c.ResolvedVersion is null));

        return plan;
    }

    private static IReadOnlyList<ConflictGroup> ApplyResolutions(
        IReadOnlyList<ConflictGroup> conflicts,
        IReadOnlyList<ConflictResolution> resolutions)
    {
        var resolutionMap = resolutions
            .ToDictionary(
                r => r.PackageId,
                r => r.Version,
                StringComparer.OrdinalIgnoreCase);

        return conflicts
            .Select(c =>
            {
                if (resolutionMap.TryGetValue(c.Package.Id, out string? resolvedVersion))
                {
                    return c with { ResolvedVersion = resolvedVersion };
                }
                return c;
            })
            .ToList()
            .AsReadOnly();
    }

    private List<MigrationWarning> CollectWarnings(
        SolutionInfo solution,
        List<ProjectInfo> legacyProjects,
        List<ProjectInfo> sdkProjects)
    {
        var warnings = new List<MigrationWarning>();

        // Warn about legacy projects
        foreach (var legacy in legacyProjects)
        {
            warnings.Add(new MigrationWarning
            {
                Code = WarningCodes.LegacyProjectFormat,
                Message = $"Legacy project format detected — will be skipped: {legacy.RelativePath}",
                ProjectPath = legacy.RelativePath
            });
        }

        // Warn about existing Directory.Packages.props
        if (solution.HasExistingDirectoryPackagesProps)
        {
            warnings.Add(new MigrationWarning
            {
                Code = WarningCodes.ExistingDirectoryPackagesProps,
                Message = "An existing Directory.Packages.props was found and will be overwritten"
            });
        }

        // Warn about projects with no package references
        foreach (var project in sdkProjects.Where(p => p.PackageReferences.Count == 0))
        {
            warnings.Add(new MigrationWarning
            {
                Code = WarningCodes.NoPackageReferences,
                Message = $"No PackageReference elements found in: {project.RelativePath}",
                ProjectPath = project.RelativePath
            });
        }

        // Warn about Update= references
        foreach (var project in sdkProjects)
        {
            foreach (var updateRef in project.PackageReferences.Where(r => r.IsUpdate))
            {
                warnings.Add(new MigrationWarning
                {
                    Code = WarningCodes.UpdateReferenceDetected,
                    Message = $"Update= PackageReference for '{updateRef.Package.Id}' in {project.RelativePath} — version will not be centralized",
                    ProjectPath = project.RelativePath,
                    PackageId = updateRef.Package.Id
                });
            }
        }

        // Warn about conditional version references
        foreach (var project in sdkProjects)
        {
            foreach (var condRef in project.PackageReferences.Where(r => r.Condition is not null && r.Version is not null))
            {
                warnings.Add(new MigrationWarning
                {
                    Code = WarningCodes.ConditionalVersionReference,
                    Message = $"Conditional PackageReference for '{condRef.Package.Id}' in {project.RelativePath} with Condition='{condRef.Condition}'",
                    ProjectPath = project.RelativePath,
                    PackageId = condRef.Package.Id
                });
            }
        }

        // Warn about projects already using CPM
        foreach (var project in sdkProjects.Where(p => p.AlreadyUsesCentralManagement))
        {
            warnings.Add(new MigrationWarning
            {
                Code = WarningCodes.ExistingCentralManagement,
                Message = $"Project already uses central package management: {project.RelativePath}",
                ProjectPath = project.RelativePath
            });
        }

        return warnings;
    }

    private Dictionary<PackageIdentity, string> BuildPackageVersionMap(
        List<ProjectInfo> sdkProjects,
        IReadOnlyList<ConflictGroup> conflicts)
    {
        var map = new Dictionary<PackageIdentity, string>();

        // Build resolution map from conflicts
        var conflictResolutions = conflicts
            .Where(c => c.ResolvedVersion is not null)
            .ToDictionary(c => c.Package, c => c.ResolvedVersion!);

        var unresolvedConflictPackages = conflicts
            .Where(c => c.ResolvedVersion is null)
            .Select(c => c.Package)
            .ToHashSet();

        // Collect all versioned, non-Update references
        var allRefs = sdkProjects
            .SelectMany(p => p.PackageReferences)
            .Where(r => r.Version is not null && !r.IsUpdate)
            .ToList();

        foreach (var group in allRefs.GroupBy(r => r.Package))
        {
            var packageId = group.Key;

            // If there's a conflict resolution, use it
            if (conflictResolutions.TryGetValue(packageId, out string? resolved))
            {
                map[packageId] = resolved;
                continue;
            }

            // If there's an unresolved conflict, skip (don't add to map)
            if (unresolvedConflictPackages.Contains(packageId))
            {
                continue;
            }

            // No conflict: all versions are the same
            string version = group.First().Version!;
            map[packageId] = version;
        }

        return map;
    }

    private List<FileCreate> GenerateCreates(
        SolutionInfo solution,
        Dictionary<PackageIdentity, string> packageVersionMap)
    {
        var creates = new List<FileCreate>();

        if (packageVersionMap.Count == 0)
        {
            return creates;
        }

        string content = GenerateDirectoryPackagesProps(packageVersionMap);

        creates.Add(new FileCreate
        {
            RelativePath = DirectoryPackagesPropsFileName,
            Content = content,
            Description = "Central Package Management file with all package versions"
        });

        return creates;
    }

    private static string GenerateDirectoryPackagesProps(
        Dictionary<PackageIdentity, string> packageVersionMap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<Project>");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine();
        sb.AppendLine("  <ItemGroup>");

        foreach (var kvp in packageVersionMap.OrderBy(k => k.Key.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"    <PackageVersion Include=\"{kvp.Key.Id}\" Version=\"{kvp.Value}\" />");
        }

        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("</Project>");

        return sb.ToString();
    }

    private List<FileEdit> GenerateEdits(List<ProjectInfo> sdkProjects, string solutionRoot)
    {
        var edits = new List<FileEdit>();

        foreach (var project in sdkProjects)
        {
            var versionedRefs = project.PackageReferences
                .Where(r => r.Version is not null && !r.IsUpdate)
                .ToList();

            if (versionedRefs.Count == 0)
            {
                continue;
            }

            var operations = new List<EditOperation>();

            foreach (var pkgRef in versionedRefs)
            {
                operations.Add(new EditOperation
                {
                    Type = EditOperationType.RemoveVersion,
                    Line = pkgRef.Line,
                    PackageId = pkgRef.Package.Id,
                    OriginalText = pkgRef.Version,
                    Description = $"Remove Version=\"{pkgRef.Version}\" from {pkgRef.Package.Id}"
                });
            }

            edits.Add(new FileEdit
            {
                RelativePath = project.RelativePath,
                Operations = operations
                    .OrderByDescending(o => o.Line)
                    .ToList()
                    .AsReadOnly()
            });
        }

        return edits;
    }
}
