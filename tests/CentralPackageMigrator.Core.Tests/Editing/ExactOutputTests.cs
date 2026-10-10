using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Editing;

/// <summary>
/// Runs the real planner and edit engine and compares whole files, byte for byte.
/// Substring assertions let formatting regressions through; these do not.
/// </summary>
public sealed class ExactOutputTests
{
    private readonly string _root = Path.GetFullPath("/repo");
    private readonly InMemoryFileSystem _fs = new();
    private readonly Pipeline _pipeline;

    public ExactOutputTests()
    {
        _pipeline = new Pipeline(_fs);
    }

    [Fact]
    public void Removes_version_attribute_in_any_position_and_keeps_indentation()
    {
        string project = AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                    <PackageReference Include="A" Version="1.0.0" />
                    <PackageReference Version="2.0.0" Include="B" />
                    <PackageReference Include="C" PrivateAssets="all" Version="3.0.0" />
                    <PackageReference Include="D"
                                      Version="4.0.0"
                                      PrivateAssets="all" />
                    <PackageReference
                        Version="5.0.0"
                        Include="E" />
                </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(project).Should().Be(Lf("""
            <Project Sdk="Microsoft.NET.Sdk">
                <ItemGroup>
                    <PackageReference Include="A" />
                    <PackageReference Include="B" />
                    <PackageReference Include="C" PrivateAssets="all" />
                    <PackageReference Include="D"
                                      PrivateAssets="all" />
                    <PackageReference
                        Include="E" />
                </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Removes_nested_Version_element_and_collapses_emptied_reference()
    {
        string project = AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="E">
                  <Version>5.0.0</Version>
                </PackageReference>
                <PackageReference Include="F">
                  <Version>6.0.0</Version>
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(project).Should().Be(Lf("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="E" />
                <PackageReference Include="F">
                  <PrivateAssets>all</PrivateAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Preserves_tabs_CRLF_comments_and_xml_declaration()
    {
        string input = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                     + "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
                     + "\t<!-- packages -->\r\n"
                     + "\t<ItemGroup>\r\n"
                     + "\t\t<PackageReference Include=\"G\" Version=\"1.0.0\" /> <!-- trailing -->\r\n"
                     + "\t\t<PackageReference Include=\"H\">\r\n"
                     + "\t\t\t<Version>2.0.0</Version>\r\n"
                     + "\t\t</PackageReference>\r\n"
                     + "\t</ItemGroup>\r\n"
                     + "</Project>\r\n";

        string project = Path.Combine(_root, "A", "A.csproj");
        _fs.AddFile(project, input);

        Apply();

        _fs.GetFileContent(project).Should().Be(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
            + "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n"
            + "\t<!-- packages -->\r\n"
            + "\t<ItemGroup>\r\n"
            + "\t\t<PackageReference Include=\"G\" /> <!-- trailing -->\r\n"
            + "\t\t<PackageReference Include=\"H\" />\r\n"
            + "\t</ItemGroup>\r\n"
            + "</Project>\r\n");

        // The generated file follows the projects' line endings.
        _fs.GetFileContent(Path.Combine(_root, "Directory.Packages.props")).Should().Contain("\r\n")
            .And.NotMatchRegex("[^\r]\n");
    }

    [Fact]
    public void Leaves_Update_references_untouched()
    {
        string project = AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="G" Version="1.0.0" />
                <PackageReference Update="G" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(project).Should().Be(Lf("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="G" />
                <PackageReference Update="G" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Generates_sorted_Directory_Packages_props()
    {
        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="3.1.1" />
                <PackageReference Include="FluentValidation" Version="11.9.0" />
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(Path.Combine(_root, "Directory.Packages.props")).Should().Be(Lf("""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageVersion Include="FluentValidation" Version="11.9.0" />
                <PackageVersion Include="Serilog" Version="3.1.1" />
              </ItemGroup>
            </Project>

            """));
    }

    [Fact]
    public void Generates_conditional_PackageVersions_and_escapes_attribute_values()
    {
        string project = AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg" Version="1.0.0" Condition="'$(TargetFramework)' == 'net48'" />
                <PackageReference Include="Pkg" Version="2.0.0" Condition="'$(LangVersion)' &lt; '9'" />
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(project).Should().Be(Lf("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg" Condition="'$(TargetFramework)' == 'net48'" />
                <PackageReference Include="Pkg" Condition="'$(LangVersion)' &lt; '9'" />
              </ItemGroup>
            </Project>
            """));

        _fs.GetFileContent(Path.Combine(_root, "Directory.Packages.props")).Should().Contain(Lf("""
                <PackageVersion Include="Pkg" Version="2.0.0" Condition="'$(LangVersion)' &lt; '9'" />
                <PackageVersion Include="Pkg" Version="1.0.0" Condition="'$(TargetFramework)' == 'net48'" />
            """));
    }

    [Fact]
    public void Merges_new_versions_into_existing_Directory_Packages_props()
    {
        string central = AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <!-- shared -->
                <PackageVersion Include="Existing" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Existing" Version="1.0" />
                <PackageReference Include="New" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(central).Should().Be(Lf("""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <!-- shared -->
                <PackageVersion Include="Existing" Version="1.0.0" />
                <PackageVersion Include="New" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Resolved_conflict_with_existing_central_version_updates_it_in_place()
    {
        string central = AddFile("Directory.Packages.props", """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Existing" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Existing" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        var unresolved = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root));
        unresolved.Conflicts.Should().ContainSingle()
            .Which.Entries.SelectMany(e => e.Projects).Should().Contain("Directory.Packages.props");

        Apply(new ConflictResolution { PackageId = "Existing", Version = "2.0.0" });

        // Central management was not enabled anywhere, so it is switched on as well.
        _fs.GetFileContent(central).Should().Be(Lf("""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
              <ItemGroup>
                <PackageVersion Include="Existing" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Adds_an_ItemGroup_when_Directory_Packages_props_has_none()
    {
        string central = AddFile("Directory.Packages.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);

        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="New" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """);

        Apply();

        _fs.GetFileContent(central).Should().Be(Lf("""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageVersion Include="New" Version="2.0.0" />
              </ItemGroup>
            </Project>
            """));
    }

    [Fact]
    public void Migrates_PackageReferences_in_Directory_Build_props()
    {
        string buildProps = AddFile("Directory.Build.props", """
            <Project>
              <ItemGroup>
                <PackageReference Include="StyleCop.Analyzers" Version="1.1.118" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);

        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """);

        Apply();

        _fs.GetFileContent(buildProps).Should().Be(Lf("""
            <Project>
              <ItemGroup>
                <PackageReference Include="StyleCop.Analyzers" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """));
        _fs.GetFileContent(Path.Combine(_root, "Directory.Packages.props"))
            .Should().Contain("<PackageVersion Include=\"StyleCop.Analyzers\" Version=\"1.1.118\" />");
    }

    [Fact]
    public void Second_run_changes_nothing()
    {
        AddProject("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="A" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        Apply();
        var second = _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root));

        second.IsNoOp.Should().BeTrue();
    }

    private void Apply(params ConflictResolution[] resolutions) =>
        _pipeline.Service.Apply(Pipeline.RequestFor(_root, resolutions), dryRun: false);

    private string AddProject(string relativePath, string content) => AddFile(relativePath, content);

    private string AddFile(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        _fs.AddFile(path, Lf(content));
        return path;
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n");
}
