using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Editing;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Security;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed partial class ProjectScanner : IProjectScanner
{
    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj"
    };

    private static readonly HashSet<string> ImportFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets"
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "TestResults"
    };

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

    public IReadOnlyList<ProjectInfo> ScanProjects(
        string solutionRoot,
        string? solutionFilePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);

        string root = _fileSystem.GetFullPath(solutionRoot);
        _logger.LogDebug("Scanning for project and import files under: {Root}", root);

        // Directory.Packages.props applies to every project below its directory, so all of them
        // are scanned — not just the ones the solution file lists.
        var files = EnumerateCandidateFiles(root, cancellationToken)
            .OrderBy(f => _fileSystem.GetRelativePath(root, f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inheritedProperties = ReadRootProperties(root);
        var results = new List<ProjectInfo>();

        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var kind = ImportFileNames.Contains(_fileSystem.GetFileName(path))
                ? ProjectKind.ImportFile
                : ProjectKind.Project;

            try
            {
                results.Add(AnalyzeFile(path, root, kind, inheritedProperties));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read {Path}", path);
                results.Add(new ProjectInfo
                {
                    AbsolutePath = path,
                    RelativePath = _fileSystem.GetRelativePath(root, path),
                    Name = _fileSystem.GetFileNameWithoutExtension(path),
                    Kind = kind,
                    ParseError = ex.Message
                });
            }
        }

        if (solutionFilePath is not null)
        {
            results.AddRange(FindProjectsOutsideRoot(solutionFilePath, root));
        }

        _logger.LogInformation(
            "Found {Projects} project file(s) and {Imports} import file(s)",
            results.Count(p => p.Kind == ProjectKind.Project),
            results.Count(p => p.Kind == ProjectKind.ImportFile));

        return results.AsReadOnly();
    }

    private IEnumerable<string> EnumerateCandidateFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();

            foreach (string file in _fileSystem.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string name = _fileSystem.GetFileName(file);
                if (ImportFileNames.Contains(name) || ProjectExtensions.Contains(Path.GetExtension(name)))
                {
                    yield return file;
                }
            }

            foreach (string subdirectory in _fileSystem.EnumerateDirectories(directory))
            {
                string name = _fileSystem.GetFileName(subdirectory);
                if (name.StartsWith('.') || ExcludedDirectories.Contains(name))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    private IReadOnlyDictionary<string, string>? ReadRootProperties(string root)
    {
        string path = _fileSystem.Combine(root, "Directory.Build.props");
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            return PackageCollector.ReadUnconditionalProperties(XDocument.Parse(_fileSystem.ReadAllText(path)));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private ProjectInfo AnalyzeFile(
        string path,
        string root,
        ProjectKind kind,
        IReadOnlyDictionary<string, string>? inheritedProperties)
    {
        string relativePath = _fileSystem.GetRelativePath(root, path);
        string name = _fileSystem.GetFileNameWithoutExtension(path);
        string content = _fileSystem.ReadAllText(path);

        var info = new ProjectInfo
        {
            AbsolutePath = path,
            RelativePath = relativePath,
            Name = name,
            Kind = kind,
            ContentSha256 = TextFormat.Sha256(content),
            NewLine = TextFormat.DetectNewLine(content)
        };

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning("Malformed XML in {Path}: {Error}", relativePath, ex.Message);
            return info with { ParseError = ex.Message };
        }

        bool isSdkStyle = kind == ProjectKind.ImportFile || IsSdkStyleProject(doc);

        IReadOnlyList<PackageReference> packageRefs = isSdkStyle
            ? _packageCollector.CollectFromDocument(doc, relativePath, inheritedProperties)
            : [];

        _logger.LogDebug(
            "{Kind} {Name}: SDK={IsSdk}, Packages={Count}",
            kind, name, isSdkStyle, packageRefs.Count);

        return info with
        {
            IsSdkStyle = isSdkStyle,
            TargetFrameworks = ExtractTargetFrameworks(doc),
            PackageReferences = packageRefs,
            AlreadyUsesCentralManagement = DetectCentralManagement(doc)
        };
    }

    /// <summary>
    /// Projects the solution file references that live outside the root. They are reported
    /// (Directory.Packages.props at the root does not apply to them) but never edited.
    /// </summary>
    private IEnumerable<ProjectInfo> FindProjectsOutsideRoot(string solutionFilePath, string root)
    {
        string solutionDirectory = _fileSystem.GetDirectoryName(solutionFilePath);

        IEnumerable<string> listed = [];
        try
        {
            listed = ReadSolutionProjectPaths(_fileSystem.ReadAllText(solutionFilePath), solutionFilePath);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            _logger.LogWarning(ex, "Could not read solution file {Path}", solutionFilePath);
        }

        foreach (string listedPath in listed)
        {
            string normalized = listedPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string fullPath = _fileSystem.GetFullPath(_fileSystem.Combine(solutionDirectory, normalized));

            if (PathSafety.IsUnderRoot(fullPath, root) || !_fileSystem.FileExists(fullPath))
            {
                continue;
            }

            yield return new ProjectInfo
            {
                AbsolutePath = fullPath,
                RelativePath = _fileSystem.GetRelativePath(root, fullPath),
                Name = _fileSystem.GetFileNameWithoutExtension(fullPath),
                IsOutsideRoot = true
            };
        }
    }

    private static IEnumerable<string> ReadSolutionProjectPaths(string content, string solutionFilePath)
    {
        if (solutionFilePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return XDocument.Parse(content)
                .Descendants()
                .Where(e => e.Name.LocalName == "Project")
                .Select(e => e.Attribute("Path")?.Value)
                .OfType<string>()
                .Where(p => ProjectExtensions.Contains(Path.GetExtension(p)))
                .ToList();
        }

        return SlnProjectRegex()
            .Matches(content)
            .Select(m => m.Groups["path"].Value)
            .Where(p => ProjectExtensions.Contains(Path.GetExtension(p)))
            .ToList();
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

        // Also check for <Sdk> child elements or <Import Sdk="..."> imports
        return root.Elements().Any(e =>
            e.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase) ||
            (e.Name.LocalName == "Import" && e.Attribute("Sdk") is not null));
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

    // Project("{type-guid}") = "Name", "relative\path.csproj", "{project-guid}"
    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*""\s*,\s*""(?<path>[^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectRegex();
}
