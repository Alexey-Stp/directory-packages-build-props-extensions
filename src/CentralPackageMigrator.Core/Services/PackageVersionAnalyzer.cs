using System.Text.RegularExpressions;
using CentralPackageMigrator.Core.Models;
using NuGet.Versioning;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Decides how a package's versions can be centralized. Shared by
/// <see cref="ConflictDetector"/> and <see cref="MigrationPlanner"/> so both agree.
/// </summary>
internal static partial class PackageVersionAnalyzer
{
    /// <summary>
    /// Groups the centralizable references of all migratable projects by package,
    /// ordered deterministically (by project path, then line).
    /// </summary>
    public static IEnumerable<IGrouping<PackageIdentity, PackageReference>> GroupCentralizable(
        IEnumerable<ProjectInfo> projects) => projects
        .Where(p => p.IsMigratable)
        .SelectMany(p => p.PackageReferences)
        .Where(r => r.IsCentralizable)
        .OrderBy(r => r.ProjectRelativePath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.Line)
        .GroupBy(r => r.Package);

    public static PackageDecision Decide(
        PackageIdentity package,
        IReadOnlyList<PackageReference> references,
        string? existingCentralVersion)
    {
        var keys = references.Select(r => Normalize(r.EffectiveVersion!)).ToList();
        if (existingCentralVersion is not null)
        {
            keys.Add(Normalize(existingCentralVersion));
        }

        if (keys.Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return new PackageDecision(package, DecisionKind.Single, existingCentralVersion ?? references[0].EffectiveVersion, []);
        }

        if (existingCentralVersion is null && references.All(r => r.Condition is not null))
        {
            var byCondition = references
                .GroupBy(r => NormalizeCondition(r.Condition!), StringComparer.Ordinal)
                .ToList();

            bool consistent = byCondition.All(g =>
                g.Select(r => Normalize(r.EffectiveVersion!)).Distinct(StringComparer.Ordinal).Count() == 1);

            if (consistent)
            {
                var conditional = byCondition
                    .Select(g => new ConditionalVersion(g.First().Condition!, g.First().EffectiveVersion!))
                    .OrderBy(c => c.Condition, StringComparer.Ordinal)
                    .ToList();

                return new PackageDecision(package, DecisionKind.Conditional, null, conditional);
            }
        }

        return new PackageDecision(package, DecisionKind.Conflict, null, []);
    }

    /// <summary>Builds the conflict description for a package that could not be centralized automatically.</summary>
    public static ConflictGroup BuildConflict(
        PackageIdentity package,
        IReadOnlyList<PackageReference> references,
        string? existingCentralVersion,
        string centralFileRelativePath)
    {
        var sources = references
            .Select(r => (Version: r.EffectiveVersion!, Path: r.ProjectRelativePath))
            .ToList();

        if (existingCentralVersion is not null)
        {
            sources.Add((existingCentralVersion, centralFileRelativePath));
        }

        var entries = sources
            .GroupBy(s => Normalize(s.Version), StringComparer.Ordinal)
            .Select(g => new ConflictEntry
            {
                Version = g.First().Version,
                Projects = g
                    .Select(s => s.Path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly()
            })
            .OrderByDescending(e => e.Projects.Count)
            .ThenBy(e => e.Version, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return new ConflictGroup
        {
            Package = package,
            Entries = entries,
            SuggestedVersion = Highest(entries.Select(e => e.Version))
        };
    }

    /// <summary>True if two version strings denote the same NuGet version or range.</summary>
    public static bool AreEquivalent(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static string? Highest(IEnumerable<string> versions)
    {
        string? best = null;
        NuGetVersion? bestVersion = null;
        foreach (string version in versions)
        {
            if (!NuGetVersion.TryParse(version, out var parsed))
            {
                return null;
            }

            if (bestVersion is null || parsed > bestVersion)
            {
                bestVersion = parsed;
                best = version;
            }
        }

        return best;
    }

    private static string Normalize(string version)
    {
        if (NuGetVersion.TryParse(version, out var parsed))
        {
            return parsed.ToNormalizedString().ToUpperInvariant();
        }

        if (VersionRange.TryParse(version, allowFloating: true, out var range))
        {
            return range.ToNormalizedString().ToUpperInvariant();
        }

        return version.Trim().ToUpperInvariant();
    }

    private static string NormalizeCondition(string condition) =>
        WhitespaceRegex().Replace(condition, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

internal enum DecisionKind
{
    /// <summary>All references agree on one version.</summary>
    Single,

    /// <summary>References are all conditional and each condition maps to one version.</summary>
    Conditional,

    /// <summary>Versions disagree; an explicit resolution is required.</summary>
    Conflict
}

internal sealed record ConditionalVersion(string Condition, string Version);

internal sealed record PackageDecision(
    PackageIdentity Package,
    DecisionKind Kind,
    string? Version,
    IReadOnlyList<ConditionalVersion> ConditionalVersions);
