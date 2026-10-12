using System.Text.Json;
using System.Text.RegularExpressions;
using CentralPackageMigrator.Cli;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.TestUtilities;
using FluentAssertions;
using Json.Schema;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralPackageMigrator.Integration.Tests.Contract;

/// <summary>
/// Freezes the JSON contract IDE adapters bind to. Every --json output must validate against the
/// committed schemas in docs/schemas and match the approved snapshot byte for byte. Any shape change
/// fails here; an intentional change must update the approved files, the schemas and
/// <see cref="MigrationPlan.CurrentSchemaVersion"/> in the same PR (CI enforces the bump).
/// Set CPM_UPDATE_CONTRACT=1 to rewrite the approved files after such a change.
/// </summary>
public sealed partial class JsonContractTests : IDisposable
{
    private const string ContractChanged =
        "the JSON contract changed. If intentional, rerun with CPM_UPDATE_CONTRACT=1, update docs/schemas " +
        "and docs/JSON_SCHEMA.md, and bump MigrationPlan.CurrentSchemaVersion in the same PR";

    private static readonly string ContractDirectory = Path.Combine(AppContext.BaseDirectory, "Contract");

    private readonly string _root;

    public JsonContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cpm-contract-" + Guid.NewGuid().ToString("N")[..8]);
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
    public async Task Analyze_with_conflicts()
    {
        WriteConflictSolution();

        var result = await RunAsync(["analyze", "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.UnresolvedConflicts);
        AssertContract("analyze-conflicts", "plan.schema.json", result.Output);
    }

    [Fact]
    public async Task Plan_with_resolved_conflict()
    {
        WriteConflictSolution();

        var result = await RunAsync(["plan", "--json", "--root", _root, "--resolve", "Newtonsoft.Json=13.0.3"]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        AssertContract("plan-resolved", "plan.schema.json", result.Output);
    }

    [Fact]
    public async Task Plan_with_existing_central_file()
    {
        WriteExistingCentralSolution();

        var result = await RunAsync(["plan", "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        AssertContract("plan-existing-central", "plan.schema.json", result.Output);
    }

    [Fact]
    public async Task Apply_dry_run()
    {
        WriteExistingCentralSolution();

        var result = await RunAsync(["apply", "--dry-run", "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        AssertContract("apply-dry-run", "apply-result.schema.json", result.Output);
    }

    [Fact]
    public async Task Check_with_failed_restore()
    {
        WriteFile("App.csproj", Project(("Serilog", "3.1.1")));

        var result = await RunAsync(["check", "--build", "--json", "--root", _root], new FailingRestoreRunner());

        result.ExitCode.Should().Be(ExitCodes.CheckFailed);
        AssertContract("check-failed", "check-result.schema.json", result.Output);
    }

    [Fact]
    public async Task Error()
    {
        var result = await RunAsync(["plan", "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.Error);
        AssertContract("error-no-projects", "error.schema.json", result.Output);
    }

    [Fact]
    public void Schemas_declare_the_current_schema_version()
    {
        foreach (string path in Directory.GetFiles(Path.Combine(ContractDirectory, "schemas"), "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(path));
            string? declared = schema.RootElement
                .GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString();

            declared.Should().Be(
                MigrationPlan.CurrentSchemaVersion,
                "{0} must be updated together with MigrationPlan.CurrentSchemaVersion",
                Path.GetFileName(path));
        }
    }

    [Fact]
    public void Documented_full_example_is_a_valid_plan()
    {
        string markdown = File.ReadAllText(Path.Combine(ContractDirectory, "JSON_SCHEMA.md"));
        var example = FullExampleRegex().Match(markdown);
        example.Success.Should().BeTrue("docs/JSON_SCHEMA.md must contain a '## Full Example' json block");

        AssertValid("plan.schema.json", example.Groups["json"].Value, "docs/JSON_SCHEMA.md full example");
    }

    [GeneratedRegex(@"## Full Example\s+```json\s*\n(?<json>.*?)\n```", RegexOptions.Singleline)]
    private static partial Regex FullExampleRegex();

    private void AssertContract(string name, string schemaFile, string output)
    {
        AssertValid(schemaFile, output, name);

        string actual = Normalize(output);
        string approvedPath = Path.Combine(ContractDirectory, "approved", name + ".json");

        if (Environment.GetEnvironmentVariable("CPM_UPDATE_CONTRACT") == "1")
        {
            File.WriteAllText(Path.Combine(FindSourceApprovedDirectory(), name + ".json"), actual);
            return;
        }

        File.Exists(approvedPath).Should().BeTrue($"approved snapshot {name}.json must exist; {ContractChanged}");
        string approved = File.ReadAllText(approvedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        actual.Should().Be(approved, $"{name}: {ContractChanged}");
    }

    private static void AssertValid(string schemaFile, string json, string subject)
    {
        using var document = JsonDocument.Parse(json);
        var results = LoadSchema(schemaFile).Evaluate(
            document.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

        var errors = new[] { results }.Concat(results.Details ?? [])
            .Where(d => d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key}: {e.Value}"))
            .ToList();

        results.IsValid.Should().BeTrue(
            "{0} must validate against docs/schemas/{1}:{2}{3}",
            subject,
            schemaFile,
            Environment.NewLine,
            string.Join(Environment.NewLine, errors));
    }

    private static JsonSchema LoadSchema(string schemaFile)
    {
        // apply-result references plan.schema.json, so every schema is registered in one registry.
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        JsonSchema? requested = null;
        foreach (string path in Directory.GetFiles(Path.Combine(ContractDirectory, "schemas"), "*.schema.json").Order())
        {
            var schema = JsonSchema.FromFile(path, options);
            if (Path.GetFileName(path) == schemaFile)
            {
                requested = schema;
            }
        }

        return requested ?? throw new FileNotFoundException("Schema not found", schemaFile);
    }

    /// <summary>
    /// Makes output comparable across machines and operating systems: the temp root becomes
    /// &lt;ROOT&gt; and line endings become \n.
    /// </summary>
    private string Normalize(string output)
    {
        string escapedRoot = JsonSerializer.Serialize(_root)[1..^1];
        return output
            .Replace(escapedRoot, "<ROOT>", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindSourceApprovedDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CentralPackageMigrator.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Repository root (CentralPackageMigrator.sln) not found");
        }

        string approved = Path.Combine(
            directory.FullName, "tests", "CentralPackageMigrator.Integration.Tests", "Contract", "approved");
        Directory.CreateDirectory(approved);
        return approved;
    }

    // Fixtures are flat (no subdirectories) and LF-only so paths, hashes and generated content are
    // identical on every OS.
    private void WriteConflictSolution()
    {
        WriteFile(
            "App.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\n" +
            "    <PackageReference Include=\"Serilog\" Version=\"3.1.1\" />\n" +
            "  </ItemGroup>\n" +
            "  <ItemGroup Condition=\"'$(TargetFramework)' == 'net48'\">\n" +
            "    <PackageReference Include=\"System.ValueTuple\" Version=\"4.5.0\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        WriteFile("Lib.csproj", Project(("Newtonsoft.Json", "12.0.3"), ("Serilog", "3.1.1")));
    }

    private void WriteExistingCentralSolution()
    {
        WriteFile(
            "Directory.Packages.props",
            "<Project>\n" +
            "  <ItemGroup>\n" +
            "    <PackageVersion Include=\"Serilog\" Version=\"3.1.1\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");
        WriteFile("App.csproj", Project(("Newtonsoft.Json", "13.0.3"), ("Serilog", "3.1.1")));
    }

    private static string Project(params (string Id, string Version)[] packages) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
        string.Concat(packages.Select(p => $"    <PackageReference Include=\"{p.Id}\" Version=\"{p.Version}\" />\n")) +
        "  </ItemGroup>\n</Project>\n";

    private void WriteFile(string name, string content) => File.WriteAllText(Path.Combine(_root, name), content);

    private async Task<CliResult> RunAsync(string[] args, IProcessRunner? processRunner = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = await Program.RunAsync(
            args,
            output,
            error,
            services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                if (processRunner is not null)
                {
                    services.AddSingleton(processRunner);
                }
            },
            workingDirectory: _root);

        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class FailingRestoreRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult
            {
                ExitCode = 1,
                StandardOutput = "Determining projects to restore...",
                StandardError = "error NU1008: Projects that use central package version management should not define the version on the PackageReference items",
                Duration = TimeSpan.FromMilliseconds(1830)
            });
    }
}
