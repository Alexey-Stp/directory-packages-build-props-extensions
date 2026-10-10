using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class MigrationServiceTests
{
    private const string ProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Pkg" Version="1.0.0" />
          </ItemGroup>
        </Project>
        """;

    private readonly string _root = Path.GetFullPath("/repo");
    private readonly string _project;
    private readonly InMemoryFileSystem _fs = new();
    private readonly Pipeline _pipeline;

    public MigrationServiceTests()
    {
        _project = Path.Combine(_root, "A", "A.csproj");
        _fs.AddFile(_project, ProjectXml);
        _pipeline = new Pipeline(_fs);
    }

    [Fact]
    public void Saved_plan_applies_when_nothing_changed()
    {
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root));

        var result = _pipeline.Service.ApplySavedPlan(saved, Pipeline.RequestFor(_root), dryRun: false);

        result.WrittenFiles.Should().HaveCount(2);
        _fs.GetFileContent(_project).Should().NotContain("Version=");
    }

    [Fact]
    public void Saved_plan_is_rejected_after_the_project_changed()
    {
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root));
        _fs.AddFile(_project, ProjectXml.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));

        Expect(() => _pipeline.Service.ApplySavedPlan(saved, Pipeline.RequestFor(_root), dryRun: false), ErrorCodes.StalePlan);
    }

    [Fact]
    public void Saved_plan_with_modified_content_is_rejected()
    {
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root));
        var tampered = saved with
        {
            Creates = [saved.Creates[0] with { Content = "<Project><Target Name=\"Evil\" /></Project>" }]
        };

        Expect(() => _pipeline.Service.ApplySavedPlan(tampered, Pipeline.RequestFor(_root), dryRun: false), ErrorCodes.StalePlan);
        _fs.FileExists(Path.Combine(_root, "Directory.Packages.props")).Should().BeFalse();
    }

    [Fact]
    public void Saved_plan_for_another_root_is_rejected()
    {
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root)) with
        {
            SolutionRoot = Path.GetFullPath("/elsewhere")
        };

        Expect(() => _pipeline.Service.ApplySavedPlan(saved, Pipeline.RequestFor(_root), dryRun: false), ErrorCodes.InvalidPlanFile);
    }

    [Fact]
    public void Saved_plan_with_unknown_major_schema_is_rejected()
    {
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root)) with { SchemaVersion = "2.0" };

        Expect(() => _pipeline.Service.ApplySavedPlan(saved, Pipeline.RequestFor(_root), dryRun: false), ErrorCodes.InvalidPlanFile);
    }

    [Fact]
    public void Saved_plan_keeps_its_resolutions()
    {
        _fs.AddFile(Path.Combine(_root, "B", "B.csproj"), ProjectXml.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var saved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(
            _root, new ConflictResolution { PackageId = "Pkg", Version = "2.0.0" }));

        var result = _pipeline.Service.ApplySavedPlan(saved, Pipeline.RequestFor(_root), dryRun: true);

        result.Plan.HasUnresolvedConflicts.Should().BeFalse();
    }

    [Fact]
    public void Missing_directory_is_reported()
    {
        Expect(() => _pipeline.Service.CreatePlan(Pipeline.RequestFor(Path.GetFullPath("/missing"))), ErrorCodes.SolutionNotFound);
    }

    [Fact]
    public void Directory_without_projects_is_reported()
    {
        string empty = Path.GetFullPath("/empty");
        _fs.AddDirectory(empty);

        Expect(() => _pipeline.Service.CreatePlan(Pipeline.RequestFor(empty)), ErrorCodes.NoProjectsFound);
    }

    private static void Expect(Action act, string code) =>
        act.Should().Throw<MigrationException>().Which.Code.Should().Be(code);
}
