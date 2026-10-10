using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class ConflictDetector : IConflictDetector
{
    private const string CentralFileName = "Directory.Packages.props";

    private readonly ILogger<ConflictDetector> _logger;

    public ConflictDetector(ILogger<ConflictDetector> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<ConflictGroup> DetectConflicts(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<PackageIdentity, string>? existingCentralVersions = null)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var conflicts = new List<ConflictGroup>();

        foreach (var group in PackageVersionAnalyzer.GroupCentralizable(projects))
        {
            var references = group.ToList();
            string? existing = null;
            existingCentralVersions?.TryGetValue(group.Key, out existing);

            var decision = PackageVersionAnalyzer.Decide(group.Key, references, existing);
            if (decision.Kind != DecisionKind.Conflict)
            {
                continue;
            }

            var conflict = PackageVersionAnalyzer.BuildConflict(group.Key, references, existing, CentralFileName);

            _logger.LogDebug(
                "Version conflict for {Package}: {Versions}",
                group.Key.Id,
                string.Join(", ", conflict.Entries.Select(e => $"{e.Version} ({e.Projects.Count} file(s))")));

            conflicts.Add(conflict);
        }

        _logger.LogDebug("Detected {Count} version conflict(s)", conflicts.Count);
        return conflicts
            .OrderBy(c => c.Package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }
}
