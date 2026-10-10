using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class ProjectScannerTests
{
    private const string SdkProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Pkg" Version="1.0.0" />
          </ItemGroup>
        </Project>
        """;

    private readonly string _root = Path.GetFullPath("/repo/app");
    private readonly InMemoryFileSystem _fs = new();
    private readonly Pipeline _pipeline;

    public ProjectScannerTests()
    {
        _pipeline = new Pipeline(_fs);
    }

    [Fact]
    public void Finds_csproj_fsproj_vbproj_and_import_files()
    {
        Add("src/Cs/Cs.csproj", SdkProject);
        Add("src/Fs/Fs.fsproj", SdkProject);
        Add("src/Vb/Vb.vbproj", SdkProject);
        Add("Directory.Build.props", "<Project />");
        Add("src/Directory.Build.targets", "<Project />");
        Add("src/Cs/Program.cs", "class P {}");

        var projects = _pipeline.Scanner.ScanProjects(_root);

        projects.Where(p => p.Kind == ProjectKind.Project).Select(p => p.Name)
            .Should().BeEquivalentTo("Cs", "Fs", "Vb");
        projects.Where(p => p.Kind == ProjectKind.ImportFile).Should().HaveCount(2)
            .And.OnlyContain(p => p.IsSdkStyle);
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("packages")]
    [InlineData(".git")]
    [InlineData(".vs")]
    public void Skips_build_output_dependency_and_hidden_directories(string directory)
    {
        Add("src/App/App.csproj", SdkProject);
        Add($"src/App/{directory}/Copy/Copy.csproj", SdkProject);

        var projects = _pipeline.Scanner.ScanProjects(_root);

        projects.Should().ContainSingle().Which.Name.Should().Be("App");
    }

    [Fact]
    public void Records_parse_errors_instead_of_failing()
    {
        Add("src/Broken/Broken.csproj", "<Project");

        var project = _pipeline.Scanner.ScanProjects(_root).Single();

        project.ParseError.Should().NotBeNullOrWhiteSpace();
        project.IsMigratable.Should().BeFalse();
    }

    [Fact]
    public void Flags_solution_projects_outside_the_root()
    {
        Add("src/App/App.csproj", SdkProject);
        Add("App.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Shared", "..\shared\Shared.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        _fs.AddFile(Path.GetFullPath("/repo/shared/Shared.csproj"), SdkProject);

        var projects = _pipeline.Scanner.ScanProjects(_root, Path.Combine(_root, "App.sln"));

        var outside = projects.Single(p => p.IsOutsideRoot);
        outside.Name.Should().Be("Shared");
        outside.IsMigratable.Should().BeFalse();
    }

    [Fact]
    public void Reads_slnx_solution_files()
    {
        Add("src/App/App.csproj", SdkProject);
        Add("App.slnx", """
            <Solution>
              <Project Path="src/App/App.csproj" />
              <Project Path="../shared/Shared.csproj" />
            </Solution>
            """);
        _fs.AddFile(Path.GetFullPath("/repo/shared/Shared.csproj"), SdkProject);

        var projects = _pipeline.Scanner.ScanProjects(_root, Path.Combine(_root, "App.slnx"));

        projects.Should().ContainSingle(p => p.IsOutsideRoot);
    }

    [Fact]
    public void Honors_cancellation()
    {
        Add("src/App/App.csproj", SdkProject);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _pipeline.Scanner.ScanProjects(_root, cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    private void Add(string relativePath, string content) =>
        _fs.AddFile(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), content);
}
