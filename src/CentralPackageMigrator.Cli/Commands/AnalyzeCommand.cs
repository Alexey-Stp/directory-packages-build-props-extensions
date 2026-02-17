using System.CommandLine;
using System.CommandLine.Invocation;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "analyze" command: scans the solution and reports findings
/// including projects, packages, and version conflicts.
/// </summary>
public static class AnalyzeCommand
{
    public static Command Create()
    {
        var command = new Command("analyze", "Analyze solution for Central Package Management migration");

        var rootOption = new Option<string?>(
            "--root",
            "Solution root path (defaults to current directory)");

        var jsonOption = new Option<bool>(
            "--json",
            "Output results as JSON");

        var verboseOption = new Option<bool>(
            "--verbose",
            "Show verbose output");

        command.AddOption(rootOption);
        command.AddOption(jsonOption);
        command.AddOption(verboseOption);

        command.SetHandler(async (InvocationContext context) =>
        {
            var root = context.ParseResult.GetValueForOption(rootOption);
            var json = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var serviceProvider = (IServiceProvider)context.BindingContext.GetService(typeof(IServiceProvider))!;

            context.ExitCode = Execute(root, json, verbose, serviceProvider);
        });

        return command;
    }

    private static int Execute(string? root, bool json, bool verbose, IServiceProvider services)
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
            var conflictDetector = services.GetRequiredService<IConflictDetector>();

            var solution = detector.Detect(solutionRoot);
            var projects = scanner.ScanProjects(solution.RootPath);
            solution = solution with { Projects = projects };

            var conflicts = conflictDetector.DetectConflicts(projects);

            if (json)
            {
                // For JSON analysis output, create a lightweight plan to serialize
                var planner = services.GetRequiredService<IMigrationPlanner>();
                var plan = planner.CreatePlan(solution, []);
                var serializer = services.GetRequiredService<PlanSerializer>();
                var jsonRenderer = new JsonRenderer(serializer);
                jsonRenderer.RenderPlan(plan);
            }
            else
            {
                var consoleRenderer = new ConsoleRenderer();
                consoleRenderer.RenderAnalysis(solution, conflicts);

                if (verbose)
                {
                    // In verbose mode, also show per-project package details
                    Console.WriteLine("--- Package Details ---");
                    foreach (var project in projects)
                    {
                        Console.WriteLine($"  {project.RelativePath}:");
                        foreach (var pkgRef in project.PackageReferences)
                        {
                            var version = pkgRef.Version ?? "(inherited)";
                            var condition = pkgRef.Condition is not null ? $" [Condition: {pkgRef.Condition}]" : "";
                            Console.WriteLine($"    {pkgRef.Package.Id} {version}{condition}");
                        }
                    }
                    Console.WriteLine();
                }
            }

            return conflicts.Count > 0 ? ExitCodes.AnalysisWarnings : ExitCodes.Success;
        }
        catch (Exception ex)
        {
            var renderer = new ConsoleRenderer();
            renderer.RenderError(ex.Message);
            return ExitCodes.Error;
        }
    }
}
