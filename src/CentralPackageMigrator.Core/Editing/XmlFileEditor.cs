using System.Text;
using System.Xml;
using System.Xml.Linq;
using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Editing;

/// <summary>
/// Applies <see cref="EditOperation"/>s to the text of an MSBuild file by replacing exact
/// character spans, then re-parses the result and verifies every operation took effect.
/// </summary>
internal static class XmlFileEditor
{
    private const string PackageReference = "PackageReference";
    private const string PackageVersion = "PackageVersion";
    private const string ManageCentrally = "ManagePackageVersionsCentrally";

    /// <summary>Returns the edited text. Throws <see cref="MigrationException"/> on failure.</summary>
    public static string Apply(string content, IReadOnlyList<EditOperation> operations, string displayPath)
    {
        var source = ParseOrThrow(content, displayPath);
        var edits = new TextEdits();
        var collapseCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string newLine = TextFormat.DetectNewLine(content);

        foreach (var op in operations.Where(o => o.Type == EditOperationType.RemoveVersion))
        {
            RemoveVersion(source, edits, op, collapseCandidates, displayPath);
        }

        foreach (var op in operations.Where(o => o.Type == EditOperationType.UpdatePackageVersion))
        {
            UpdatePackageVersion(source, edits, op, displayPath);
        }

        if (operations.Any(o => o.Type == EditOperationType.EnableCentralPackageManagement))
        {
            EnableCentralManagement(source, edits, newLine);
        }

        var additions = operations.Where(o => o.Type == EditOperationType.AddPackageVersion).ToList();
        if (additions.Count > 0)
        {
            AddPackageVersions(source, edits, additions, newLine, displayPath);
        }

        string result = edits.ApplyTo(content);

        if (collapseCandidates.Count > 0)
        {
            result = CollapseEmptyReferences(result, collapseCandidates, displayPath);
        }

        Verify(result, operations, displayPath);
        return result;
    }

    private static void RemoveVersion(
        XmlSource source,
        TextEdits edits,
        EditOperation op,
        HashSet<string> collapseCandidates,
        string displayPath)
    {
        string packageId = RequirePackageId(op, displayPath);

        var candidates = IncludeReferences(source.Root, packageId).ToList();
        var onLine = candidates.Where(e => XmlSource.LineOf(e) == op.Line).ToList();
        var targets = op.Line > 0 && onLine.Count > 0 ? onLine : candidates;

        foreach (var element in targets)
        {
            var attribute = element.Attribute("Version");
            if (attribute is not null)
            {
                var (start, end) = source.AttributeSpan(attribute);

                if (attribute.IsFirstAttributeOf(element))
                {
                    // <PackageReference Version="1" Include="X" />: take the following whitespace,
                    // so the element name keeps its separator from the next attribute.
                    while (end < source.Text.Length && char.IsWhiteSpace(source.Text[end]))
                    {
                        end++;
                    }
                }
                else
                {
                    // Take the whitespace separating it from the previous attribute with it.
                    while (start > 0 && char.IsWhiteSpace(source.Text[start - 1]))
                    {
                        start--;
                    }
                }

                edits.Remove(start, end);
            }

            foreach (var child in element.Elements().Where(c => c.Name.LocalName == "Version"))
            {
                var (start, end) = source.ElementLineSpan(child);
                edits.Remove(start, end);
                collapseCandidates.Add(packageId);
            }
        }
    }

    private static void UpdatePackageVersion(XmlSource source, TextEdits edits, EditOperation op, string displayPath)
    {
        string packageId = RequirePackageId(op, displayPath);
        string version = RequireVersion(op, displayPath);

        var element = UnconditionalPackageVersions(source.Root, packageId).FirstOrDefault()
            ?? throw Verification(displayPath, $"PackageVersion for '{packageId}' to update was not found.");

        var attribute = element.Attribute("Version");
        if (attribute is not null)
        {
            var (start, end) = source.AttributeValueSpan(attribute);
            edits.Replace(start, end, TextFormat.EscapeAttribute(version));
            return;
        }

        var child = element.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")
            ?? throw Verification(displayPath, $"PackageVersion for '{packageId}' has no Version to update.");

        var (innerStart, innerEnd) = source.InnerSpan(child);
        edits.Replace(innerStart, innerEnd, System.Security.SecurityElement.Escape(version));
    }

    private static void EnableCentralManagement(XmlSource source, TextEdits edits, string newLine)
    {
        var existing = source.Root.Descendants()
            .Where(e => e.Name.LocalName == ManageCentrally && !e.HasElements)
            .ToList();

        if (existing.Count > 0)
        {
            foreach (var element in existing)
            {
                var (start, end) = source.InnerSpan(element);
                edits.Replace(start, end, "true");
            }

            return;
        }

        string unit = IndentUnit(source);
        int insertAt = source.StartTagEnd(source.Root);
        string text = newLine
            + unit + "<PropertyGroup>" + newLine
            + unit + unit + $"<{ManageCentrally}>true</{ManageCentrally}>" + newLine
            + unit + "</PropertyGroup>";

        edits.Insert(insertAt, text);
    }

    private static void AddPackageVersions(
        XmlSource source,
        TextEdits edits,
        List<EditOperation> additions,
        string newLine,
        string displayPath)
    {
        var items = additions
            .Select(op => FormatPackageVersion(RequirePackageId(op, displayPath), RequireVersion(op, displayPath), op.Condition))
            .ToList();

        var itemGroup = source.Root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup" && e.Attribute("Condition") is null)
            .LastOrDefault(e => e.Elements().Any(c => c.Name.LocalName == PackageVersion));

        if (itemGroup is not null)
        {
            int endTag = source.EndTagStart(itemGroup);
            var sample = itemGroup.Elements().Last(c => c.Name.LocalName == PackageVersion);
            string indent = source.IndentationAt(source.ElementStart(sample));

            if (source.IsFirstOnLine(endTag) && indent.Length > 0)
            {
                var sb = new StringBuilder();
                foreach (string item in items)
                {
                    sb.Append(indent).Append(item).Append(newLine);
                }

                edits.Insert(source.LineStartOf(endTag), sb.ToString());
            }
            else
            {
                edits.Insert(endTag, string.Concat(items));
            }

            return;
        }

        // No suitable ItemGroup: add one before </Project>.
        string unit = IndentUnit(source);
        int projectEnd = source.EndTagStart(source.Root);
        var group = new StringBuilder();
        group.Append(unit).Append("<ItemGroup>").Append(newLine);
        foreach (string item in items)
        {
            group.Append(unit).Append(unit).Append(item).Append(newLine);
        }

        group.Append(unit).Append("</ItemGroup>").Append(newLine);

        if (source.IsFirstOnLine(projectEnd))
        {
            edits.Insert(source.LineStartOf(projectEnd), newLine + group);
        }
        else
        {
            edits.Insert(projectEnd, newLine + group);
        }
    }

    /// <summary>
    /// Turns <c>&lt;PackageReference Include="X"&gt;&lt;/PackageReference&gt;</c> (left empty after
    /// removing a nested Version element) into <c>&lt;PackageReference Include="X" /&gt;</c>.
    /// </summary>
    private static string CollapseEmptyReferences(string content, HashSet<string> packageIds, string displayPath)
    {
        var source = ParseOrThrow(content, displayPath);
        var edits = new TextEdits();

        foreach (var element in source.Root.Descendants().Where(e => e.Name.LocalName == PackageReference))
        {
            string? id = element.Attribute("Include")?.Value;
            if (id is null || !packageIds.Contains(id) || element.IsEmpty)
            {
                continue;
            }

            bool onlyWhitespace = element.Nodes().All(n => n is XText t && string.IsNullOrWhiteSpace(t.Value));
            if (!onlyWhitespace)
            {
                continue;
            }

            int gt = source.StartTagEnd(element) - 1;
            int start = gt;
            while (char.IsWhiteSpace(source.Text[start - 1]))
            {
                start--;
            }

            edits.Replace(start, source.ElementEnd(element), " />");
        }

        return edits.Count == 0 ? content : edits.ApplyTo(content);
    }

    private static void Verify(string result, IReadOnlyList<EditOperation> operations, string displayPath)
    {
        var root = ParseOrThrow(result, displayPath, ErrorCodes.VerificationFailed).Root;

        foreach (var op in operations)
        {
            string? id = op.PackageId;
            switch (op.Type)
            {
                case EditOperationType.RemoveVersion:
                    if (IncludeReferences(root, id!).Any(e =>
                            e.Attribute("Version") is not null ||
                            e.Elements().Any(c => c.Name.LocalName == "Version")))
                    {
                        throw Verification(displayPath, $"A version for '{id}' is still declared after editing.");
                    }

                    break;

                case EditOperationType.AddPackageVersion:
                case EditOperationType.UpdatePackageVersion:
                    bool present = root.Descendants()
                        .Where(e => e.Name.LocalName == PackageVersion)
                        .Any(e => string.Equals(e.Attribute("Include")?.Value, id, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(VersionOf(e), op.Version, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(e.Attribute("Condition")?.Value, op.Condition, StringComparison.Ordinal));
                    if (!present)
                    {
                        throw Verification(displayPath, $"PackageVersion '{id}' = '{op.Version}' is missing after editing.");
                    }

                    break;

                case EditOperationType.EnableCentralPackageManagement:
                    bool enabled = root.Descendants()
                        .Any(e => e.Name.LocalName == ManageCentrally &&
                                  string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
                    if (!enabled)
                    {
                        throw Verification(displayPath, $"{ManageCentrally} is not enabled after editing.");
                    }

                    break;
            }
        }
    }

    public static string FormatPackageVersion(string packageId, string version, string? condition)
    {
        string conditionAttribute = condition is null
            ? string.Empty
            : $" Condition=\"{TextFormat.EscapeAttribute(condition)}\"";

        return $"<{PackageVersion} Include=\"{TextFormat.EscapeAttribute(packageId)}\" " +
               $"Version=\"{TextFormat.EscapeAttribute(version)}\"{conditionAttribute} />";
    }

    private static bool IsFirstAttributeOf(this XAttribute attribute, XElement element) =>
        element.Attributes().FirstOrDefault(a => !a.IsNamespaceDeclaration) == attribute;

    private static IEnumerable<XElement> IncludeReferences(XElement root, string packageId) => root
        .Descendants()
        .Where(e => e.Name.LocalName == PackageReference &&
                    string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> UnconditionalPackageVersions(XElement root, string packageId) => root
        .Descendants()
        .Where(e => e.Name.LocalName == PackageVersion &&
                    e.Attribute("Condition") is null &&
                    e.Parent?.Attribute("Condition") is null &&
                    string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase));

    private static string? VersionOf(XElement element) =>
        element.Attribute("Version")?.Value
        ?? element.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value.Trim();

    private static string IndentUnit(XmlSource source)
    {
        var firstChild = source.Root.Elements().FirstOrDefault();
        if (firstChild is null)
        {
            return "  ";
        }

        string indent = source.IndentationAt(source.ElementStart(firstChild));
        return indent.Length > 0 ? indent : "  ";
    }

    private static XmlSource ParseOrThrow(string content, string displayPath, string code = ErrorCodes.MalformedProject)
    {
        try
        {
            return XmlSource.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new MigrationException(code, $"Failed to parse '{displayPath}': {ex.Message}", displayPath, ex);
        }
    }

    private static string RequirePackageId(EditOperation op, string displayPath) =>
        op.PackageId ?? throw new MigrationException(
            ErrorCodes.InvalidPlanFile, $"{op.Type} operation without a package ID for '{displayPath}'.", displayPath);

    private static string RequireVersion(EditOperation op, string displayPath) =>
        op.Version ?? throw new MigrationException(
            ErrorCodes.InvalidPlanFile, $"{op.Type} operation without a version for '{displayPath}'.", displayPath);

    private static MigrationException Verification(string displayPath, string message) =>
        new(ErrorCodes.VerificationFailed, $"{displayPath}: {message}", displayPath);
}
