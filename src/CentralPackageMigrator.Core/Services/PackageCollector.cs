using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed partial class PackageCollector : IPackageCollector
{
    private const int MaxPropertyExpansionDepth = 10;

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

        return CollectFromDocument(doc, relativePath, inheritedProperties: null);
    }

    public IReadOnlyList<PackageReference> CollectFromDocument(
        XDocument document,
        string relativePath,
        IReadOnlyDictionary<string, string>? inheritedProperties)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (document.Root is null)
        {
            return [];
        }

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (inheritedProperties is not null)
        {
            foreach (var (name, value) in inheritedProperties)
            {
                properties[name] = value;
            }
        }

        foreach (var (name, value) in ReadUnconditionalProperties(document))
        {
            properties[name] = value;
        }

        var references = new List<PackageReference>();

        var elements = document.Root
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference");

        foreach (var element in elements)
        {
            var packageRef = ParsePackageReference(element, relativePath, properties);
            if (packageRef is not null)
            {
                references.Add(packageRef);
            }
        }

        _logger.LogDebug("Collected {Count} PackageReference(s) from {Path}", references.Count, relativePath);
        return references.AsReadOnly();
    }

    /// <summary>
    /// Reads properties defined outside any condition. Later definitions win, as in MSBuild.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ReadUnconditionalProperties(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Root is null)
        {
            return properties;
        }

        var groups = document.Root
            .Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup" && e.Attribute("Condition") is null);

        foreach (var group in groups)
        {
            foreach (var property in group.Elements().Where(p => p.Attribute("Condition") is null && !p.HasElements))
            {
                properties[property.Name.LocalName] = property.Value.Trim();
            }
        }

        return properties;
    }

    private PackageReference? ParsePackageReference(
        XElement element,
        string projectRelativePath,
        IReadOnlyDictionary<string, string> properties)
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

        string? version = GetMetadata(element, "Version");

        return new PackageReference
        {
            Package = new PackageIdentity(packageId.Trim()),
            Version = version,
            EffectiveVersion = version is null ? null : ExpandProperties(version, properties),
            VersionOverride = GetMetadata(element, "VersionOverride"),
            IsVersionAttribute = element.Attribute("Version") is not null,
            IsUpdate = updateValue is not null && includeValue is null,
            Condition = CombineConditions(element.Parent?.Attribute("Condition")?.Value, element.Attribute("Condition")?.Value),
            PrivateAssets = GetMetadata(element, "PrivateAssets"),
            IncludeAssets = GetMetadata(element, "IncludeAssets"),
            ExcludeAssets = GetMetadata(element, "ExcludeAssets"),
            Line = GetLineNumber(element),
            Column = GetColumnNumber(element),
            ProjectRelativePath = projectRelativePath
        };
    }

    /// <summary>Substitutes known <c>$(Name)</c> references; unknown ones are left in place.</summary>
    private static string ExpandProperties(string value, IReadOnlyDictionary<string, string> properties)
    {
        string current = value;
        for (int depth = 0; depth < MaxPropertyExpansionDepth && current.Contains("$(", StringComparison.Ordinal); depth++)
        {
            string next = PropertyReferenceRegex().Replace(
                current,
                m => properties.TryGetValue(m.Groups[1].Value, out string? resolved) ? resolved : m.Value);

            if (next == current)
            {
                break;
            }

            current = next;
        }

        return current;
    }

    private static string? CombineConditions(string? groupCondition, string? itemCondition)
    {
        if (string.IsNullOrWhiteSpace(groupCondition))
        {
            return string.IsNullOrWhiteSpace(itemCondition) ? null : itemCondition;
        }

        if (string.IsNullOrWhiteSpace(itemCondition) ||
            string.Equals(groupCondition.Trim(), itemCondition.Trim(), StringComparison.Ordinal))
        {
            return groupCondition;
        }

        return $"({groupCondition}) And ({itemCondition})";
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

    [GeneratedRegex(@"\$\(([A-Za-z_][A-Za-z0-9_.\-]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyReferenceRegex();
}
