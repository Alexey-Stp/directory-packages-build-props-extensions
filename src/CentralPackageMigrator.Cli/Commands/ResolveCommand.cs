using System.CommandLine;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "resolve" command: applies conflict resolutions and outputs the updated plan.
/// Equivalent to "plan" with at least one required --resolve.
/// </summary>
internal static class ResolveCommand
{
    public static Command Create(CliContext context)
    {
        var command = new Command("resolve", "Provide conflict resolutions and regenerate the migration plan");

        var resolveOption = CliOptions.Resolve(required: true);
        var outputOption = CliOptions.Output("Write the resolved JSON plan to a file");

        // Kept so existing scripts don't break; resolve never writes project files anyway.
        var dryRunOption = CliOptions.DryRun("Deprecated: has no effect");
        dryRunOption.Hidden = true;

        command.Options.Add(resolveOption);
        command.Options.Add(outputOption);
        command.Options.Add(dryRunOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.Run(
            parseResult,
            context,
            (scope, ct) => PlanCommand.Execute(
                scope,
                parseResult.GetValue(outputOption),
                parseResult.GetValue(resolveOption),
                "Resolved plan",
                ct),
            cancellationToken));

        return command;
    }
}
