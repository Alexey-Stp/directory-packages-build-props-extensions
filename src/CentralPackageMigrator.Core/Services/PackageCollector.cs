using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class PackageCollector : IPackageCollector
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<PackageCollector> _logger;

    public PackageCollector(IFileSystem fileSystem, ILogger<PackageCollector> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<PackageReference> CollectFromProject(string projectPath, string solutionRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionRoot);

        string relativePath = _fileSystem.GetRelativePath(solutionRoot, projectPath);
        string content = _fileSystem.ReadAllText(projectPath);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            _logger.LogWarning("Failed to parse XML in {Path}: {Error}", relativePath, ex.Message);
            return [];
        }

        if (doc.Root is null)
        {
            return [];
        }

        var references = new List<PackageReference>();

        var elements = doc.Root
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference");

        foreach (var element in elements)
        {
            var packageRef = ParsePackageReference(element, relativePath);
            if (packageRef is not null)
            {
                references.Add(packageRef);
            }
        }

        _logger.LogDebug("Collected {Count} PackageReference(s) from {Path}", references.Count, relativePath);
        return references.AsReadOnly();
    }

    private PackageReference? ParsePackageReference(XElement element, string projectRelativePath)
    {
        // Get the package ID from Include= or Update=
        string? includeValue = element.Attribute("Include")?.Value;
        string? updateValue = element.Attribute("Update")?.Value;
        string? packageId = includeValue ?? updateValue;

        if (string.IsNullOrWhiteSpace(packageId))
        {
            _logger.LogWarning(
                "PackageReference without Include or Update attribute in {Path} at line {Line}",
                projectRelativePath, GetLineNumber(element));
            return null;
        }

        // Extract version: first check attribute, then nested element
        string? version = null;
        bool isVersionAttribute = false;

        var versionAttr = element.Attribute("Version");
        if (versionAttr is not null)
        {
            version = versionAttr.Value;
            isVersionAttribute = true;
        }
        else
        {
            var versionElement = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Version");
            if (versionElement is not null)
            {
                version = versionElement.Value.Trim();
                isVersionAttribute = false;
            }
        }

        // Extract metadata
        string? condition = element.Attribute("Condition")?.Value;
        string? privateAssets = GetMetadata(element, "PrivateAssets");
        string? includeAssets = GetMetadata(element, "IncludeAssets");
        string? excludeAssets = GetMetadata(element, "ExcludeAssets");

        return new PackageReference
        {
            Package = new PackageIdentity(packageId),
            Version = version,
            IsVersionAttribute = isVersionAttribute,
            IsUpdate = updateValue is not null && includeValue is null,
            Condition = condition,
            PrivateAssets = privateAssets,
            IncludeAssets = includeAssets,
            ExcludeAssets = excludeAssets,
            Line = GetLineNumber(element),
            Column = GetColumnNumber(element),
            ProjectRelativePath = projectRelativePath
        };
    }

    private static string? GetMetadata(XElement packageRef, string metadataName)
    {
        // Check attribute first
        var attr = packageRef.Attribute(metadataName);
        if (attr is not null)
        {
            return attr.Value;
        }

        // Check nested element
        var childElement = packageRef.Elements()
            .FirstOrDefault(e => e.Name.LocalName == metadataName);

        return childElement?.Value.Trim();
    }

    private static int GetLineNumber(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
    }

    private static int GetColumnNumber(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo() ? lineInfo.LinePosition : 0;
    }
}
