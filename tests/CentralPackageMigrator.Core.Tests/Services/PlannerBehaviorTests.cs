using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Core.Tests.Services;

/// <summary>
/// Planner behaviour over real (in-memory) project files, scanned and planned end to end.
/// </summary>
public sealed class PlannerBehaviorTests
{
    private readonly string _root = Path.GetFullPath("/repo");
    private readonly InMemoryFileSystem _fs = new();
    private readonly Pipeline _pipeline;

    public PlannerBehaviorTests()
    {
        _pipeline = new Pipeline(_fs);
    }

    [Fact]
    public void Equivalent_versions_are_not_a_conflict()
    {
        AddProject("A", ("Pkg", "1.0"));
        AddProject("B", ("Pkg", "1.0.0"));

        var plan = Plan();

        plan.Conflicts.Should().BeEmpty();
        plan.Stats.UniquePackages.Should().Be(1);
    }

    [Fact]
    public void Conflict_suggests_the_highest_version()
    {
        AddProject("A", ("Pkg", "13.0.3"));
        AddProject("B", ("Pkg", "12.0.3"));
        AddProject("C", ("Pre", "2.0.0-beta.1"));
        AddProject("D", ("Pre", "1.9.0"));

        var plan = Plan();

        plan.Conflicts.Single(c => c.Package.Id == "Pkg").SuggestedVersion.Should().Be("13.0.3");
        plan.Conflicts.Single(c => c.Package.Id == "Pre").SuggestedVersion.Should().Be("2.0.0-beta.1");
    }

    [Fact]
    public void Resolution_for_a_package_without_conflict_is_reported()
    {
        AddProject("A", ("Pkg", "1.0.0"));

        var plan = Plan(new ConflictResolution { PackageId = "Other", Version = "1.0.0" });

        plan.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.UnusedResolution)
            .Which.Severity.Should().Be(WarningSeverity.Warning);
    }

    [Fact]
    public void Property_version_defined_in_project_is_resolved()
    {
        AddFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <SerilogVersion>3.1.1</SerilogVersion>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Serilog" Version="$(SerilogVersion)" />
              </ItemGroup>
            </Project>
            """);

        var plan = Plan();

        plan.Creates.Single().Content.Should().Contain("<PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />");
        plan.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.PropertyBasedVersion)
            .Which.Severity.Should().Be(WarningSeverity.Info);
    }

    [Fact]
    public void Property_version_defined_in_root_Directory_Build_props_is_resolved()
    {
        AddFile("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <SerilogVersion>3.1.1</SerilogVersion>
              </PropertyGroup>
            </Project>
            """);
        AddFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="$(SerilogVersion)" />
              </ItemGroup>
            </Project>
            """);

        var plan = Plan();

        plan.Creates.Single().Content.Should().Contain("Version=\"3.1.1\"");
    }

    [Fact]
    public void Unresolvable_property_version_is_kept_and_flagged()
    {
        AddFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="$(Missing)" />
              </ItemGroup>
            </Project>
            """);

        var plan = Plan();

        plan.Creates.Single().Content.Should().Contain("Version=\"$(Missing)\"");
        plan.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.PropertyBasedVersion)
            .Which.Severity.Should().Be(WarningSeverity.Warning);
        plan.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        AddProject("B", ("Zeta", "1.0.0"), ("Alpha", "2.0.0"));
        AddProject("A", ("Alpha", "2.0.0"));

        var first = Plan();
        var second = Plan();

        first.Timestamp.Should().Be(FixedTimeProvider.DefaultInstant);
        second.Timestamp.Should().Be(first.Timestamp);
        second.Creates.Should().BeEquivalentTo(first.Creates, o => o.WithStrictOrdering());
        second.Edits.Should().BeEquivalentTo(first.Edits, o => o.WithStrictOrdering());
    }

    [Fact]
    public void Edits_carry_the_hash_of_the_planned_content()
    {
        AddProject("A", ("Pkg", "1.0.0"));

        var plan = Plan();

        plan.Edits.Single().OriginalSha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Informational_warnings_do_not_count_as_warnings()
    {
        AddProject("A", ("Pkg", "1.0.0"));
        AddProject("Empty");

        var plan = Plan();

        plan.Warnings.Should().ContainSingle(w => w.Code == WarningCodes.NoPackageReferences)
            .Which.Severity.Should().Be(WarningSeverity.Info);
        plan.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void Malformed_project_is_reported_as_malformed_not_legacy()
    {
        AddProject("A", ("Pkg", "1.0.0"));
        AddFile("Broken/Broken.csproj", "<Project");

        var plan = Plan();

        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.MalformedXml);
        plan.Warnings.Should().NotContain(w => w.Code == WarningCodes.LegacyProjectFormat);
        plan.Stats.LegacyProjects.Should().Be(0);
    }

    [Fact]
    public void Does_not_enable_central_management_when_Directory_Build_props_already_does()
    {
        AddFile("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        AddFile("Directory.Packages.props", """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Existing" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        AddProject("A", ("New", "1.0.0"));

        var plan = Plan();

        var central = plan.Edits.Single(e => e.RelativePath == "Directory.Packages.props");
        central.Operations.Should().ContainSingle()
            .Which.Type.Should().Be(EditOperationType.AddPackageVersion);
    }

    [Fact]
    public void Existing_central_file_is_merged_never_recreated()
    {
        AddFile("Directory.Packages.props", """
            <Project>
              <ItemGroup>
                <PackageVersion Include="Existing" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        AddProject("A", ("New", "1.0.0"));

        var plan = Plan();

        plan.Creates.Should().BeEmpty();
        plan.Warnings.Should().Contain(w => w.Code == WarningCodes.ExistingDirectoryPackagesProps && w.Severity == WarningSeverity.Info);
    }

    [Fact]
    public void Mixed_conditional_and_unconditional_versions_conflict()
    {
        AddFile("A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pkg" Version="1.0.0" />
                <PackageReference Include="Pkg" Version="2.0.0" Condition="'$(X)' == 'y'" />
              </ItemGroup>
            </Project>
            """);

        var plan = Plan();

        plan.HasUnresolvedConflicts.Should().BeTrue();
    }

    private MigrationPlan Plan(params ConflictResolution[] resolutions) =>
        _pipeline.Service.CreatePlan(Pipeline.RequestFor(_root, resolutions));

    private void AddProject(string name, params (string Id, string Version)[] packages)
    {
        string references = string.Concat(packages.Select(p =>
            $"    <PackageReference Include=\"{p.Id}\" Version=\"{p.Version}\" />\n"));

        AddFile($"{name}/{name}.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" + references + "  </ItemGroup>\n</Project>\n");
    }

    private void AddFile(string relativePath, string content) =>
        _fs.AddFile(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)), content.ReplaceLineEndings("\n"));
}
