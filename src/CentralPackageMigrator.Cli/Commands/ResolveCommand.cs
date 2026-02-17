using System.CommandLine;
using System.CommandLine.Invocation;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "resolve" command: applies conflict resolutions to a migration plan
/// and outputs the updated plan.
/// </summary>
public static class ResolveCommand
{
    public static Command Create()
    {
        var command = new Command("resolve", "Provide conflict resolutions and regenerate the migration plan");

        var rootOption = new Option<string?>(
            "--root",
            "Solution root path (defaults to current directory)");

        var jsonOption = new Option<bool>(
            "--json",
            "Output results as JSON");

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show verbose output");

        var dryRunOption = new Option<bool>(
            "--dry-run",
            "Preview the resolved plan without applying");

        var resolveOption = new Option<string[]>(
            "--resolve",
            "Conflict resolutions in format Package=Version (required)")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true
        };

        var outputOption = new Option<string?>(
            "--output",
            "Write resolved plan to file instead of stdout");

        command.AddOption(rootOption);
        command.AddOption(jsonOption);
        command.AddOption(verboseOption);
        command.AddOption(dryRunOption);
        command.AddOption(resolveOption);
        command.AddOption(outputOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var root = context.ParseResult.GetValueForOption(rootOption);
            var json = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var resolve = context.ParseResult.GetValueForOption(resolveOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var serviceProvider = (IServiceProvider)context.BindingContext.GetService(typeof(IServiceProvider))!;

            context.ExitCode = Execute(root, json, verbose, dryRun, resolve!, output, serviceProvider);
        });

        return command;
    }

    private static int Execute(
        string? root,
        bool json,
        bool verbose,
        bool dryRun,
        string[] resolve,
        string? output,
        IServiceProvider services)
    {
        try
        {
            var solutionRoot = root ?? Directory.GetCurrentDirectory();
            var consoleRenderer = new ConsoleRenderer();

            if (!Directory.Exists(solutionRoot))
            {
                consoleRenderer.RenderError($"Directory not found: {solutionRoot}");
                return ExitCodes.FileSystemError;
            }

            var resolutions = PlanCommand.ParseResolutions(resolve);

            if (resolutions.Count == 0)
            {
                consoleRenderer.RenderError("No resolutions provided. Use --resolve Package=Version.");
                return ExitCodes.InvalidArguments;
            }

            var detector = services.GetRequiredService<ISolutionDetector>();
            var scanner = services.GetRequiredService<IProjectScanner>();
            var planner = services.GetRequiredService<IMigrationPlanner>();

            var solution = detector.Detect(solutionRoot);
            var projects = scanner.ScanProjects(solution.RootPath);
            solution = solution with { Projects = projects };

            var plan = planner.CreatePlan(solution, resolutions);

            if (verbose && !json)
            {
                Console.WriteLine($"Applied {resolutions.Count} resolution(s):");
                foreach (var r in resolutions)
                {
                    Console.WriteLine($"  {r.PackageId} -> {r.Version}");
                }
                Console.WriteLine();
            }

            if (json || output is not null)
            {
                var serializer = services.GetRequiredService<PlanSerializer>();
                var jsonRenderer = new JsonRenderer(serializer);

                if (output is not null)
                {
                    jsonRenderer.RenderPlanToFile(plan, output);

                    if (!json)
                    {
                        Console.WriteLine($"Resolved plan written to {output}");
                    }
                }
                else
                {
                    jsonRenderer.RenderPlan(plan);
                }
            }
            else
            {
                consoleRenderer.RenderPlan(plan);
            }

            if (plan.HasUnresolvedConflicts)
            {
                if (!json)
                {
                    consoleRenderer.RenderError(
                        "Some conflicts remain unresolved. Provide additional --resolve arguments.");
                }
                return ExitCodes.UnresolvedConflicts;
            }

            return ExitCodes.Success;
        }
        catch (ArgumentException ex)
        {
            var renderer = new ConsoleRenderer();
            renderer.RenderError(ex.Message);
            return ExitCodes.InvalidArguments;
        }
        catch (Exception ex)
        {
            var renderer = new ConsoleRenderer();
            renderer.RenderError(ex.Message);
            return ExitCodes.Error;
        }
    }
}
