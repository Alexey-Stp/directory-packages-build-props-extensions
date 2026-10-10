using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Golden.Tests;

public sealed class GoldenFileTests
{
    private static string TestDataRoot => Path.Combine(
        AppContext.BaseDirectory, "TestData");

    [Fact]
    public void SimpleSolution_ProducesCorrectPlan()
    {
        var fs = LoadScenario("simple-solution");
        string root = Path.GetFullPath("/test/simple-solution");

        var collector = new PackageCollector(fs, NullLogger<PackageCollector>.Instance);
        var scanner = new ProjectScanner(fs, collector, NullLogger<ProjectScanner>.Instance);
        var conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        var planner = new MigrationPlanner(conflictDetector, fs, NullLogger<MigrationPlanner>.Instance);

        var solution = new SolutionInfo
        {
            RootPath = root,
            SolutionFilePath = Path.Combine(root, "SimpleSolution.sln"),
            DetectionMethod = RootDetectionMethod.SolutionFile,
            Projects = scanner.ScanProjects(root)
        };

        var plan = planner.CreatePlan(solution, []);

        // 3 unique packages: Newtonsoft.Json, Serilog, FluentValidation
        plan.Stats.UniquePackages.Should().Be(3);
        plan.Stats.SdkStyleProjects.Should().Be(2);
        plan.Stats.ConflictCount.Should().Be(0);
        plan.Stats.FilesCreated.Should().Be(1);
        plan.Stats.FilesModified.Should().Be(2);

        // Directory.Packages.props created
        plan.Creates.Should().HaveCount(1);
        plan.Creates[0].RelativePath.Should().Be("Directory.Packages.props");
        plan.Creates[0].Content.Should().Contain("FluentValidation");
        plan.Creates[0].Content.Should().Contain("Newtonsoft.Json");
        plan.Creates[0].Content.Should().Contain("Serilog");

        // 2 project edits
        plan.Edits.Should().HaveCount(2);

        // No conflicts
        plan.Conflicts.Should().BeEmpty();
        plan.HasUnresolvedConflicts.Should().BeFalse();
    }

    [Fact]
    public void MultiVersionConflict_DetectsConflict()
    {
        var fs = LoadScenario("multi-version-conflict");
        string root = Path.GetFullPath("/test/multi-version-conflict");

        var collector = new PackageCollector(fs, NullLogger<PackageCollector>.Instance);
        var scanner = new ProjectScanner(fs, collector, NullLogger<ProjectScanner>.Instance);
        var conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        var planner = new MigrationPlanner(conflictDetector, fs, NullLogger<MigrationPlanner>.Instance);

        var solution = new SolutionInfo
        {
            RootPath = root,
            SolutionFilePath = Path.Combine(root, "Conflict.sln"),
            DetectionMethod = RootDetectionMethod.SolutionFile,
            Projects = scanner.ScanProjects(root)
        };

        var plan = planner.CreatePlan(solution, []);

        // Should have 1 conflict: Newtonsoft.Json (13.0.3 vs 12.0.3)
        plan.Conflicts.Should().HaveCount(1);
        plan.Conflicts[0].Package.Id.Should().Be("Newtonsoft.Json");
        plan.Conflicts[0].Entries.Should().HaveCount(2);
        plan.HasUnresolvedConflicts.Should().BeTrue();

        // Serilog should not be in conflict (both use 3.1.1)
        plan.Conflicts.Should().NotContain(c => c.Package.Id == "Serilog");
    }

    [Fact]
    public void MultiVersionConflict_WithResolution_Resolves()
    {
        var fs = LoadScenario("multi-version-conflict");
        string root = Path.GetFullPath("/test/multi-version-conflict");

        var collector = new PackageCollector(fs, NullLogger<PackageCollector>.Instance);
        var scanner = new ProjectScanner(fs, collector, NullLogger<ProjectScanner>.Instance);
        var conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        var planner = new MigrationPlanner(conflictDetector, fs, NullLogger<MigrationPlanner>.Instance);

        var solution = new SolutionInfo
        {
            RootPath = root,
            SolutionFilePath = Path.Combine(root, "Conflict.sln"),
            DetectionMethod = RootDetectionMethod.SolutionFile,
            Projects = scanner.ScanProjects(root)
        };

        var resolutions = new[]
        {
            new ConflictResolution { PackageId = "Newtonsoft.Json", Version = "13.0.3" }
        };

        var plan = planner.CreatePlan(solution, resolutions);

        plan.HasUnresolvedConflicts.Should().BeFalse();
        plan.Conflicts[0].ResolvedVersion.Should().Be("13.0.3");

        // Directory.Packages.props should include resolved version
        plan.Creates[0].Content.Should().Contain("Newtonsoft.Json");
        plan.Creates[0].Content.Should().Contain("13.0.3");
    }

    [Fact]
    public void ConditionalReferences_GeneratesWarning()
    {
        var fs = LoadScenario("conditional-references");
        string root = Path.GetFullPath("/test/conditional-references");

        var collector = new PackageCollector(fs, NullLogger<PackageCollector>.Instance);
        var scanner = new ProjectScanner(fs, collector, NullLogger<ProjectScanner>.Instance);
        var conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        var planner = new MigrationPlanner(conflictDetector, fs, NullLogger<MigrationPlanner>.Instance);

        var solution = new SolutionInfo
        {
            RootPath = root,
            DetectionMethod = RootDetectionMethod.CurrentDirectory,
            Projects = scanner.ScanProjects(root)
        };

        var plan = planner.CreatePlan(solution, []);

        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.ConditionalVersionReference);
    }

    [Fact]
    public void ExistingCentralProps_GeneratesWarning()
    {
        var fs = LoadScenario("existing-central-props");
        string root = Path.GetFullPath("/test/existing-central-props");

        var collector = new PackageCollector(fs, NullLogger<PackageCollector>.Instance);
        var scanner = new ProjectScanner(fs, collector, NullLogger<ProjectScanner>.Instance);
        var conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        var planner = new MigrationPlanner(conflictDetector, fs, NullLogger<MigrationPlanner>.Instance);

        var solution = new SolutionInfo
        {
            RootPath = root,
            DetectionMethod = RootDetectionMethod.CurrentDirectory,
            HasExistingDirectoryPackagesProps = true,
            Projects = scanner.ScanProjects(root)
        };

        var plan = planner.CreatePlan(solution, []);

        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.ExistingDirectoryPackagesProps);
    }

    /// <summary>
    /// Migrates TestData/formatting-snapshot/input and compares every resulting file with
    /// TestData/formatting-snapshot/expected, byte for byte. To add a case, edit both trees.
    /// </summary>
    [Fact]
    public void FormattingSnapshot_MatchesExpectedFilesExactly()
    {
        const string scenario = "formatting-snapshot";
        var fs = LoadScenario(Path.Combine(scenario, "input"));
        string root = Path.GetFullPath($"/test/{scenario}/input");
        var pipeline = new Pipeline(fs);

        var result = pipeline.Service.Apply(Pipeline.RequestFor(root), dryRun: false);

        result.Plan.HasUnresolvedConflicts.Should().BeFalse();
        string expectedDir = Path.Combine(TestDataRoot, scenario, "expected");
        var expectedFiles = Directory.EnumerateFiles(expectedDir, "*", SearchOption.AllDirectories).ToList();

        foreach (string expectedFile in expectedFiles)
        {
            string relativePath = Path.GetRelativePath(expectedDir, expectedFile);
            string actual = fs.GetFileContent(Path.Combine(root, relativePath));
            actual.Should().Be(File.ReadAllText(expectedFile), $"{relativePath} should match the expected snapshot");
        }

        fs.EnumerateFiles(root, "*", SearchOption.AllDirectories).Should().HaveCount(expectedFiles.Count);
    }

    /// <summary>
    /// Loads a test scenario from disk into an InMemoryFileSystem rooted at /test/{scenarioName}.
    /// </summary>
    private static InMemoryFileSystem LoadScenario(string scenarioName)
    {
        var fs = new InMemoryFileSystem();
        string scenarioDir = Path.Combine(TestDataRoot, scenarioName);
        string virtualRoot = Path.GetFullPath($"/test/{scenarioName}");

        fs.AddDirectory(virtualRoot);

        foreach (string file in Directory.EnumerateFiles(scenarioDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(scenarioDir, file);
            string virtualPath = Path.Combine(virtualRoot, relativePath);
            string content = File.ReadAllText(file);
            fs.AddFile(virtualPath, content);
        }

        return fs;
    }
}
