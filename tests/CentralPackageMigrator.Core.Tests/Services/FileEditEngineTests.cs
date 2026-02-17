using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Core.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class FileEditEngineTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FileEditEngine _sut;

    public FileEditEngineTests()
    {
        _sut = new FileEditEngine(_fs, NullLogger<FileEditEngine>.Instance);
    }

    [Fact]
    public void Creates_new_files_from_plan()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);

        var plan = MakePlan(root,
            creates: new[]
            {
                new FileCreate
                {
                    RelativePath = "Directory.Packages.props",
                    Content = "<Project />",
                    Description = "Central Package Management"
                }
            });

        var result = _sut.Apply(plan);

        result.Should().Contain("Directory.Packages.props");
        _fs.FileExists(Path.Combine(root, "Directory.Packages.props")).Should().BeTrue();
        _fs.GetFileContent(Path.Combine(root, "Directory.Packages.props")).Should().Be("<Project />");
    }

    [Fact]
    public void Removes_Version_attribute_from_csproj()
    {
        string root = Path.GetFullPath("/repo");
        string csproj = Path.Combine(root, "src", "A", "A.csproj");
        _fs.AddFile(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="Serilog" Version="3.1.1" />
              </ItemGroup>
            </Project>
            """);

        var plan = MakePlan(root,
            edits: new[]
            {
                new FileEdit
                {
                    RelativePath = Path.Combine("src", "A", "A.csproj"),
                    Operations = new[]
                    {
                        new EditOperation
                        {
                            Type = EditOperationType.RemoveVersion,
                            PackageId = "Newtonsoft.Json",
                            OriginalText = "13.0.3",
                            Description = "Remove version"
                        },
                        new EditOperation
                        {
                            Type = EditOperationType.RemoveVersion,
                            PackageId = "Serilog",
                            OriginalText = "3.1.1",
                            Description = "Remove version"
                        }
                    }
                }
            });

        _sut.Apply(plan);

        string modified = _fs.GetFileContent(csproj);
        modified.Should().NotContain("Version=\"13.0.3\"");
        modified.Should().NotContain("Version=\"3.1.1\"");
        modified.Should().Contain("Newtonsoft.Json");
        modified.Should().Contain("Serilog");
    }

    [Fact]
    public void DryRun_does_not_write_files()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);

        var plan = MakePlan(root,
            creates: new[]
            {
                new FileCreate
                {
                    RelativePath = "Directory.Packages.props",
                    Content = "<Project />",
                    Description = "CPM"
                }
            });

        var result = _sut.Apply(plan, dryRun: true);

        result.Should().HaveCount(1);
        _fs.FileExists(Path.Combine(root, "Directory.Packages.props")).Should().BeFalse();
    }

    [Fact]
    public void Throws_on_unresolved_conflicts()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);

        var plan = new MigrationPlan
        {
            SchemaVersion = "1.0",
            Timestamp = DateTimeOffset.UtcNow,
            SolutionRoot = root,
            Stats = new MigrationStats(),
            Conflicts = new[]
            {
                new ConflictGroup
                {
                    Package = new PackageIdentity("Newtonsoft.Json"),
                    Entries = new[]
                    {
                        new ConflictEntry { Version = "13.0.3", Projects = ["A.csproj"] },
                        new ConflictEntry { Version = "12.0.3", Projects = ["B.csproj"] }
                    },
                    ResolvedVersion = null
                }
            }
        };

        var act = () => _sut.Apply(plan);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unresolved conflicts*");
    }

    [Fact]
    public void Validates_path_traversal()
    {
        string root = Path.GetFullPath("/repo");
        _fs.AddDirectory(root);

        var plan = MakePlan(root,
            creates: new[]
            {
                new FileCreate
                {
                    RelativePath = "../../../etc/malicious",
                    Content = "bad",
                    Description = "test"
                }
            });

        var act = () => _sut.Apply(plan);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Path*");
    }

    [Fact]
    public void Handles_multiple_edits_in_same_file()
    {
        string root = Path.GetFullPath("/repo");
        string csproj = Path.Combine(root, "A.csproj");
        _fs.AddFile(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg1" Version="1.0.0" />
                <PackageReference Include="Pkg2" Version="2.0.0" />
                <PackageReference Include="Pkg3" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);

        var plan = MakePlan(root,
            edits: new[]
            {
                new FileEdit
                {
                    RelativePath = "A.csproj",
                    Operations = new[]
                    {
                        new EditOperation { Type = EditOperationType.RemoveVersion, PackageId = "Pkg1", Description = "r" },
                        new EditOperation { Type = EditOperationType.RemoveVersion, PackageId = "Pkg2", Description = "r" },
                        new EditOperation { Type = EditOperationType.RemoveVersion, PackageId = "Pkg3", Description = "r" }
                    }
                }
            });

        _sut.Apply(plan);

        string modified = _fs.GetFileContent(csproj);
        modified.Should().NotContain("Version=\"1.0.0\"");
        modified.Should().NotContain("Version=\"2.0.0\"");
        modified.Should().NotContain("Version=\"3.0.0\"");
        modified.Should().Contain("Pkg1");
        modified.Should().Contain("Pkg2");
        modified.Should().Contain("Pkg3");
    }

    [Fact]
    public void Returns_list_of_written_files()
    {
        string root = Path.GetFullPath("/repo");
        string csproj = Path.Combine(root, "A.csproj");
        _fs.AddFile(csproj, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg1" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var plan = MakePlan(root,
            creates: new[]
            {
                new FileCreate { RelativePath = "Directory.Packages.props", Content = "<Project />", Description = "CPM" }
            },
            edits: new[]
            {
                new FileEdit
                {
                    RelativePath = "A.csproj",
                    Operations = new[]
                    {
                        new EditOperation { Type = EditOperationType.RemoveVersion, PackageId = "Pkg1", Description = "r" }
                    }
                }
            });

        var result = _sut.Apply(plan);

        result.Should().HaveCount(2);
        result.Should().Contain("Directory.Packages.props");
        result.Should().Contain("A.csproj");
    }

    private static MigrationPlan MakePlan(
        string root,
        FileCreate[]? creates = null,
        FileEdit[]? edits = null)
    {
        return new MigrationPlan
        {
            SchemaVersion = "1.0",
            Timestamp = DateTimeOffset.UtcNow,
            SolutionRoot = root,
            Creates = creates ?? [],
            Edits = edits ?? [],
            Stats = new MigrationStats()
        };
    }
}
