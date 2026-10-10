using System.Text.Json;
using CentralPackageMigrator.Cli;
using FluentAssertions;
using Xunit;

namespace CentralPackageMigrator.Integration.Tests;

/// <summary>
/// End to end: migrate a real project, then let NuGet itself judge the result with
/// <c>dotnet restore</c> (via the check command). Any version left on a PackageReference fails
/// restore with NU1008, which string assertions cannot catch.
/// Needs the .NET SDK and access to the configured NuGet feeds.
/// Exclude with: dotnet test --filter "Category!=E2E"
/// </summary>
[Trait("Category", "E2E")]
public sealed class RealRestoreTests : IDisposable
{
    private readonly string _root;

    public RealRestoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cpm-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Migrated_project_restores_with_Central_Package_Management()
    {
        // Packages this repository already restores, so they are usually in the local cache.
        await File.WriteAllTextAsync(Path.Combine(_root, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Version="6.12.2" Include="FluentAssertions" />
                <PackageReference Include="NSubstitute">
                  <Version>5.3.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(_root, "Directory.Build.props"), """
            <Project>
              <ItemGroup>
                <PackageReference Include="NuGet.Versioning" Version="7.6.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);

        var apply = await RunAsync("apply", "--root", _root);
        apply.ExitCode.Should().Be(ExitCodes.Success, apply.Error);

        var check = await RunAsync("check", "--json", "--root", _root, "--timeout", "300");

        using var json = JsonDocument.Parse(check.Output);
        check.ExitCode.Should().Be(ExitCodes.Success, check.Output);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exitCode = await Program.RunAsync(args, output, error, workingDirectory: _root);
        return (exitCode, output.ToString(), error.ToString());
    }
}
