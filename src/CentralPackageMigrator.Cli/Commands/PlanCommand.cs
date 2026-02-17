using System.CommandLine;
using System.CommandLine.Invocation;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "plan" command: generates a migration plan and outputs it as JSON
/// or renders a human-readable summary.
/// </summary>
public static class PlanCommand
{
    public static Command Create()
    {
        var command = new Command("plan", "Generate a migration plan for Central Package Management");

        var rootOption = new Option<string?>(
            "--root",
            "Solution root path (defaults to current directory)");

        var jsonOption = new Option<bool>(
            "--json",
            "Output plan as JSON");

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show verbose output");

        var outputOption = new Option<string?>(
            "--output",
            "Write plan to file instead of stdout");

        var resolveOption = new Option<string[]?>(
            "--resolve",
            "Conflict resolutions in format Package=Version")
        {
            AllowMultipleArgumentsPerToken = true
        };

        command.AddOption(rootOption);
        command.AddOption(jsonOption);
        command.AddOption(verboseOption);
        command.AddOption(outputOption);
        command.AddOption(resolveOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var root = context.ParseResult.GetValueForOption(rootOption);
            var json = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var resolve = context.ParseResult.GetValueForOption(resolveOption);
            var serviceProvider = (IServiceProvider)context.BindingContext.GetService(typeof(IServiceProvider))!;

            context.ExitCode = Execute(root, json, verbose, output, resolve, serviceProvider);
        });

        return command;
    }

    private static int Execute(
        string? root,
        bool json,
        bool verbose,
        string? output,
        string[]? resolve,
        IServiceProvider services)
    {
        try
        {
            var solutionRoot = root ?? Directory.GetCurrentDirectory();

            if (!Directory.Exists(solutionRoot))
            {
                var renderer = new ConsoleRenderer();
                renderer.RenderError($"Directory not found: {solutionRoot}");
                return ExitCodes.FileSystemError;
            }

            var detector = services.GetRequiredService<ISolutionDetector>();
            var scanner = services.GetRequiredService<IProjectScanner>();
            var planner = services.GetRequiredService<IMigrationPlanner>();

            var solution = detector.Detect(solutionRoot);
            var projects = scanner.ScanProjects(solution.RootPath);
            solution = solution with { Projects = projects };

            var resolutions = ParseResolutions(resolve);
            var plan = planner.CreatePlan(solution, resolutions);

            if (json || output is not null)
            {
                var serializer = services.GetRequiredService<PlanSerializer>();
                var jsonRenderer = new JsonRenderer(serializer);

                if (output is not null)
                {
                    jsonRenderer.RenderPlanToFile(plan, output);

                    if (!json)
                    {
                        var consoleRenderer = new ConsoleRenderer();
                        Console.WriteLine($"Plan written to {output}");
                    }
                }
                else
                {
                    jsonRenderer.RenderPlan(plan);
                }
            }
            else
            {
                var consoleRenderer = new ConsoleRenderer();
                consoleRenderer.RenderPlan(plan);
            }

            if (plan.HasUnresolvedConflicts)
                return ExitCodes.UnresolvedConflicts;

            if (plan.Warnings.Count > 0)
                return ExitCodes.AnalysisWarnings;

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            var renderer = new ConsoleRenderer();
            renderer.RenderError(ex.Message);
            return ExitCodes.Error;
        }
    }

    internal static IReadOnlyList<ConflictResolution> ParseResolutions(string[]? resolve)
    {
        if (resolve is null or { Length: 0 })
            return [];

        var resolutions = new List<ConflictResolution>(resolve.Length);

        foreach (var entry in resolve)
        {
            var eqIndex = entry.IndexOf('=');
            if (eqIndex <= 0 || eqIndex >= entry.Length - 1)
            {
                throw new ArgumentException(
                    $"Invalid resolution format: '{entry}'. Expected format: Package=Version");
            }

            resolutions.Add(new ConflictResolution
            {
                PackageId = entry[..eqIndex],
                Version = entry[(eqIndex + 1)..]
            });
        }

        return resolutions;
    }
}
