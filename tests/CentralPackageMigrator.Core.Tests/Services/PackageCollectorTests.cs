using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Core.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

public sealed class PackageCollectorTests
{
    private const string SolutionRoot = "/repo";
    private const string ProjectPath = "/repo/src/MyApp/MyApp.csproj";

    private readonly InMemoryFileSystem _fs = new();
    private readonly PackageCollector _sut;

    public PackageCollectorTests()
    {
        _fs.AddDirectory(SolutionRoot);
        _sut = new PackageCollector(_fs, NullLogger<PackageCollector>.Instance);
    }

    [Fact]
    public void Extracts_PackageReference_with_Version_attribute()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].Package.Id.Should().Be("Newtonsoft.Json");
        result[0].Version.Should().Be("13.0.1");
        result[0].IsVersionAttribute.Should().BeTrue();
        result[0].IsUpdate.Should().BeFalse();
    }

    [Fact]
    public void Extracts_PackageReference_with_nested_Version_element()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.1</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].Package.Id.Should().Be("Newtonsoft.Json");
        result[0].Version.Should().Be("13.0.1");
        result[0].IsVersionAttribute.Should().BeFalse();
    }

    [Fact]
    public void Handles_PackageReference_without_version()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].Package.Id.Should().Be("Newtonsoft.Json");
        result[0].Version.Should().BeNull();
    }

    [Fact]
    public void Extracts_PrivateAssets_IncludeAssets_ExcludeAssets_metadata()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="coverlet.collector" Version="6.0.2"
                    PrivateAssets="all" IncludeAssets="runtime;build" ExcludeAssets="contentfiles" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].PrivateAssets.Should().Be("all");
        result[0].IncludeAssets.Should().Be("runtime;build");
        result[0].ExcludeAssets.Should().Be("contentfiles");
    }

    [Fact]
    public void Extracts_metadata_from_nested_elements()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="coverlet.collector" Version="6.0.2">
                  <PrivateAssets>all</PrivateAssets>
                  <IncludeAssets>runtime; build</IncludeAssets>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].PrivateAssets.Should().Be("all");
        result[0].IncludeAssets.Should().Be("runtime; build");
    }

    [Fact]
    public void Detects_Update_references()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Update="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].IsUpdate.Should().BeTrue();
        result[0].Package.Id.Should().Be("Newtonsoft.Json");
        result[0].Version.Should().Be("13.0.1");
    }

    [Fact]
    public void Extracts_Condition_attribute()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="System.Memory" Version="4.5.5"
                    Condition="'$(TargetFramework)' == 'netstandard2.0'" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].Condition.Should().Be("'$(TargetFramework)' == 'netstandard2.0'");
    }

    [Fact]
    public void Handles_malformed_XML_gracefully()
    {
        // Arrange
        _fs.AddFile(ProjectPath, "<<< this is not valid XML >>>");

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Handles_empty_project_file()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Extracts_multiple_PackageReferences_from_one_file()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Include="Serilog" Version="3.1.1" />
                <PackageReference Include="xunit" Version="2.9.2" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Package.Id).Should().BeEquivalentTo(
            "Newtonsoft.Json", "Serilog", "xunit");
    }

    [Fact]
    public void Records_correct_line_numbers()
    {
        // Arrange — line numbers are 1-based in XDocument parsing
        string xml = "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
                   + "  <ItemGroup>\n"
                   + "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.1\" />\n"
                   + "    <PackageReference Include=\"Serilog\" Version=\"3.1.1\" />\n"
                   + "  </ItemGroup>\n"
                   + "</Project>";

        _fs.AddFile(ProjectPath, xml);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(2);
        result[0].Line.Should().Be(3);
        result[1].Line.Should().Be(4);
    }

    [Theory]
    [InlineData("Include", false)]
    [InlineData("Update", true)]
    public void IsUpdate_reflects_attribute_type(string attributeName, bool expectedIsUpdate)
    {
        // Arrange
        _fs.AddFile(ProjectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference {attributeName}="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].IsUpdate.Should().Be(expectedIsUpdate);
    }

    [Fact]
    public void Sets_ProjectRelativePath_correctly()
    {
        // Arrange
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().HaveCount(1);
        result[0].ProjectRelativePath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Skips_PackageReference_without_Include_or_Update()
    {
        // Arrange — a PackageReference with neither Include nor Update should be skipped
        _fs.AddFile(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        // Act
        var result = _sut.CollectFromProject(ProjectPath, SolutionRoot);

        // Assert
        result.Should().BeEmpty();
    }
}
