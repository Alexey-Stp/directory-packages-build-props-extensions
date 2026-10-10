using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CentralPackageMigrator.TestUtilities;

/// <summary>
/// The Core services wired together over a given file system, for tests that run the
/// whole pipeline without dependency injection.
/// </summary>
public sealed class Pipeline
{
    public Pipeline(IFileSystem fileSystem, TimeProvider? timeProvider = null)
    {
        FileSystem = fileSystem;
        Collector = new PackageCollector(fileSystem, NullLogger<PackageCollector>.Instance);
        Detector = new SolutionDetector(fileSystem, NullLogger<SolutionDetector>.Instance);
        Scanner = new ProjectScanner(fileSystem, Collector, NullLogger<ProjectScanner>.Instance);
        ConflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        Planner = new MigrationPlanner(
            ConflictDetector, fileSystem, NullLogger<MigrationPlanner>.Instance, timeProvider ?? new FixedTimeProvider());
        Engine = new FileEditEngine(fileSystem, NullLogger<FileEditEngine>.Instance);
        Service = new MigrationService(
            Detector, Scanner, Planner, Engine, fileSystem, NullLogger<MigrationService>.Instance);
    }

    public IFileSystem FileSystem { get; }

    public PackageCollector Collector { get; }

    public SolutionDetector Detector { get; }

    public ProjectScanner Scanner { get; }

    public ConflictDetector ConflictDetector { get; }

    public MigrationPlanner Planner { get; }

    public FileEditEngine Engine { get; }

    public MigrationService Service { get; }

    /// <summary>A request that uses <paramref name="root"/> as the explicit solution root.</summary>
    public static MigrationRequest RequestFor(string root, params Core.Models.ConflictResolution[] resolutions) => new()
    {
        RootPath = root,
        WorkingDirectory = root,
        Resolutions = resolutions
    };
}
