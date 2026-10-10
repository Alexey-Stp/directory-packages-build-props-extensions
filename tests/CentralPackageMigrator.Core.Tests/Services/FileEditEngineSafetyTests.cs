using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class FileEditEngineSafetyTests
{
    private const string ProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Pkg" Version="1.0.0" />
          </ItemGroup>
        </Project>
        """;

    private readonly string _root = Path.GetFullPath("/repo");
    private readonly InMemoryFileSystem _fs = new();
    private readonly FileEditEngine _sut;

    public FileEditEngineSafetyTests()
    {
        _fs.AddDirectory(_root);
        _sut = new FileEditEngine(_fs, NullLogger<FileEditEngine>.Instance);
    }

    [Theory]
    [InlineData("../repo-evil/Directory.Packages.props")]
    [InlineData("sub/../../repo-evil/Directory.Packages.props")]
    [InlineData("/repo-evil/Directory.Packages.props")]
    [InlineData("C:/repo-evil/Directory.Packages.props")]
    [InlineData("C:Directory.Packages.props")]
    public void Rejects_paths_that_are_not_plain_relative_paths(string relativePath)
    {
        var plan = MakePlan(creates: [Create(relativePath)]);

        Expect(() => _sut.Apply(plan), ErrorCodes.InvalidPath);
    }

    [Fact]
    public void Rejects_absolute_path_in_sibling_directory_sharing_the_root_prefix()
    {
        string sibling = Path.GetFullPath("/repo-evil/Directory.Packages.props");
        var plan = MakePlan(creates: [Create(sibling)]);

        Expect(() => _sut.Apply(plan), ErrorCodes.InvalidPath);
        _fs.FileExists(sibling).Should().BeFalse();
    }

    [Theory]
    [InlineData("evil.targets")]
    [InlineData("Directory.Build.targets")]
    [InlineData("src/App/App.csproj")]
    public void Creates_only_Directory_Packages_props(string relativePath)
    {
        var plan = MakePlan(creates: [Create(relativePath)]);

        Expect(() => _sut.Apply(plan), ErrorCodes.InvalidPath);
    }

    [Fact]
    public void Edits_only_project_and_MSBuild_files()
    {
        _fs.AddFile(Path.Combine(_root, "Program.cs"), ProjectXml);
        var plan = MakePlan(edits: [Edit("Program.cs", RemoveVersion("Pkg"))]);

        Expect(() => _sut.Apply(plan), ErrorCodes.InvalidPath);
    }

    [Fact]
    public void Rejects_operations_that_do_not_belong_to_the_file_type()
    {
        _fs.AddFile(Path.Combine(_root, "A.csproj"), ProjectXml);
        var op = new EditOperation
        {
            Type = EditOperationType.AddPackageVersion,
            PackageId = "Pkg",
            Version = "1.0.0",
            Description = "add"
        };

        Expect(() => _sut.Apply(MakePlan(edits: [Edit("A.csproj", op)])), ErrorCodes.InvalidPlanFile);
    }

    [Fact]
    public void Rejects_edit_when_file_changed_since_planning()
    {
        string path = Path.Combine(_root, "A.csproj");
        _fs.AddFile(path, ProjectXml);
        var edit = Edit("A.csproj", RemoveVersion("Pkg")) with { OriginalSha256 = new string('0', 64) };

        Expect(() => _sut.Apply(MakePlan(edits: [edit])), ErrorCodes.StalePlan);
        _fs.GetFileContent(path).Should().Be(ProjectXml);
    }

    [Fact]
    public void Rejects_create_when_file_already_exists()
    {
        string path = Path.Combine(_root, "Directory.Packages.props");
        _fs.AddFile(path, "<Project />");

        Expect(() => _sut.Apply(MakePlan(creates: [Create("Directory.Packages.props")])), ErrorCodes.StalePlan);
        _fs.GetFileContent(path).Should().Be("<Project />");
    }

    [Fact]
    public void Rejects_create_with_malformed_content()
    {
        var create = Create("Directory.Packages.props") with { Content = "<Project>" };

        Expect(() => _sut.Apply(MakePlan(creates: [create])), ErrorCodes.InvalidPlanFile);
    }

    [Fact]
    public void Fails_verification_and_writes_nothing_when_a_version_would_remain()
    {
        string path = Path.Combine(_root, "A.csproj");
        string content = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg" Version="1.0.0" Condition="'$(A)' == '1'" />
                <PackageReference Include="Pkg" Version="2.0.0" Condition="'$(A)' == '2'" />
              </ItemGroup>
            </Project>
            """.ReplaceLineEndings("\n");
        _fs.AddFile(path, content);

        // Only the first reference is targeted, so the second keeps its version.
        var op = RemoveVersion("Pkg") with { Line = 3 };

        Expect(() => _sut.Apply(MakePlan(edits: [Edit("A.csproj", op)])), ErrorCodes.VerificationFailed);
        _fs.GetFileContent(path).Should().Be(content);
    }

    [Fact]
    public void Dry_run_runs_every_check()
    {
        _fs.AddFile(Path.Combine(_root, "A.csproj"), ProjectXml);
        var edit = Edit("A.csproj", RemoveVersion("Pkg")) with { OriginalSha256 = new string('0', 64) };

        Expect(() => _sut.Apply(MakePlan(edits: [edit]), dryRun: true), ErrorCodes.StalePlan);
    }

    [Fact]
    public void Rolls_back_written_files_when_a_later_write_fails()
    {
        string first = Path.Combine(_root, "A.csproj");
        string second = Path.Combine(_root, "B.csproj");
        string central = Path.Combine(_root, "Directory.Packages.props");
        _fs.AddFile(first, ProjectXml);
        _fs.AddFile(second, ProjectXml);
        _fs.FailingWrites.Add(second);

        var plan = MakePlan(
            creates: [Create("Directory.Packages.props")],
            edits: [Edit("A.csproj", RemoveVersion("Pkg")), Edit("B.csproj", RemoveVersion("Pkg"))]);

        Expect(() => _sut.Apply(plan), ErrorCodes.FileWriteFailed);

        _fs.FileExists(central).Should().BeFalse("the created file is removed again");
        _fs.GetFileContent(first).Should().Be(ProjectXml, "the edited file is restored");
    }

    private static void Expect(Action act, string code) =>
        act.Should().Throw<MigrationException>().Which.Code.Should().Be(code);

    private static FileCreate Create(string relativePath) => new()
    {
        RelativePath = relativePath,
        Content = "<Project />",
        Description = "test"
    };

    private static FileEdit Edit(string relativePath, params EditOperation[] operations) => new()
    {
        RelativePath = relativePath,
        Operations = operations
    };

    private static EditOperation RemoveVersion(string packageId) => new()
    {
        Type = EditOperationType.RemoveVersion,
        PackageId = packageId,
        Description = "remove"
    };

    private MigrationPlan MakePlan(FileCreate[]? creates = null, FileEdit[]? edits = null) => new()
    {
        SchemaVersion = MigrationPlan.CurrentSchemaVersion,
        Timestamp = FixedTimeProvider.DefaultInstant,
        SolutionRoot = _root,
        Creates = creates ?? [],
        Edits = edits ?? [],
        Stats = new MigrationStats()
    };
}
