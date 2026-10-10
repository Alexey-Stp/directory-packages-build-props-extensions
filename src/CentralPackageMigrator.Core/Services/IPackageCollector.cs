using System.Xml.Linq;
using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Extracts PackageReference items from MSBuild files.
/// </summary>
public interface IPackageCollector
{
    /// <summary>Reads and parses a file, returning its PackageReferences (empty on malformed XML).</summary>
    public IReadOnlyList<PackageReference> CollectFromProject(string projectPath, string solutionRoot);

    /// <summary>
    /// Collects PackageReferences from an already-parsed document (parsed with line info).
    /// <paramref name="inheritedProperties"/> (e.g. from the root Directory.Build.props) are
    /// used to resolve <c>$(Name)</c> versions, overridden by the file's own properties.
    /// </summary>
    public IReadOnlyList<PackageReference> CollectFromDocument(
        XDocument document,
        string relativePath,
        IReadOnlyDictionary<string, string>? inheritedProperties);
}
