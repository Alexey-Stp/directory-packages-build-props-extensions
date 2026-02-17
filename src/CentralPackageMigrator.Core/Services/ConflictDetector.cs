using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class ConflictDetector : IConflictDetector
{
    private readonly ILogger<ConflictDetector> _logger;

    public ConflictDetector(ILogger<ConflictDetector> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ConflictGroup> DetectConflicts(IReadOnlyList<ProjectInfo> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        // Collect all package references across all SDK-style projects,
        // grouped by package identity
        var allRefs = projects
            .Where(p => p.IsSdkStyle)
            .SelectMany(p => p.PackageReferences)
            .Where(r => r.Version is not null && !r.IsUpdate)
            .ToList();

        var grouped = allRefs
            .GroupBy(r => r.Package)
            .ToList();

        var conflicts = new List<ConflictGroup>();

        foreach (var group in grouped)
        {
            var versionGroups = group
                .GroupBy(r => r.Version!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (versionGroups.Count <= 1)
            {
                continue;
            }

            var entries = versionGroups
                .Select(vg => new ConflictEntry
                {
                    Version = vg.Key,
                    Projects = vg
                        .Select(r => r.ProjectRelativePath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                        .AsReadOnly()
                })
                .OrderByDescending(e => e.Projects.Count)
                .ThenBy(e => e.Version, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();

            var conflict = new ConflictGroup
            {
                Package = group.Key,
                Entries = entries
            };

            _logger.LogWarning(
                "Version conflict for {Package}: {Versions}",
                group.Key.Id,
                string.Join(", ", entries.Select(e => $"{e.Version} ({e.Projects.Count} project(s))")));

            conflicts.Add(conflict);
        }

        _logger.LogInformation("Detected {Count} version conflict(s)", conflicts.Count);
        return conflicts
            .OrderBy(c => c.Package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }
}
