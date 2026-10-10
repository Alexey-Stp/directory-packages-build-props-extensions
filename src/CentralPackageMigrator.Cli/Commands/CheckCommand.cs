using System.CommandLine;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "check" command: verifies the migration by running dotnet restore
/// and optionally dotnet build against the solution file (or root directory).
/// </summary>
internal static class CheckCommand
{
    private const int DefaultTimeoutSeconds = 600;

    public static Command Create(CliContext context)
    {
        var command = new Command("check", "Verify migration by running dotnet restore and optionally dotnet build");

        var buildOption = new Option<bool>("--build")
        {
            Description = "Also run dotnet build after restore"
        };

        var timeoutOption = new Option<int>("--timeout")
        {
            Description = "Timeout in seconds for each dotnet command",
            DefaultValueFactory = _ => DefaultTimeoutSeconds
        };
        timeoutOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() <= 0)
            {
                result.AddError("--timeout must be a positive number of seconds.");
            }
        });

        command.Options.Add(buildOption);
        command.Options.Add(timeoutOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(
            parseResult,
            context,
            (scope, ct) => ExecuteAsync(
                scope,
                parseResult.GetValue(buildOption),
                TimeSpan.FromSeconds(parseResult.GetValue(timeoutOption)),
                ct),
            cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        CommandScope scope,
        bool build,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var solution = scope.Migration.LoadSolution(scope.CreateRequest(), cancellationToken);
        var runner = scope.Services.GetRequiredService<IProcessRunner>();

        // Target the solution file when there is one; a directory with several
        // project or solution files makes `dotnet restore` fail with MSB1011.
        var target = solution.SolutionFilePath is not null ? new[] { solution.SolutionFilePath } : [];
        var steps = new List<CheckStep>();

        var restore = await RunStepAsync(
            scope, runner, "restore", ["restore", .. target], solution.RootPath, timeout, ErrorCodes.RestoreFailed, cancellationToken)
            .ConfigureAwait(false);
        steps.Add(restore);

        if (build)
        {
            if (restore.Success)
            {
                steps.Add(await RunStepAsync(
                    scope, runner, "build", ["build", .. target, "--no-restore"], solution.RootPath, timeout, ErrorCodes.BuildFailed, cancellationToken)
                    .ConfigureAwait(false));
            }
            else
            {
                steps.Add(new CheckStep { Name = "build", Success = false, Skipped = true });
            }
        }

        bool success = steps.All(s => s.Success);

        if (scope.Json)
        {
            scope.JsonOutput.Render(new CheckResult { Success = success, Steps = steps });
        }
        else
        {
            scope.Console.WriteLine();
            scope.Console.WriteLine(success
                ? "All checks passed."
                : "Some checks failed. Review the output above for details.");
            scope.Console.WriteLine();
        }

        return success ? ExitCodes.Success : ExitCodes.CheckFailed;
    }

    private static async Task<CheckStep> RunStepAsync(
        CommandScope scope,
        IProcessRunner runner,
        string name,
        string[] arguments,
        string workingDirectory,
        TimeSpan timeout,
        string errorCode,
        CancellationToken cancellationToken)
    {
        if (!scope.Json)
        {
            scope.Console.WriteLine();
            scope.Console.WriteLine($"Running dotnet {name}...");
        }

        var result = await runner.RunAsync("dotnet", arguments, workingDirectory, timeout, cancellationToken)
            .ConfigureAwait(false);

        bool includeOutput = scope.Verbose || !result.Success;

        if (!scope.Json)
        {
            scope.Console.RenderStatus(result.Success, result.TimedOut
                ? $"dotnet {name} timed out after {timeout.TotalSeconds:0}s."
                : $"dotnet {name} {(result.Success ? "succeeded" : "failed")}.");

            if (includeOutput && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                scope.Console.WriteLine();
                scope.Console.WriteLine(result.StandardOutput);
            }

            if (includeOutput && !string.IsNullOrWhiteSpace(result.StandardError))
            {
                scope.Console.RenderErrorDetail(result.StandardError);
            }
        }

        return new CheckStep
        {
            Name = name,
            Success = result.Success,
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            ErrorCode = result.Success ? null : errorCode,
            StandardOutput = includeOutput ? result.StandardOutput : null,
            StandardError = includeOutput ? result.StandardError : null
        };
    }
}
