using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class ProjectScanner : IProjectScanner
{
    private readonly IFileSystem _fileSystem;
    private readonly IPackageCollector _packageCollector;
    private readonly ILogger<ProjectScanner> _logger;

    public ProjectScanner(
        IFileSystem fileSystem,
        IPackageCollector packageCollector,
        ILogger<ProjectScanner> logger)
    {
        _fileSystem = fileSystem;
        _packageCollector = packageCollector;
        _logger = logger;
    }

    public IReadOnlyList<ProjectInfo> ScanProjects(string solutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);

        _logger.LogDebug("Scanning for .csproj files under: {Root}", solutionRoot);

        var projectFiles = _fileSystem
            .EnumerateFiles(solutionRoot, "*.csproj", SearchOption.AllDirectories)
            .ToList();

        _logger.LogInformation("Found {Count} .csproj file(s)", projectFiles.Count);

        var results = new List<ProjectInfo>();

        foreach (string projectPath in projectFiles)
        {
            try
            {
                var project = AnalyzeProject(projectPath, solutionRoot);
                results.Add(project);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogWarning(ex, "Failed to analyze project: {Path}", projectPath);
                results.Add(new ProjectInfo
                {
                    AbsolutePath = projectPath,
                    RelativePath = _fileSystem.GetRelativePath(solutionRoot, projectPath),
                    Name = _fileSystem.GetFileNameWithoutExtension(projectPath),
                    IsSdkStyle = false
                });
            }
        }

        return results.AsReadOnly();
    }

    private ProjectInfo AnalyzeProject(string projectPath, string solutionRoot)
    {
        string relativePath = _fileSystem.GetRelativePath(solutionRoot, projectPath);
        string name = _fileSystem.GetFileNameWithoutExtension(projectPath);
        string content = _fileSystem.ReadAllText(projectPath);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogWarning("Malformed XML in {Path}: {Error}", relativePath, ex.Message);
            return new ProjectInfo
            {
                AbsolutePath = projectPath,
                RelativePath = relativePath,
                Name = name,
                IsSdkStyle = false
            };
        }

        bool isSdkStyle = IsSdkStyleProject(doc);
        var targetFrameworks = ExtractTargetFrameworks(doc);
        bool usesCpm = DetectCentralManagement(doc);

        IReadOnlyList<PackageReference> packageRefs = isSdkStyle
            ? _packageCollector.CollectFromProject(projectPath, solutionRoot)
            : [];

        _logger.LogDebug(
            "Project {Name}: SDK={IsSdk}, Packages={Count}, CPM={Cpm}",
            name, isSdkStyle, packageRefs.Count, usesCpm);

        return new ProjectInfo
        {
            AbsolutePath = projectPath,
            RelativePath = relativePath,
            Name = name,
            IsSdkStyle = isSdkStyle,
            TargetFrameworks = targetFrameworks,
            PackageReferences = packageRefs,
            AlreadyUsesCentralManagement = usesCpm
        };
    }

    private static bool IsSdkStyleProject(XDocument doc)
    {
        // SDK-style projects have <Project Sdk="..."> attribute
        var root = doc.Root;
        if (root is null)
        {
            return false;
        }

        var sdkAttr = root.Attribute("Sdk");
        if (sdkAttr is not null)
        {
            return true;
        }

        // Also check for <Sdk> child elements
        return root.Elements()
            .Any(e => e.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ExtractTargetFrameworks(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
        {
            return [];
        }

        // Check <TargetFramework> (single)
        var tfElement = root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "TargetFramework");

        if (tfElement is not null && !string.IsNullOrWhiteSpace(tfElement.Value))
        {
            return [tfElement.Value.Trim()];
        }

        // Check <TargetFrameworks> (multi)
        var tfsElement = root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "TargetFrameworks");

        if (tfsElement is not null && !string.IsNullOrWhiteSpace(tfsElement.Value))
        {
            return tfsElement.Value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
                .AsReadOnly();
        }

        return [];
    }

    private static bool DetectCentralManagement(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
        {
            return false;
        }

        // Check for ManagePackageVersionsCentrally property
        var cpmProp = root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ManagePackageVersionsCentrally");

        if (cpmProp is not null &&
            bool.TryParse(cpmProp.Value.Trim(), out bool cpmEnabled) &&
            cpmEnabled)
        {
            return true;
        }

        // Check for any VersionOverride attributes
        return root
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Any(e => e.Attribute("VersionOverride") is not null);
    }
}
