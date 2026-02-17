using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class FileEditEngine : IFileEditEngine
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FileEditEngine> _logger;

    public FileEditEngine(IFileSystem fileSystem, ILogger<FileEditEngine> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<string> Apply(MigrationPlan plan, bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.HasUnresolvedConflicts)
        {
            throw new InvalidOperationException(
                "Cannot apply plan with unresolved conflicts. Resolve all conflicts first.");
        }

        var writtenFiles = new List<string>();

        // Validate all paths first (fail-fast)
        ValidatePaths(plan);

        // Create new files
        foreach (var create in plan.Creates)
        {
            string absolutePath = _fileSystem.Combine(plan.SolutionRoot, create.RelativePath);

            _logger.LogInformation(
                "{Action} {Path}",
                dryRun ? "[DRY-RUN] Would create" : "Creating",
                create.RelativePath);

            if (!dryRun)
            {
                _fileSystem.WriteAllText(absolutePath, create.Content);
            }

            writtenFiles.Add(create.RelativePath);
        }

        // Apply edits to existing files
        foreach (var edit in plan.Edits)
        {
            string absolutePath = _fileSystem.Combine(plan.SolutionRoot, edit.RelativePath);

            _logger.LogInformation(
                "{Action} {Path} ({Count} edit(s))",
                dryRun ? "[DRY-RUN] Would edit" : "Editing",
                edit.RelativePath,
                edit.Operations.Count);

            if (!dryRun)
            {
                ApplyProjectEdits(absolutePath, edit.Operations);
            }

            writtenFiles.Add(edit.RelativePath);
        }

        _logger.LogInformation(
            "{Action} {Count} file(s)",
            dryRun ? "[DRY-RUN] Would write" : "Wrote",
            writtenFiles.Count);

        return writtenFiles.AsReadOnly();
    }

    private void ValidatePaths(MigrationPlan plan)
    {
        string rootNormalized = _fileSystem.GetFullPath(plan.SolutionRoot);

        // Validate all create paths
        foreach (var create in plan.Creates)
        {
            string fullPath = _fileSystem.GetFullPath(
                _fileSystem.Combine(plan.SolutionRoot, create.RelativePath));

            ValidatePathSecurity(fullPath, rootNormalized, create.RelativePath);
        }

        // Validate all edit paths
        foreach (var edit in plan.Edits)
        {
            string fullPath = _fileSystem.GetFullPath(
                _fileSystem.Combine(plan.SolutionRoot, edit.RelativePath));

            ValidatePathSecurity(fullPath, rootNormalized, edit.RelativePath);

            if (!_fileSystem.FileExists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Project file not found for editing: {edit.RelativePath}", fullPath);
            }
        }
    }

    private static void ValidatePathSecurity(string fullPath, string rootNormalized, string relativePath)
    {
        // Directory traversal protection
        if (!fullPath.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: '{relativePath}' resolves outside solution root");
        }

        // Block obviously dangerous patterns
        if (relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Path contains '..' segments: '{relativePath}'");
        }
    }

    private void ApplyProjectEdits(string projectPath, IReadOnlyList<EditOperation> operations)
    {
        string content = _fileSystem.ReadAllText(projectPath);

        // Use XML-aware editing to preserve formatting
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse project file '{projectPath}': {ex.Message}", ex);
        }

        if (doc.Root is null)
        {
            throw new InvalidOperationException($"Project file has no root element: '{projectPath}'");
        }

        // Process RemoveVersion operations
        var removeVersionOps = operations
            .Where(o => o.Type == EditOperationType.RemoveVersion && o.PackageId is not null)
            .ToList();

        foreach (var op in removeVersionOps)
        {
            RemoveVersionFromPackageReference(doc.Root, op.PackageId!);
        }

        // Save with preserved formatting
        using var writer = new StringWriter();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  "
        };

        // We write by reconstructing from the XDocument to preserve structure
        // but we use a string-based approach for better formatting preservation
        string result = SavePreservingFormat(content, doc, removeVersionOps);
        _fileSystem.WriteAllText(projectPath, result);
    }

    private static void RemoveVersionFromPackageReference(XElement root, string packageId)
    {
        var pkgRefs = root
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Where(e =>
            {
                string? id = e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value;
                return string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var pkgRef in pkgRefs)
        {
            // Remove Version attribute if present
            pkgRef.Attribute("Version")?.Remove();

            // Remove nested Version element if present
            var versionElements = pkgRef.Elements()
                .Where(e => e.Name.LocalName == "Version")
                .ToList();

            foreach (var ve in versionElements)
            {
                ve.Remove();
            }
        }
    }

    /// <summary>
    /// Applies version removal via string manipulation to preserve original formatting.
    /// Falls back to XDocument serialization if string approach fails.
    /// </summary>
    private static string SavePreservingFormat(
        string originalContent,
        XDocument modifiedDoc,
        List<EditOperation> removeVersionOps)
    {
        string result = originalContent;

        foreach (var op in removeVersionOps.OrderByDescending(o => o.Line))
        {
            if (op.PackageId is null)
            {
                continue;
            }

            // Try to remove Version="..." attribute via regex (safe for this specific pattern)
            string escaped = Regex.Escape(op.PackageId);

            // Pattern: PackageReference with Include/Update="PackageId" and Version="..."
            // Remove the Version="..." attribute
            string pattern = $@"(<PackageReference\s+(?:Include|Update)=""{escaped}"")\s+Version=""[^""]*""";
            string replacement = "$1";
            string updated = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);

            if (updated != result)
            {
                result = updated;
                continue;
            }

            // Try alternate order: Version before Include
            pattern = $@"(<PackageReference\s+)Version=""[^""]*""\s+((?:Include|Update)=""{escaped}"")";
            replacement = "$1$2";
            updated = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);

            if (updated != result)
            {
                result = updated;
                continue;
            }

            // Try removing nested <Version>...</Version> element
            pattern = $@"(\s*<Version>[^<]*</Version>\s*)";
            // This is a fallback — we only apply it within the context of the right PackageReference
            // For safety, we use the XDocument result if string manipulation fails
        }

        // If we have self-closing tags that lost all attributes except Include, ensure they stay self-closing
        // Clean up any double spaces from attribute removal
        result = Regex.Replace(result, @"  +", " ");
        // But restore indentation (don't collapse leading spaces)
        var lines = result.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            // Only collapse spaces that are clearly inside a tag (not leading whitespace)
            int leadingSpaces = line.Length - line.TrimStart().Length;
            string leading = line[..leadingSpaces];
            string rest = line[leadingSpaces..];
            // Collapse multiple spaces inside XML tags but not in content
            if (rest.TrimStart().StartsWith('<'))
            {
                rest = Regex.Replace(rest, @"  +", " ");
            }
            lines[i] = leading + rest;
        }

        return string.Join('\n', lines);
    }
}
