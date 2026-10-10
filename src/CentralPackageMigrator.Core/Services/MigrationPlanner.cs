using System.Text;
using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Editing;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class MigrationPlanner : IMigrationPlanner
{
    private const string DirectoryPackagesPropsFileName = "Directory.Packages.props";
    private const string DirectoryBuildPropsFileName = "Directory.Build.props";

    private readonly IConflictDetector _conflictDetector;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<MigrationPlanner> _logger;
    private readonly TimeProvider _timeProvider;

    public MigrationPlanner(
        IConflictDetector conflictDetector,
        IFileSystem fileSystem,
        ILogger<MigrationPlanner> logger,
        TimeProvider? timeProvider = null)
    {
        _conflictDetector = conflictDetector;
        _fileSystem = fileSystem;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public MigrationPlan CreatePlan(
        SolutionInfo solution,
        IReadOnlyList<ConflictResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(resolutions);

        _logger.LogInformation("Creating migration plan for: {Root}", solution.RootPath);

        var migratable = solution.Projects.Where(p => p.IsMigratable).ToList();
        var central = ReadExistingCentralFile(solution.RootPath);

        // Detect conflicts, including mismatches with an existing Directory.Packages.props
        var rawConflicts = _conflictDetector.DetectConflicts(solution.Projects, central?.UnconditionalVersions);
        var conflicts = ApplyResolutions(rawConflicts, resolutions, out var unusedResolutions);

        var entries = BuildCentralEntries(solution.Projects, conflicts, central, out var existingConditionalOnly);
        bool cpmEnabledElsewhere = IsCentralManagementEnabledByBuildProps(solution);

        var creates = new List<FileCreate>();
        var edits = new List<FileEdit>();
        bool enablesCentralManagement = false;

        if (central is null)
        {
            if (entries.Count > 0)
            {
                string newLine = migratable.FirstOrDefault()?.NewLine ?? Environment.NewLine;
                creates.Add(new FileCreate
                {
                    RelativePath = DirectoryPackagesPropsFileName,
                    Content = GenerateDirectoryPackagesProps(entries, newLine),
                    Description = "Central Package Management file with all package versions"
                });
            }
        }
        else
        {
            var centralEdit = PlanCentralFileEdit(central, entries, cpmEnabledElsewhere, migratable);
            if (centralEdit is not null)
            {
                edits.Add(centralEdit);
                enablesCentralManagement = centralEdit.Operations
                    .Any(o => o.Type == EditOperationType.EnableCentralPackageManagement);
            }
        }

        edits.AddRange(GenerateProjectEdits(migratable));

        var warnings = CollectWarnings(
            solution, central is not null, unusedResolutions, existingConditionalOnly, enablesCentralManagement, entries);

        var stats = new MigrationStats
        {
            TotalProjects = solution.Projects.Count(p => p.Kind == ProjectKind.Project),
            SdkStyleProjects = solution.Projects.Count(p => p.Kind == ProjectKind.Project && p.IsSdkStyle),
            LegacyProjects = solution.Projects.Count(p => p.Kind == ProjectKind.Project && !p.IsSdkStyle && p.ParseError is null && !p.IsOutsideRoot),
            TotalPackageReferences = migratable.Sum(p => p.PackageReferences.Count),
            UniquePackages = entries.Select(e => e.Package).Distinct().Count(),
            ConflictCount = rawConflicts.Count,
            FilesCreated = creates.Count,
            FilesModified = edits.Count,
            VersionsRemoved = migratable.SelectMany(p => p.PackageReferences).Count(r => r.IsCentralizable)
        };

        var plan = new MigrationPlan
        {
            SchemaVersion = MigrationPlan.CurrentSchemaVersion,
            Timestamp = _timeProvider.GetUtcNow(),
            SolutionRoot = solution.RootPath,
            Creates = creates.AsReadOnly(),
            Edits = edits.AsReadOnly(),
            Conflicts = conflicts,
            Warnings = warnings.AsReadOnly(),
            Stats = stats
        };

        _logger.LogInformation(
            "Plan created: {Creates} file(s) to create, {Edits} file(s) to edit, {Conflicts} unresolved conflict(s)",
            creates.Count, edits.Count, conflicts.Count(c => c.ResolvedVersion is null));

        return plan;
    }

    private static IReadOnlyList<ConflictGroup> ApplyResolutions(
        IReadOnlyList<ConflictGroup> conflicts,
        IReadOnlyList<ConflictResolution> resolutions,
        out List<ConflictResolution> unused)
    {
        var resolutionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolution in resolutions)
        {
            resolutionMap[resolution.PackageId] = resolution.Version;
        }

        unused = resolutions
            .Where(r => !conflicts.Any(c => string.Equals(c.Package.Id, r.PackageId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return conflicts
            .Select(c => resolutionMap.TryGetValue(c.Package.Id, out string? resolvedVersion)
                ? c with { ResolvedVersion = resolvedVersion }
                : c)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Decides the central version(s) for every package whose versions can be centralized.
    /// Packages with unresolved conflicts are left out.
    /// </summary>
    private static List<CentralEntry> BuildCentralEntries(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyList<ConflictGroup> conflicts,
        ExistingCentralFile? central,
        out List<PackageIdentity> existingConditionalOnly)
    {
        var resolved = conflicts
            .Where(c => c.ResolvedVersion is not null)
            .ToDictionary(c => c.Package, c => c.ResolvedVersion!);

        var unresolved = conflicts
            .Where(c => c.ResolvedVersion is null)
            .Select(c => c.Package)
            .ToHashSet();

        var entries = new List<CentralEntry>();
        existingConditionalOnly = [];

        foreach (var group in PackageVersionAnalyzer.GroupCentralizable(projects))
        {
            var package = group.Key;
            if (unresolved.Contains(package))
            {
                continue;
            }

            if (central?.ConditionalOnly.Contains(package) == true)
            {
                existingConditionalOnly.Add(package);
                continue;
            }

            if (resolved.TryGetValue(package, out string? resolvedVersion))
            {
                entries.Add(new CentralEntry(package, resolvedVersion, null));
                continue;
            }

            string? existing = null;
            central?.UnconditionalVersions.TryGetValue(package, out existing);

            var decision = PackageVersionAnalyzer.Decide(package, group.ToList(), existing);
            switch (decision.Kind)
            {
                case DecisionKind.Single:
                    entries.Add(new CentralEntry(package, decision.Version!, null));
                    break;

                case DecisionKind.Conditional:
                    entries.AddRange(decision.ConditionalVersions.Select(cv => new CentralEntry(package, cv.Version, cv.Condition)));
                    break;
            }
        }

        return entries
            .OrderBy(e => e.Package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Condition ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    private static FileEdit? PlanCentralFileEdit(
        ExistingCentralFile central,
        List<CentralEntry> entries,
        bool cpmEnabledElsewhere,
        List<ProjectInfo> migratable)
    {
        var operations = new List<EditOperation>();

        foreach (var entry in entries)
        {
            bool hasExisting = central.UnconditionalVersions.TryGetValue(entry.Package, out string? existing);

            if (entry.Condition is null && hasExisting)
            {
                if (!PackageVersionAnalyzer.AreEquivalent(existing!, entry.Version))
                {
                    operations.Add(new EditOperation
                    {
                        Type = EditOperationType.UpdatePackageVersion,
                        PackageId = entry.Package.Id,
                        OriginalText = existing,
                        Version = entry.Version,
                        Description = $"Change PackageVersion {entry.Package.Id} from {existing} to {entry.Version}"
                    });
                }

                continue;
            }

            operations.Add(new EditOperation
            {
                Type = EditOperationType.AddPackageVersion,
                PackageId = entry.Package.Id,
                Version = entry.Version,
                Condition = entry.Condition,
                Description = entry.Condition is null
                    ? $"Add PackageVersion {entry.Package.Id} {entry.Version}"
                    : $"Add PackageVersion {entry.Package.Id} {entry.Version} when {entry.Condition}"
            });
        }

        bool needsCpm = operations.Count > 0 || migratable.Any(p => p.PackageReferences.Any(r => r.IsCentralizable));
        if (!central.ManagesCentrally && !cpmEnabledElsewhere && needsCpm)
        {
            operations.Insert(0, new EditOperation
            {
                Type = EditOperationType.EnableCentralPackageManagement,
                Description = "Set ManagePackageVersionsCentrally to true"
            });
        }

        if (operations.Count == 0)
        {
            return null;
        }

        return new FileEdit
        {
            RelativePath = DirectoryPackagesPropsFileName,
            OriginalSha256 = central.Sha256,
            Operations = operations.AsReadOnly()
        };
    }

    private static IEnumerable<FileEdit> GenerateProjectEdits(List<ProjectInfo> migratable)
    {
        foreach (var project in migratable)
        {
            var operations = project.PackageReferences
                .Where(r => r.IsCentralizable)
                .Select(r => new EditOperation
                {
                    Type = EditOperationType.RemoveVersion,
                    Line = r.Line,
                    PackageId = r.Package.Id,
                    OriginalText = r.Version,
                    Description = $"Remove Version=\"{r.Version}\" from {r.Package.Id}"
                })
                .OrderByDescending(o => o.Line)
                .ToList();

            if (operations.Count == 0)
            {
                continue;
            }

            yield return new FileEdit
            {
                RelativePath = project.RelativePath,
                OriginalSha256 = project.ContentSha256,
                Operations = operations.AsReadOnly()
            };
        }
    }

    private static List<MigrationWarning> CollectWarnings(
        SolutionInfo solution,
        bool hasExistingCentralFile,
        List<ConflictResolution> unusedResolutions,
        List<PackageIdentity> existingConditionalOnly,
        bool enablesCentralManagement,
        List<CentralEntry> entries)
    {
        var warnings = new List<MigrationWarning>();
        var conditionalPackages = entries.Where(e => e.Condition is not null).Select(e => e.Package).ToHashSet();

        foreach (var project in solution.Projects)
        {
            if (project.IsOutsideRoot)
            {
                warnings.Add(Warning(WarningCodes.ProjectOutsideRoot,
                    $"Project is listed in the solution but outside the root; Directory.Packages.props will not apply to it: {project.RelativePath}",
                    project.RelativePath));
            }
            else if (project.ParseError is not null)
            {
                warnings.Add(Warning(WarningCodes.MalformedXml,
                    $"Could not parse {project.RelativePath}; it will be skipped: {project.ParseError}",
                    project.RelativePath));
            }
            else if (!project.IsSdkStyle)
            {
                warnings.Add(Warning(WarningCodes.LegacyProjectFormat,
                    $"Legacy project format detected — will be skipped: {project.RelativePath}",
                    project.RelativePath));
            }
        }

        if (hasExistingCentralFile)
        {
            warnings.Add(Info(WarningCodes.ExistingDirectoryPackagesProps,
                "An existing Directory.Packages.props was found; new versions will be merged into it"));
        }

        if (enablesCentralManagement)
        {
            warnings.Add(Info(WarningCodes.CentralManagementEnabled,
                "ManagePackageVersionsCentrally is not enabled; it will be set to true in Directory.Packages.props"));
        }

        foreach (var project in solution.Projects.Where(p => p.IsMigratable))
        {
            if (project.Kind == ProjectKind.Project && project.PackageReferences.Count == 0)
            {
                warnings.Add(Info(WarningCodes.NoPackageReferences,
                    $"No PackageReference elements found in: {project.RelativePath}",
                    project.RelativePath));
            }

            if (project.Kind == ProjectKind.Project && project.AlreadyUsesCentralManagement)
            {
                warnings.Add(Info(WarningCodes.ExistingCentralManagement,
                    $"Project already uses central package management: {project.RelativePath}",
                    project.RelativePath));
            }

            foreach (var reference in project.PackageReferences)
            {
                AddReferenceWarnings(warnings, project, reference, conditionalPackages);
            }
        }

        foreach (var package in existingConditionalOnly)
        {
            warnings.Add(Warning(WarningCodes.ExistingConditionalPackageVersion,
                $"Directory.Packages.props only has conditional PackageVersion items for '{package.Id}'; verify they cover every project",
                packageId: package.Id));
        }

        foreach (var resolution in unusedResolutions)
        {
            warnings.Add(Warning(WarningCodes.UnusedResolution,
                $"Resolution '{resolution.PackageId}={resolution.Version}' was ignored: the package has no version conflict",
                packageId: resolution.PackageId));
        }

        return warnings;
    }

    private static void AddReferenceWarnings(
        List<MigrationWarning> warnings,
        ProjectInfo project,
        PackageReference reference,
        HashSet<PackageIdentity> conditionalPackages)
    {
        string id = reference.Package.Id;

        if (reference.IsUpdate && reference.Version is not null)
        {
            warnings.Add(Warning(WarningCodes.UpdateReferenceDetected,
                $"Update= PackageReference for '{id}' in {project.RelativePath} sets a version; it is not centralized and will trigger NU1008 under Central Package Management",
                project.RelativePath, id));
        }

        if (reference.IsCentralizable && reference.Condition is not null)
        {
            string detail = conditionalPackages.Contains(reference.Package)
                ? "a conditional PackageVersion will be generated"
                : "the version is centralized unconditionally";
            warnings.Add(Info(WarningCodes.ConditionalVersionReference,
                $"Conditional PackageReference for '{id}' in {project.RelativePath} with Condition='{reference.Condition}'; {detail}",
                project.RelativePath, id));
        }

        if (reference.VersionOverride is not null)
        {
            warnings.Add(Info(WarningCodes.VersionOverrideDetected,
                $"PackageReference for '{id}' in {project.RelativePath} uses VersionOverride='{reference.VersionOverride}'; it is left unchanged",
                project.RelativePath, id));
        }

        if (reference.IsCentralizable && reference.IsPropertyVersion)
        {
            warnings.Add(reference.HasUnresolvedProperty
                ? Warning(WarningCodes.PropertyBasedVersion,
                    $"Version '{reference.Version}' of '{id}' in {project.RelativePath} uses a property that could not be resolved; it is copied as-is and must be defined for Directory.Packages.props",
                    project.RelativePath, id)
                : Info(WarningCodes.PropertyBasedVersion,
                    $"Version '{reference.Version}' of '{id}' in {project.RelativePath} was resolved to '{reference.EffectiveVersion}'",
                    project.RelativePath, id));
        }
    }

    private static MigrationWarning Warning(string code, string message, string? projectPath = null, string? packageId = null) =>
        new() { Code = code, Message = message, Severity = WarningSeverity.Warning, ProjectPath = projectPath, PackageId = packageId };

    private static MigrationWarning Info(string code, string message, string? projectPath = null, string? packageId = null) =>
        new() { Code = code, Message = message, Severity = WarningSeverity.Info, ProjectPath = projectPath, PackageId = packageId };

    private static string GenerateDirectoryPackagesProps(List<CentralEntry> entries, string newLine)
    {
        var sb = new StringBuilder();
        sb.Append("<Project>").Append(newLine);
        sb.Append("  <PropertyGroup>").Append(newLine);
        sb.Append("    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>").Append(newLine);
        sb.Append("  </PropertyGroup>").Append(newLine);
        sb.Append(newLine);
        sb.Append("  <ItemGroup>").Append(newLine);

        foreach (var entry in entries)
        {
            sb.Append("    ")
              .Append(XmlFileEditor.FormatPackageVersion(entry.Package.Id, entry.Version, entry.Condition))
              .Append(newLine);
        }

        sb.Append("  </ItemGroup>").Append(newLine);
        sb.Append("</Project>").Append(newLine);

        return sb.ToString();
    }

    private static bool IsCentralManagementEnabledByBuildProps(SolutionInfo solution) => solution.Projects.Any(p =>
        p.Kind == ProjectKind.ImportFile &&
        string.Equals(p.RelativePath, DirectoryBuildPropsFileName, StringComparison.OrdinalIgnoreCase) &&
        p.AlreadyUsesCentralManagement);

    private ExistingCentralFile? ReadExistingCentralFile(string rootPath)
    {
        string path = _fileSystem.Combine(rootPath, DirectoryPackagesPropsFileName);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        string content = _fileSystem.ReadAllText(path);
        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new MigrationException(
                ErrorCodes.MalformedProject,
                $"Existing {DirectoryPackagesPropsFileName} is not valid XML: {ex.Message}",
                DirectoryPackagesPropsFileName,
                ex);
        }

        var unconditional = new Dictionary<PackageIdentity, string>();
        var conditional = new HashSet<PackageIdentity>();

        foreach (var item in document.Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
        {
            string? id = item.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var package = new PackageIdentity(id.Trim());
            bool isConditional = item.Attribute("Condition") is not null || item.Parent?.Attribute("Condition") is not null;
            string? version = item.Attribute("Version")?.Value
                ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value.Trim();

            if (isConditional)
            {
                conditional.Add(package);
            }
            else if (version is not null)
            {
                unconditional[package] = version;
            }
        }

        conditional.ExceptWith(unconditional.Keys);

        bool managesCentrally = document.Descendants()
            .Any(e => e.Name.LocalName == "ManagePackageVersionsCentrally" &&
                      string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

        return new ExistingCentralFile(TextFormat.Sha256(content), managesCentrally, unconditional, conditional);
    }

    private sealed record CentralEntry(PackageIdentity Package, string Version, string? Condition);

    private sealed record ExistingCentralFile(
        string Sha256,
        bool ManagesCentrally,
        IReadOnlyDictionary<PackageIdentity, string> UnconditionalVersions,
        IReadOnlySet<PackageIdentity> ConditionalOnly);
}
