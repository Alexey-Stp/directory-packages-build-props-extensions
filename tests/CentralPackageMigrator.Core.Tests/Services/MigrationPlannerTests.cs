using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class MigrationPlannerTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly ConflictDetector _conflictDetector;
    private readonly MigrationPlanner _sut;

    public MigrationPlannerTests()
    {
        _conflictDetector = new ConflictDetector(NullLogger<ConflictDetector>.Instance);
        _sut = new MigrationPlanner(
            _conflictDetector,
            _fs,
            NullLogger<MigrationPlanner>.Instance);
    }

    [Fact]
    public void Creates_DirectoryPackagesProps_with_all_unique_packages()
    {
        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3"), ("Serilog", "3.1.1")),
            MakeProject("B", ("Newtonsoft.Json", "13.0.3"), ("FluentValidation", "11.9.0"))
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.Creates.Should().HaveCount(1);
        plan.Creates[0].RelativePath.Should().Be("Directory.Packages.props");

        string content = plan.Creates[0].Content;
        content.Should().Contain("ManagePackageVersionsCentrally");
        content.Should().Contain("FluentValidation");
        content.Should().Contain("Newtonsoft.Json");
        content.Should().Contain("Serilog");
    }

    [Fact]
    public void Generates_edit_operations_to_remove_versions()
    {
        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3"))
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.Edits.Should().HaveCount(1);
        plan.Edits[0].Operations.Should().HaveCount(1);
        plan.Edits[0].Operations[0].Type.Should().Be(EditOperationType.RemoveVersion);
        plan.Edits[0].Operations[0].PackageId.Should().Be("Newtonsoft.Json");
    }

    [Fact]
    public void Applies_conflict_resolutions_correctly()
    {
        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProject("B", ("Newtonsoft.Json", "12.0.3"))
        );

        var resolutions = new[]
        {
            new ConflictResolution { PackageId = "Newtonsoft.Json", Version = "13.0.3" }
        };

        var plan = _sut.CreatePlan(solution, resolutions);

        plan.HasUnresolvedConflicts.Should().BeFalse();
        plan.Conflicts.Should().HaveCount(1);
        plan.Conflicts[0].ResolvedVersion.Should().Be("13.0.3");

        // Should include the resolved package in Directory.Packages.props
        plan.Creates.Should().HaveCount(1);
        plan.Creates[0].Content.Should().Contain("Newtonsoft.Json");
        plan.Creates[0].Content.Should().Contain("13.0.3");
    }

    [Fact]
    public void Reports_unresolved_conflicts_in_plan()
    {
        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProject("B", ("Newtonsoft.Json", "12.0.3"))
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.HasUnresolvedConflicts.Should().BeTrue();
        plan.Conflicts.Should().HaveCount(1);
        plan.Conflicts[0].ResolvedVersion.Should().BeNull();
    }

    [Fact]
    public void Generates_warnings_for_legacy_projects()
    {
        var legacy = new ProjectInfo
        {
            AbsolutePath = "/repo/Legacy/Legacy.csproj",
            RelativePath = "Legacy/Legacy.csproj",
            Name = "Legacy",
            IsSdkStyle = false
        };

        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            legacy
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.LegacyProjectFormat);
    }

    [Fact]
    public void Generates_warning_for_existing_DirectoryPackagesProps()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddFile(Path.Combine(root, "Directory.Packages.props"), "<Project />");

        var solution = new SolutionInfo
        {
            RootPath = root,
            DetectionMethod = RootDetectionMethod.SolutionFile,
            HasExistingDirectoryPackagesProps = true,
            Projects = new[]
            {
                MakeProject("A", ("Newtonsoft.Json", "13.0.3"))
            }
        };

        var plan = _sut.CreatePlan(solution, []);

        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.ExistingDirectoryPackagesProps);
    }

    [Fact]
    public void IsNoOp_when_no_packages()
    {
        var solution = MakeSolution(
            new ProjectInfo
            {
                AbsolutePath = "/repo/A/A.csproj",
                RelativePath = "A/A.csproj",
                Name = "A",
                IsSdkStyle = true,
                PackageReferences = []
            }
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void Stats_are_calculated_correctly()
    {
        var legacy = new ProjectInfo
        {
            AbsolutePath = "/repo/Legacy/Legacy.csproj",
            RelativePath = "Legacy/Legacy.csproj",
            Name = "Legacy",
            IsSdkStyle = false
        };

        var solution = MakeSolution(
            MakeProject("A", ("Newtonsoft.Json", "13.0.3"), ("Serilog", "3.1.1")),
            MakeProject("B", ("Newtonsoft.Json", "13.0.3")),
            legacy
        );

        var plan = _sut.CreatePlan(solution, []);

        plan.Stats.TotalProjects.Should().Be(3);
        plan.Stats.SdkStyleProjects.Should().Be(2);
        plan.Stats.LegacyProjects.Should().Be(1);
        plan.Stats.TotalPackageReferences.Should().Be(3);
        plan.Stats.UniquePackages.Should().Be(2);
        plan.Stats.ConflictCount.Should().Be(0);
        plan.Stats.FilesCreated.Should().Be(1);
        plan.Stats.FilesModified.Should().Be(2);
    }

    [Fact]
    public void Skips_Update_references_in_central_props()
    {
        var project = new ProjectInfo
        {
            AbsolutePath = "/repo/A/A.csproj",
            RelativePath = "A/A.csproj",
            Name = "A",
            IsSdkStyle = true,
            PackageReferences = new[]
            {
                new PackageReference
                {
                    Package = new PackageIdentity("Newtonsoft.Json"),
                    Version = "13.0.3",
                    IsVersionAttribute = true,
                    IsUpdate = false,
                    ProjectRelativePath = "A/A.csproj"
                },
                new PackageReference
                {
                    Package = new PackageIdentity("Serilog"),
                    Version = "3.1.1",
                    IsVersionAttribute = true,
                    IsUpdate = true,
                    ProjectRelativePath = "A/A.csproj"
                }
            }
        };

        var solution = MakeSolution(project);
        var plan = _sut.CreatePlan(solution, []);

        // Only Newtonsoft.Json should be in Directory.Packages.props
        plan.Creates[0].Content.Should().Contain("Newtonsoft.Json");
        plan.Creates[0].Content.Should().NotContain("Serilog");
    }

    [Fact]
    public void SchemaVersion_is_set()
    {
        var solution = MakeSolution(MakeProject("A", ("Newtonsoft.Json", "13.0.3")));
        var plan = _sut.CreatePlan(solution, []);

        plan.SchemaVersion.Should().Be(MigrationPlan.CurrentSchemaVersion);
    }

    [Fact]
    public void Warns_about_conditional_references_with_version()
    {
        var project = new ProjectInfo
        {
            AbsolutePath = "/repo/A/A.csproj",
            RelativePath = "A/A.csproj",
            Name = "A",
            IsSdkStyle = true,
            PackageReferences = new[]
            {
                new PackageReference
                {
                    Package = new PackageIdentity("System.Memory"),
                    Version = "4.5.5",
                    Condition = "'$(TargetFramework)' == 'netstandard2.0'",
                    ProjectRelativePath = "A/A.csproj"
                }
            }
        };

        var solution = MakeSolution(project);
        var plan = _sut.CreatePlan(solution, []);

        plan.Warnings.Should().Contain(w =>
            w.Code == WarningCodes.ConditionalVersionReference &&
            w.PackageId == "System.Memory");
    }

    private static SolutionInfo MakeSolution(params ProjectInfo[] projects)
    {
        return new SolutionInfo
        {
            RootPath = Path.GetFullPath("/repo"),
            DetectionMethod = RootDetectionMethod.SolutionFile,
            Projects = projects
        };
    }

    private static ProjectInfo MakeProject(string name, params (string Id, string Version)[] packages)
    {
        return new ProjectInfo
        {
            AbsolutePath = $"/repo/{name}/{name}.csproj",
            RelativePath = $"{name}/{name}.csproj",
            Name = name,
            IsSdkStyle = true,
            PackageReferences = packages.Select(p => new PackageReference
            {
                Package = new PackageIdentity(p.Id),
                Version = p.Version,
                IsVersionAttribute = true,
                IsUpdate = false,
                ProjectRelativePath = $"{name}/{name}.csproj"
            }).ToList()
        };
    }
}
