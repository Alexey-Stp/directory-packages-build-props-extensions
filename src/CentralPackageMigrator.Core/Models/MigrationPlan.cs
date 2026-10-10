namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// The complete migration plan — a deterministic, serializable representation
/// of all changes needed to migrate to Central Package Management.
/// This is the primary contract between the CLI engine and IDE adapters.
/// </summary>
public sealed record MigrationPlan
{
    /// <summary>Current JSON contract version written by this build.</summary>
    public const string CurrentSchemaVersion = "1.1";

    /// <summary>The major schema version this build can read.</summary>
    public const int SupportedSchemaMajor = 1;

    /// <summary>Schema version for forward compatibility with IDE adapters.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>UTC timestamp when this plan was generated.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Absolute path to the solution root.</summary>
    public required string SolutionRoot { get; init; }

    /// <summary>Files to be created (e.g., Directory.Packages.props).</summary>
    public IReadOnlyList<FileCreate> Creates { get; init; } = [];

    /// <summary>Files to be edited (project files, import files, an existing Directory.Packages.props).</summary>
    public IReadOnlyList<FileEdit> Edits { get; init; } = [];

    /// <summary>Version conflicts requiring resolution.</summary>
    public IReadOnlyList<ConflictGroup> Conflicts { get; init; } = [];

    /// <summary>Non-fatal warnings (e.g., legacy project detected).</summary>
    public IReadOnlyList<MigrationWarning> Warnings { get; init; } = [];

    /// <summary>Aggregate statistics about the migration.</summary>
    public required MigrationStats Stats { get; init; }

    /// <summary>True if there are unresolved conflicts blocking the migration.</summary>
    public bool HasUnresolvedConflicts => Conflicts.Any(c => c.ResolvedVersion is null);

    /// <summary>True if any warning has <see cref="WarningSeverity.Warning"/> severity.</summary>
    public bool HasWarnings => Warnings.Any(w => w.Severity == WarningSeverity.Warning);

    /// <summary>True if the plan produces no changes (already migrated or empty).</summary>
    public bool IsNoOp => Creates.Count == 0 && Edits.Count == 0;

    /// <summary>The conflict resolutions recorded in this plan.</summary>
    public IReadOnlyList<ConflictResolution> GetResolutions() => Conflicts
        .Where(c => c.ResolvedVersion is not null)
        .Select(c => new ConflictResolution { PackageId = c.Package.Id, Version = c.ResolvedVersion! })
        .ToList()
        .AsReadOnly();
}
