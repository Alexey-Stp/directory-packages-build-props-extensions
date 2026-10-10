using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CentralPackageMigrator.Integration.Tests;

public sealed class FullMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _services;

    public FullMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cpm-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        var sc = new ServiceCollection();
        sc.AddCentralPackageMigrator();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        _services = sc.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void FullMigration_CreatesDirectoryPackagesProps_AndRemovesVersions()
    {
        // Arrange: create a mini solution on disk
        CreateProjectFile("src/AppA/AppA.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="Serilog" Version="3.1.1" />
              </ItemGroup>
            </Project>
            """);

        CreateProjectFile("src/AppB/AppB.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="FluentValidation" Version="11.9.0" />
              </ItemGroup>
            </Project>
            """);

        // Act: run the full pipeline
        var detector = _services.GetRequiredService<ISolutionDetector>();
        var scanner = _services.GetRequiredService<IProjectScanner>();
        var planner = _services.GetRequiredService<IMigrationPlanner>();
        var engine = _services.GetRequiredService<IFileEditEngine>();

        var solution = detector.Detect(_tempDir);
        solution = solution with { Projects = scanner.ScanProjects(_tempDir) };

        var plan = planner.CreatePlan(solution, []);
        plan.HasUnresolvedConflicts.Should().BeFalse();

        var written = engine.Apply(plan);

        // Assert: Directory.Packages.props was created
        string dppPath = Path.Combine(_tempDir, "Directory.Packages.props");
        File.Exists(dppPath).Should().BeTrue();
        string dppContent = File.ReadAllText(dppPath);
        dppContent.Should().Contain("ManagePackageVersionsCentrally");
        dppContent.Should().Contain("FluentValidation");
        dppContent.Should().Contain("Newtonsoft.Json");
        dppContent.Should().Contain("Serilog");

        // Assert: Version attributes removed from .csproj files
        string appA = File.ReadAllText(Path.Combine(_tempDir, "src", "AppA", "AppA.csproj"));
        appA.Should().Contain("Newtonsoft.Json");
        appA.Should().NotContain("Version=\"13.0.3\"");
        appA.Should().NotContain("Version=\"3.1.1\"");

        string appB = File.ReadAllText(Path.Combine(_tempDir, "src", "AppB", "AppB.csproj"));
        appB.Should().Contain("Newtonsoft.Json");
        appB.Should().NotContain("Version=\"13.0.3\"");
        appB.Should().NotContain("Version=\"11.9.0\"");

        written.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void FullMigration_IsIdempotent()
    {
        // Arrange
        CreateProjectFile("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        var detector = _services.GetRequiredService<ISolutionDetector>();
        var scanner = _services.GetRequiredService<IProjectScanner>();
        var planner = _services.GetRequiredService<IMigrationPlanner>();
        var engine = _services.GetRequiredService<IFileEditEngine>();

        // First run
        var sol1 = detector.Detect(_tempDir) with { Projects = scanner.ScanProjects(_tempDir) };
        var plan1 = planner.CreatePlan(sol1, []);
        engine.Apply(plan1);

        // Capture state after first run
        string dppAfterFirst = File.ReadAllText(Path.Combine(_tempDir, "Directory.Packages.props"));
        string csprojAfterFirst = File.ReadAllText(Path.Combine(_tempDir, "src", "App", "App.csproj"));

        // Second run — should detect no packages with versions to extract
        var sol2 = detector.Detect(_tempDir) with { Projects = scanner.ScanProjects(_tempDir) };
        var plan2 = planner.CreatePlan(sol2, []);

        // The csproj no longer has Version= attributes, so no versions to centralize
        plan2.Stats.VersionsRemoved.Should().Be(0);

        // State should be unchanged
        string dppAfterSecond = File.ReadAllText(Path.Combine(_tempDir, "Directory.Packages.props"));
        dppAfterSecond.Should().Be(dppAfterFirst);
    }

    [Fact]
    public void FullMigration_DryRunDoesNotModify()
    {
        // Arrange
        string csprojPath = Path.Combine(_tempDir, "src", "App", "App.csproj");
        string csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """;
        CreateProjectFile("src/App/App.csproj", csprojContent);

        var detector = _services.GetRequiredService<ISolutionDetector>();
        var scanner = _services.GetRequiredService<IProjectScanner>();
        var planner = _services.GetRequiredService<IMigrationPlanner>();
        var engine = _services.GetRequiredService<IFileEditEngine>();

        var solution = detector.Detect(_tempDir) with { Projects = scanner.ScanProjects(_tempDir) };
        var plan = planner.CreatePlan(solution, []);

        // Act: dry run
        var written = engine.Apply(plan, dryRun: true);

        // Assert: no files modified on disk
        written.Should().NotBeEmpty();
        File.Exists(Path.Combine(_tempDir, "Directory.Packages.props")).Should().BeFalse();

        string unchanged = File.ReadAllText(csprojPath);
        unchanged.Should().Contain("Version=\"13.0.3\"");
    }

    [Fact]
    public void FullMigration_WithConflicts_BlocksApply()
    {
        CreateProjectFile("src/A/A.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        CreateProjectFile("src/B/B.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
              </ItemGroup>
            </Project>
            """);

        var detector = _services.GetRequiredService<ISolutionDetector>();
        var scanner = _services.GetRequiredService<IProjectScanner>();
        var planner = _services.GetRequiredService<IMigrationPlanner>();
        var engine = _services.GetRequiredService<IFileEditEngine>();

        var solution = detector.Detect(_tempDir) with { Projects = scanner.ScanProjects(_tempDir) };
        var plan = planner.CreatePlan(solution, []);

        plan.HasUnresolvedConflicts.Should().BeTrue();

        var act = () => engine.Apply(plan);
        act.Should().Throw<MigrationException>()
            .Which.Code.Should().Be(ErrorCodes.UnresolvedConflicts);
    }

    private void CreateProjectFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_tempDir, relativePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, content);
    }
}
