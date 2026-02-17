using System.CommandLine;
using System.CommandLine.Invocation;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "apply" command: runs the full migration pipeline
/// (detect -> scan -> plan -> apply) or applies from a saved plan file.
/// </summary>
public static class ApplyCommand
{
    public static Command Create()
    {
        var command = new Command("apply", "Apply the Central Package Management migration");

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
            "Preview changes without writing files");

        var planFileOption = new Option<string?>(
            "--plan-file",
            "Apply from a saved plan file instead of generating a new plan");

        var resolveOption = new Option<string[]?>(
            "--resolve",
            "Conflict resolutions in format Package=Version")
        {
            AllowMultipleArgumentsPerToken = true
        };

        command.AddOption(rootOption);
        command.AddOption(jsonOption);
        command.AddOption(verboseOption);
        command.AddOption(dryRunOption);
        command.AddOption(planFileOption);
        command.AddOption(resolveOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var root = context.ParseResult.GetValueForOption(rootOption);
            var json = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
            var planFile = context.ParseResult.GetValueForOption(planFileOption);
            var resolve = context.ParseResult.GetValueForOption(resolveOption);
            var serviceProvider = (IServiceProvider)context.BindingContext.GetService(typeof(IServiceProvider))!;

            context.ExitCode = Execute(root, json, verbose, dryRun, planFile, resolve, serviceProvider);
        });

        return command;
    }

    private static int Execute(
        string? root,
        bool json,
        bool verbose,
        bool dryRun,
        string? planFile,
        string[]? resolve,
        IServiceProvider services)
    {
        try
        {
            var consoleRenderer = new ConsoleRenderer();
            var serializer = services.GetRequiredService<PlanSerializer>();
            MigrationPlan plan;

            if (planFile is not null)
            {
                // Load plan from file
                if (!File.Exists(planFile))
                {
                    consoleRenderer.RenderError($"Plan file not found: {planFile}");
                    return ExitCodes.FileSystemError;
                }

                var planJson = File.ReadAllText(planFile);
                plan = serializer.Deserialize(planJson);

                if (verbose)
                {
                    Console.WriteLine($"Loaded plan from {planFile}");
                }
            }
            else
            {
                // Generate plan from scratch
                var solutionRoot = root ?? Directory.GetCurrentDirectory();

                if (!Directory.Exists(solutionRoot))
                {
                    consoleRenderer.RenderError($"Directory not found: {solutionRoot}");
                    return ExitCodes.FileSystemError;
                }

                var detector = services.GetRequiredService<ISolutionDetector>();
                var scanner = services.GetRequiredService<IProjectScanner>();
                var planner = services.GetRequiredService<IMigrationPlanner>();

                var solution = detector.Detect(solutionRoot);
                var projects = scanner.ScanProjects(solution.RootPath);
                solution = solution with { Projects = projects };

                var resolutions = PlanCommand.ParseResolutions(resolve);
                plan = planner.CreatePlan(solution, resolutions);
            }

            // Check for unresolved conflicts
            if (plan.HasUnresolvedConflicts)
            {
                consoleRenderer.RenderConflicts(plan.Conflicts);
                consoleRenderer.RenderError(
                    "Cannot apply migration with unresolved conflicts. " +
                    "Use --resolve Package=Version to resolve them.");
                return ExitCodes.UnresolvedConflicts;
            }

            if (plan.IsNoOp)
            {
                if (json)
                {
                    var jsonRenderer = new JsonRenderer(serializer);
                    jsonRenderer.RenderPlan(plan);
                }
                else
                {
                    Console.WriteLine("No changes needed - solution appears to already use Central Package Management.");
                }
                return ExitCodes.Success;
            }

            // Show plan summary before applying
            if (verbose && !json)
            {
                consoleRenderer.RenderPlan(plan);
            }

            // Apply the plan
            var editEngine = services.GetRequiredService<IFileEditEngine>();
            var writtenFiles = editEngine.Apply(plan, dryRun);

            if (json)
            {
                var jsonRenderer = new JsonRenderer(serializer);
                jsonRenderer.RenderPlan(plan);
            }
            else
            {
                consoleRenderer.RenderApplyResult(writtenFiles, dryRun);

                if (plan.Warnings.Count > 0)
                {
                    consoleRenderer.RenderWarnings(plan.Warnings);
                }
            }

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            var renderer = new ConsoleRenderer();
            renderer.RenderError(ex.Message);
            return ExitCodes.Error;
        }
    }
}
