using System.CommandLine;
using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "plan" command: generates a migration plan and outputs it as JSON
/// or renders a human-readable summary.
/// </summary>
internal static class PlanCommand
{
    public static Command Create(CliContext context)
    {
        var command = new Command("plan", "Generate a migration plan for Central Package Management");

        var outputOption = CliOptions.Output("Write the JSON plan to a file");
        var resolveOption = CliOptions.Resolve();

        command.Options.Add(outputOption);
        command.Options.Add(resolveOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.Run(
            parseResult,
            context,
            (scope, ct) => Execute(
                scope,
                parseResult.GetValue(outputOption),
                parseResult.GetValue(resolveOption),
                "Plan",
                ct),
            cancellationToken));

        return command;
    }

    /// <summary>Shared by plan and resolve.</summary>
    internal static int Execute(
        CommandScope scope,
        string? output,
        string[]? resolve,
        string label,
        CancellationToken cancellationToken)
    {
        var resolutions = ConflictResolution.ParseMany(resolve);
        var plan = scope.Migration.CreatePlan(scope.CreateRequest(resolutions), cancellationToken);

        if (output is not null)
        {
            scope.JsonOutput.RenderPlanToFile(plan, output);
        }

        if (scope.Json)
        {
            scope.JsonOutput.RenderPlan(plan);
        }
        else
        {
            scope.Console.RenderPlan(plan);
            if (output is not null)
            {
                scope.Console.WriteLine($"{label} written to {output}");
            }
        }

        return ExitCodes.ForPlan(plan);
    }
}
