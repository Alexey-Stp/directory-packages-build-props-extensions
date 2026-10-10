using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Security;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Input shared by every migration operation.
/// </summary>
public sealed record MigrationRequest
{
    /// <summary>Explicit solution root (<c>--root</c>). When set it is used as-is, without walking up.</summary>
    public string? RootPath { get; init; }

    /// <summary>Directory to start root detection from when <see cref="RootPath"/> is not set.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Conflict resolutions to apply.</summary>
    public IReadOnlyList<ConflictResolution> Resolutions { get; init; } = [];
}

/// <summary>Result of <see cref="IMigrationService.Analyze"/>.</summary>
public sealed record AnalysisResult
{
    public required SolutionInfo Solution { get; init; }
    public required MigrationPlan Plan { get; init; }
}

/// <summary>Result of applying a plan.</summary>
public sealed record ApplyResult
{
    public string SchemaVersion { get; init; } = MigrationPlan.CurrentSchemaVersion;
    public required bool DryRun { get; init; }
    public required IReadOnlyList<string> WrittenFiles { get; init; }
    public required MigrationPlan Plan { get; init; }
}

/// <summary>
/// Orchestrates detect → scan → plan → apply. The single entry point for the CLI and
/// for in-process IDE hosts.
/// </summary>
public interface IMigrationService
{
    /// <summary>Detects the root and scans projects. Throws <see cref="MigrationException"/> when nothing is found.</summary>
    public SolutionInfo LoadSolution(MigrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Scans the solution and computes a plan without resolutions.</summary>
    public AnalysisResult Analyze(MigrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Scans the solution and computes a plan using <see cref="MigrationRequest.Resolutions"/>.</summary>
    public MigrationPlan CreatePlan(MigrationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Plans and applies in one step.</summary>
    public ApplyResult Apply(MigrationRequest request, bool dryRun, CancellationToken cancellationToken = default);

    /// <summary>Applies a plan computed in this process by <see cref="CreatePlan"/>.</summary>
    public ApplyResult ApplyPlan(MigrationPlan plan, bool dryRun, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a previously saved plan. The plan is never trusted: it must target the same root,
    /// and a plan regenerated from the current files (with the saved resolutions) must match it
    /// exactly. Otherwise the plan is rejected as stale or tampered with.
    /// </summary>
    public ApplyResult ApplySavedPlan(MigrationPlan savedPlan, MigrationRequest request, bool dryRun, CancellationToken cancellationToken = default);
}

public sealed class MigrationService : IMigrationService
{
    private readonly ISolutionDetector _detector;
    private readonly IProjectScanner _scanner;
    private readonly IMigrationPlanner _planner;
    private readonly IFileEditEngine _engine;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(
        ISolutionDetector detector,
        IProjectScanner scanner,
        IMigrationPlanner planner,
        IFileEditEngine engine,
        IFileSystem fileSystem,
        ILogger<MigrationService> logger)
    {
        _detector = detector;
        _scanner = scanner;
        _planner = planner;
        _engine = engine;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public SolutionInfo LoadSolution(MigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string start = request.RootPath ?? request.WorkingDirectory;
        string fullStart = _fileSystem.GetFullPath(start);
        if (!_fileSystem.DirectoryExists(fullStart))
        {
            throw new MigrationException(ErrorCodes.SolutionNotFound, $"Directory not found: {fullStart}", fullStart);
        }

        var solution = _detector.Detect(fullStart, isExplicitRoot: request.RootPath is not null);
        var projects = _scanner.ScanProjects(solution.RootPath, solution.SolutionFilePath, cancellationToken);

        if (!projects.Any(p => p.Kind == ProjectKind.Project && !p.IsOutsideRoot))
        {
            throw new MigrationException(
                ErrorCodes.NoProjectsFound,
                $"No project files (.csproj, .fsproj, .vbproj) found under {solution.RootPath}",
                solution.RootPath);
        }

        return solution with { Projects = projects };
    }

    public AnalysisResult Analyze(MigrationRequest request, CancellationToken cancellationToken = default)
    {
        var solution = LoadSolution(request, cancellationToken);
        var plan = _planner.CreatePlan(solution, []);
        return new AnalysisResult { Solution = solution, Plan = plan };
    }

    public MigrationPlan CreatePlan(MigrationRequest request, CancellationToken cancellationToken = default)
    {
        var solution = LoadSolution(request, cancellationToken);
        return _planner.CreatePlan(solution, request.Resolutions);
    }

    public ApplyResult Apply(MigrationRequest request, bool dryRun, CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan(request, cancellationToken);
        return ApplyPlan(plan, dryRun, cancellationToken);
    }

    public ApplyResult ApplyPlan(MigrationPlan plan, bool dryRun, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var written = _engine.Apply(plan, dryRun, cancellationToken);
        return new ApplyResult { DryRun = dryRun, WrittenFiles = written, Plan = plan };
    }

    public ApplyResult ApplySavedPlan(
        MigrationPlan savedPlan,
        MigrationRequest request,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(savedPlan);
        ArgumentNullException.ThrowIfNull(request);

        ValidateSchemaVersion(savedPlan.SchemaVersion);

        var solution = LoadSolution(request, cancellationToken);
        string savedRoot = _fileSystem.GetFullPath(savedPlan.SolutionRoot);
        if (!PathSafety.PathsEqual(savedRoot, solution.RootPath))
        {
            throw new MigrationException(
                ErrorCodes.InvalidPlanFile,
                $"The plan was created for '{savedRoot}' but the current solution root is '{solution.RootPath}'. " +
                "Run from that directory or pass --root.");
        }

        var current = _planner.CreatePlan(solution, savedPlan.GetResolutions());
        string? difference = FindDifference(savedPlan, current);
        if (difference is not null)
        {
            _logger.LogDebug("Saved plan differs from current state: {Difference}", difference);
            throw new MigrationException(
                ErrorCodes.StalePlan,
                $"The plan file does not match the current state of the solution ({difference}). " +
                "Files changed since it was created, or the plan was modified. Regenerate it.");
        }

        return ApplyPlan(current, dryRun, cancellationToken);
    }

    private static void ValidateSchemaVersion(string? schemaVersion)
    {
        string? major = schemaVersion?.Split('.')[0];
        if (!int.TryParse(major, out int parsed) || parsed != MigrationPlan.SupportedSchemaMajor)
        {
            throw new MigrationException(
                ErrorCodes.InvalidPlanFile,
                $"Unsupported plan schema version '{schemaVersion}'. Expected {MigrationPlan.SupportedSchemaMajor}.x.");
        }
    }

    private static string? FindDifference(MigrationPlan saved, MigrationPlan current)
    {
        if (saved.Creates.Count != current.Creates.Count || saved.Edits.Count != current.Edits.Count)
        {
            return "different number of files";
        }

        foreach (var (a, b) in saved.Creates.Zip(current.Creates))
        {
            if (!string.Equals(a.RelativePath, b.RelativePath, PathSafety.PathComparison) ||
                !string.Equals(a.Content, b.Content, StringComparison.Ordinal))
            {
                return $"content of {b.RelativePath}";
            }
        }

        foreach (var (a, b) in saved.Edits.Zip(current.Edits))
        {
            if (!string.Equals(a.RelativePath, b.RelativePath, PathSafety.PathComparison) ||
                !string.Equals(a.OriginalSha256, b.OriginalSha256, StringComparison.OrdinalIgnoreCase) ||
                a.Operations.Count != b.Operations.Count)
            {
                return $"edits to {b.RelativePath}";
            }

            bool sameOperations = a.Operations.Zip(b.Operations).All(pair =>
                pair.First.Type == pair.Second.Type &&
                pair.First.Line == pair.Second.Line &&
                string.Equals(pair.First.PackageId, pair.Second.PackageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.First.Version, pair.Second.Version, StringComparison.Ordinal) &&
                string.Equals(pair.First.Condition, pair.Second.Condition, StringComparison.Ordinal));

            if (!sameOperations)
            {
                return $"operations on {b.RelativePath}";
            }
        }

        return null;
    }
}
