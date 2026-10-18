using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CentralPackageMigrator.Cli;
using CentralPackageMigrator.Core.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CentralPackageMigrator.Integration.Tests;

/// <summary>
/// The JSON contract IDE adapters rely on: in --json mode stdout is always a single JSON
/// document — on success and on every kind of failure — and exit codes follow ExitCodes.
/// </summary>
public sealed class CliContractTests : IDisposable
{
    private readonly string _root;

    public CliContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cpm-cli-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("plan")]
    [InlineData("apply", "--dry-run")]
    [InlineData("apply")]
    public async Task Successful_commands_write_only_JSON_to_stdout(params string[] command)
    {
        WriteProject("A", ("Pkg", "1.0.0"));

        var result = await RunAsync([.. command, "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.1");
    }

    [Fact]
    public async Task Global_options_are_accepted_before_the_command()
    {
        WriteProject("A", ("Pkg", "1.0.0"));

        var result = await RunAsync(["--json", "--root", _root, "plan"]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        using var _ = JsonDocument.Parse(result.Output);
    }

    [Fact]
    public async Task Plan_with_conflicts_exits_2_with_clean_JSON()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        WriteProject("B", ("Pkg", "2.0.0"));

        var result = await RunAsync(["plan", "--json", "--root", _root]);

        result.ExitCode.Should().Be(ExitCodes.UnresolvedConflicts);
        using var json = JsonDocument.Parse(result.Output);
        var conflict = json.RootElement.GetProperty("conflicts")[0];
        conflict.GetProperty("suggestedVersion").GetString().Should().Be("2.0.0");
    }

    [Fact]
    public async Task Resolve_resolves_conflicts()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        WriteProject("B", ("Pkg", "2.0.0"));

        var result = await RunAsync(["resolve", "--json", "--root", _root, "--resolve", "Pkg=2.0.0"]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("hasUnresolvedConflicts").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Apply_result_lists_written_files()
    {
        WriteProject("A", ("Pkg", "1.0.0"));

        var result = await RunAsync(["apply", "--json", "--root", _root]);

        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("dryRun").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("writtenFiles").GetArrayLength().Should().Be(2);
        json.RootElement.GetProperty("plan").GetProperty("schemaVersion").GetString().Should().Be("1.1");
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task Failures_write_a_JSON_error_with_a_stable_code(string[] args, int exitCode, string errorCode)
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        WriteProject("B", ("Pkg", "2.0.0"));

        var result = await RunAsync([.. args.Select(a => a.Replace("{root}", _root, StringComparison.Ordinal)), "--json"]);

        result.ExitCode.Should().Be(exitCode);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(errorCode);
    }

    public static TheoryData<string[], int, string> FailureCases() => new()
    {
        { ["plan", "--root", "{root}/missing"], ExitCodes.FileSystemError, "ERR_SOLUTION_NOT_FOUND" },
        { ["plan", "--root", "{root}", "--resolve", "Pkg=not-a-version"], ExitCodes.InvalidArguments, "ERR_INVALID_ARGUMENTS" },
        { ["plan", "--root", "{root}", "--resolve", "Pkg=1.0.0", "--resolve", "Pkg=2.0.0"], ExitCodes.InvalidArguments, "ERR_INVALID_ARGUMENTS" },
        { ["plan", "--root", "{root}", "--no-such-option"], ExitCodes.InvalidArguments, "ERR_INVALID_ARGUMENTS" },
        { ["resolve", "--root", "{root}"], ExitCodes.InvalidArguments, "ERR_INVALID_ARGUMENTS" },
        { ["apply", "--root", "{root}"], ExitCodes.UnresolvedConflicts, "ERR_UNRESOLVED_CONFLICTS" },
        { ["apply", "--root", "{root}", "--plan-file", "{root}/missing.json"], ExitCodes.FileSystemError, "ERR_PLAN_FILE_NOT_FOUND" },
        { ["apply", "--root", "{root}", "--plan-file", "x.json", "--resolve", "Pkg=1.0.0"], ExitCodes.InvalidArguments, "ERR_INVALID_ARGUMENTS" },
    };

    [Fact]
    public async Task Text_mode_errors_go_to_stderr()
    {
        var result = await RunAsync(["plan", "--root", Path.Combine(_root, "missing")]);

        result.ExitCode.Should().Be(ExitCodes.FileSystemError);
        result.Output.Should().BeEmpty();
        result.Error.Should().Contain("ERROR:");
    }

    [Fact]
    public async Task Tampered_plan_file_is_rejected()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        string planFile = Path.Combine(_root, "plan.json");
        (await RunAsync(["plan", "--root", _root, "--output", planFile])).ExitCode.Should().Be(ExitCodes.Success);

        var plan = JsonNode.Parse(await File.ReadAllTextAsync(planFile))!;
        plan["creates"]![0]!["content"] = "<Project><Target Name=\"Evil\" BeforeTargets=\"Build\" /></Project>";
        await File.WriteAllTextAsync(planFile, plan.ToJsonString());

        var result = await RunAsync(["apply", "--json", "--root", _root, "--plan-file", planFile]);

        result.ExitCode.Should().Be(ExitCodes.Error);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ERR_STALE_PLAN");
        File.Exists(Path.Combine(_root, "Directory.Packages.props")).Should().BeFalse();
    }

    [Fact]
    public async Task Saved_plan_file_applies_when_unchanged()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        string planFile = Path.Combine(_root, "plan.json");
        await RunAsync(["plan", "--root", _root, "--output", planFile]);

        var result = await RunAsync(["apply", "--root", _root, "--plan-file", planFile]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        File.Exists(Path.Combine(_root, "Directory.Packages.props")).Should().BeTrue();
    }

    [Fact]
    public async Task Check_reports_each_step_as_JSON()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        var runner = new FakeProcessRunner(exitCode: 0);

        var result = await RunAsync(["check", "--json", "--build", "--root", _root], runner);

        result.ExitCode.Should().Be(ExitCodes.Success);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var steps = json.RootElement.GetProperty("steps");
        steps.GetArrayLength().Should().Be(2);
        steps[1].GetProperty("name").GetString().Should().Be("build");
        runner.Calls.Should().HaveCount(2);
        runner.Calls[1].Should().Equal("build", "--no-restore");
    }

    [Fact]
    public async Task Check_failure_exits_6_and_skips_build()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        var runner = new FakeProcessRunner(exitCode: 1);

        var result = await RunAsync(["check", "--json", "--build", "--root", _root, "--timeout", "30"], runner);

        result.ExitCode.Should().Be(ExitCodes.CheckFailed);
        using var json = JsonDocument.Parse(result.Output);
        var steps = json.RootElement.GetProperty("steps");
        steps[0].GetProperty("errorCode").GetString().Should().Be("ERR_RESTORE_FAILED");
        steps[1].GetProperty("skipped").GetBoolean().Should().BeTrue();
        runner.Calls.Should().ContainSingle();
        runner.Timeouts.Should().OnlyContain(t => t == TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Check_targets_the_solution_file_when_present()
    {
        WriteProject("A", ("Pkg", "1.0.0"));
        string solution = Path.Combine(_root, "App.sln");
        await File.WriteAllTextAsync(solution, "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        var runner = new FakeProcessRunner(exitCode: 0);

        await RunAsync(["check", "--root", _root], runner);

        runner.Calls.Single().Should().Equal("restore", solution);
    }

    [Fact]
    public async Task Version_option_prints_the_informational_version_of_the_tool()
    {
        string expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        var result = await RunAsync(["--version"]);

        result.ExitCode.Should().Be(ExitCodes.Success);
        result.Output.Trim().Should().Be(expected);
        expected.Should().NotContain("+", "the version must equal the package version exactly");
    }

    private void WriteProject(string name, params (string Id, string Version)[] packages)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        string references = string.Concat(packages.Select(p =>
            $"    <PackageReference Include=\"{p.Id}\" Version=\"{p.Version}\" />\n"));
        File.WriteAllText(
            Path.Combine(dir, $"{name}.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" + references + "  </ItemGroup>\n</Project>\n");
    }

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
                if (processRunner is not null)
                {
                    services.AddSingleton(processRunner);
                }
            },
            workingDirectory: _root);

        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly int _exitCode;

        public FakeProcessRunner(int exitCode)
        {
            _exitCode = exitCode;
        }

        public List<IReadOnlyList<string>> Calls { get; } = [];

        public List<TimeSpan> Timeouts { get; } = [];

        public Task<ProcessResult> RunAsync(
            string command,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(arguments);
            Timeouts.Add(timeout);
            return Task.FromResult(new ProcessResult
            {
                ExitCode = _exitCode,
                StandardOutput = "out",
                StandardError = _exitCode == 0 ? string.Empty : "error NU1008"
            });
        }
    }
}
