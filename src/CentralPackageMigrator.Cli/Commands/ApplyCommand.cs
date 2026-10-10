using System.CommandLine;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli.Commands;

/// <summary>
/// The "apply" command: plans and applies the migration, or applies a saved plan file
/// after verifying it still matches the solution.
/// </summary>
internal static class ApplyCommand
{
    public static Command Create(CliContext context)
    {
        var command = new Command("apply", "Apply the Central Package Management migration");

        var dryRunOption = CliOptions.DryRun("Run every check but write nothing");
        var planFileOption = new Option<string?>("--plan-file")
        {
            Description = "Apply a saved plan; it must still match the current files"
        };
        var resolveOption = CliOptions.Resolve();

        command.Options.Add(dryRunOption);
        command.Options.Add(planFileOption);
        command.Options.Add(resolveOption);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.Run(
            parseResult,
            context,
            (scope, ct) => Execute(
                scope,
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(planFileOption),
                parseResult.GetValue(resolveOption),
                ct),
            cancellationToken));

        return command;
    }

    private static int Execute(
        CommandScope scope,
        bool dryRun,
        string? planFile,
        string[]? resolve,
        CancellationToken cancellationToken)
    {
        ApplyResult result;

        if (planFile is not null)
        {
            if (resolve is { Length: > 0 })
            {
                throw new MigrationException(
                    ErrorCodes.InvalidArguments,
                    "--resolve cannot be combined with --plan-file; the plan file already records its resolutions.");
            }

            var serializer = scope.Services.GetRequiredService<PlanSerializer>();
            var savedPlan = serializer.DeserializeFromFile(planFile);
            result = scope.Migration.ApplySavedPlan(savedPlan, scope.CreateRequest(), dryRun, cancellationToken);
        }
        else
        {
            var resolutions = ConflictResolution.ParseMany(resolve);
            var plan = scope.Migration.CreatePlan(scope.CreateRequest(resolutions), cancellationToken);

            if (plan.HasUnresolvedConflicts)
            {
                if (!scope.Json)
                {
                    scope.Console.RenderConflicts(plan.Conflicts);
                }

                throw new MigrationException(
                    ErrorCodes.UnresolvedConflicts,
                    "Cannot apply migration with unresolved conflicts. Use --resolve Package=Version to resolve them.");
            }

            if (scope.Verbose && !scope.Json)
            {
                scope.Console.RenderPlan(plan);
            }

            result = scope.Migration.ApplyPlan(plan, dryRun, cancellationToken);
        }

        if (scope.Json)
        {
            scope.JsonOutput.Render(result);
        }
        else if (result.Plan.IsNoOp)
        {
            scope.Console.WriteLine("No changes needed - solution appears to already use Central Package Management.");
            scope.Console.RenderWarnings(result.Plan.Warnings);
        }
        else
        {
            scope.Console.RenderApplyResult(result.WrittenFiles, dryRun);
            scope.Console.RenderWarnings(result.Plan.Warnings);
        }

        return ExitCodes.ForPlan(result.Plan);
    }
}
