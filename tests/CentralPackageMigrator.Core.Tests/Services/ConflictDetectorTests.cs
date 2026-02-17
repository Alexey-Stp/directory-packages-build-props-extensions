using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class ConflictDetectorTests
{
    private readonly ConflictDetector _sut = new(NullLogger<ConflictDetector>.Instance);

    [Fact]
    public void No_conflicts_when_all_versions_match()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProject("B", ("Newtonsoft.Json", "13.0.3"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Detects_conflict_when_same_package_has_different_versions()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProject("B", ("Newtonsoft.Json", "12.0.3"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().HaveCount(1);
        result[0].Package.Id.Should().Be("Newtonsoft.Json");
        result[0].Entries.Should().HaveCount(2);
    }

    [Fact]
    public void Detects_multiple_conflicts()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3"), ("Serilog", "3.1.1")),
            MakeProject("B", ("Newtonsoft.Json", "12.0.3"), ("Serilog", "2.0.0"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().HaveCount(2);
        result.Select(c => c.Package.Id).Should().Contain("Newtonsoft.Json");
        result.Select(c => c.Package.Id).Should().Contain("Serilog");
    }

    [Fact]
    public void Ignores_Update_references()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProjectWithUpdate("B", ("Newtonsoft.Json", "12.0.3"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Ignores_packages_without_version()
    {
        var projectA = new ProjectInfo
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
                    Version = null,
                    ProjectRelativePath = "A/A.csproj"
                }
            }
        };

        var result = _sut.DetectConflicts(new[] { projectA });

        result.Should().BeEmpty();
    }

    [Fact]
    public void Groups_projects_correctly_by_version()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            MakeProject("B", ("Newtonsoft.Json", "12.0.3")),
            MakeProject("C", ("Newtonsoft.Json", "13.0.3"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().HaveCount(1);
        var conflict = result[0];

        var v13 = conflict.Entries.First(e => e.Version == "13.0.3");
        v13.Projects.Should().HaveCount(2);
        v13.Projects.Should().Contain("A/A.csproj");
        v13.Projects.Should().Contain("C/C.csproj");

        var v12 = conflict.Entries.First(e => e.Version == "12.0.3");
        v12.Projects.Should().HaveCount(1);
    }

    [Fact]
    public void Orders_conflicts_by_package_id()
    {
        var projects = new[]
        {
            MakeProject("A", ("Zebra", "1.0"), ("Alpha", "1.0")),
            MakeProject("B", ("Zebra", "2.0"), ("Alpha", "2.0"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().HaveCount(2);
        result[0].Package.Id.Should().Be("Alpha");
        result[1].Package.Id.Should().Be("Zebra");
    }

    [Fact]
    public void Empty_input_produces_empty_result()
    {
        var result = _sut.DetectConflicts([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Single_project_never_has_conflicts()
    {
        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3"), ("Serilog", "3.1.1"))
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Skips_legacy_projects()
    {
        var legacy = new ProjectInfo
        {
            AbsolutePath = "/repo/B/B.csproj",
            RelativePath = "B/B.csproj",
            Name = "B",
            IsSdkStyle = false,
            PackageReferences = new[]
            {
                new PackageReference
                {
                    Package = new PackageIdentity("Newtonsoft.Json"),
                    Version = "12.0.3",
                    ProjectRelativePath = "B/B.csproj"
                }
            }
        };

        var projects = new[]
        {
            MakeProject("A", ("Newtonsoft.Json", "13.0.3")),
            legacy
        };

        var result = _sut.DetectConflicts(projects);

        result.Should().BeEmpty();
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

    private static ProjectInfo MakeProjectWithUpdate(string name, params (string Id, string Version)[] packages)
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
                IsUpdate = true,
                ProjectRelativePath = $"{name}/{name}.csproj"
            }).ToList()
        };
    }
}
