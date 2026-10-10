using System.CommandLine;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "analyze" command: scans the solution and reports findings
/// including projects, packages, conflicts and warnings. Never writes files.
/// </summary>
internal static class AnalyzeCommand
{
    public static Command Create(CliContext context)
    {
        var command = new Command("analyze", "Analyze solution for Central Package Management migration");

        command.SetAction((parseResult, cancellationToken) =>
            CommandRunner.Run(parseResult, context, Execute, cancellationToken));

        return command;
    }

    private static int Execute(CommandScope scope, CancellationToken cancellationToken)
    {
        var result = scope.Migration.Analyze(scope.CreateRequest(), cancellationToken);

        if (scope.Json)
        {
            scope.JsonOutput.RenderPlan(result.Plan);
        }
        else
        {
            scope.Console.RenderAnalysis(result.Solution, result.Plan, scope.Verbose);
        }

        return ExitCodes.ForPlan(result.Plan);
    }
}
