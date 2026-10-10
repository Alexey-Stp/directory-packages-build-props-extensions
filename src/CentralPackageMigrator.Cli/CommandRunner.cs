using System.CommandLine;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Services;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace CentralPackageMigrator.Cli;

/// <summary>
/// Process-wide state for one CLI invocation. <see cref="Services"/> is set after parsing,
/// once the verbosity is known.
/// </summary>
internal sealed class CliContext
{
    public CliContext(TextWriter output, TextWriter error, GlobalOptions options)
    {
        Output = output;
        Error = error;
        Options = options;
    }

    public GlobalOptions Options { get; }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    public IServiceProvider? Services { get; set; }

    public string WorkingDirectory { get; init; } = Directory.GetCurrentDirectory();
}

/// <summary>
/// Everything a command handler needs: parsed global options, services and renderers.
/// </summary>
internal sealed class CommandScope
{
    public CommandScope(ParseResult parseResult, CliContext context)
    {
        ParseResult = parseResult;
        Context = context;
        Services = context.Services ?? throw new InvalidOperationException("Services were not initialized.");
        Json = parseResult.GetValue(context.Options.Json);
        Verbose = parseResult.GetValue(context.Options.Verbose);
        Console = new ConsoleRenderer(context.Output, context.Error);
        JsonOutput = new JsonRenderer(Services.GetRequiredService<PlanSerializer>(), context.Output);
    }

    public ParseResult ParseResult { get; }

    public CliContext Context { get; }

    public IServiceProvider Services { get; }

    public bool Json { get; }

    public bool Verbose { get; }

    public ConsoleRenderer Console { get; }

    public JsonRenderer JsonOutput { get; }

    public IMigrationService Migration => Services.GetRequiredService<IMigrationService>();

    public MigrationRequest CreateRequest(IReadOnlyList<ConflictResolution>? resolutions = null) => new()
    {
        RootPath = ParseResult.GetValue(Context.Options.Root),
        WorkingDirectory = Context.WorkingDirectory,
        Resolutions = resolutions ?? []
    };

    public void WriteError(MigrationError error)
    {
        if (Json)
        {
            JsonOutput.RenderError(error);
        }
        else
        {
            Console.RenderError(error.Message);
            if (Verbose && error.Detail is not null)
            {
                Console.RenderErrorDetail(error.Detail);
            }
        }
    }
}

/// <summary>
/// Runs a command body with uniform error handling: every failure becomes a structured
/// <see cref="MigrationError"/> (JSON on stdout in --json mode, text on stderr otherwise)
/// and a well-defined exit code.
/// </summary>
internal static class CommandRunner
{
    public static async Task<int> RunAsync(
        ParseResult parseResult,
        CliContext context,
        Func<CommandScope, CancellationToken, Task<int>> body,
        CancellationToken cancellationToken)
    {
        var scope = new CommandScope(parseResult, context);

        try
        {
            return await body(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (MigrationException ex)
        {
            scope.WriteError(ex.ToError());
            return ExitCodes.ForError(ex.Code);
        }
        catch (OperationCanceledException)
        {
            scope.WriteError(new MigrationError { Code = ErrorCodes.Cancelled, Message = "Operation cancelled." });
            return ExitCodes.Error;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            scope.WriteError(new MigrationError { Code = ErrorCodes.IoFailed, Message = ex.Message });
            return ExitCodes.FileSystemError;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            scope.WriteError(new MigrationError
            {
                Code = ErrorCodes.Unexpected,
                Message = ex.Message,
                Detail = scope.Verbose ? ex.ToString() : null
            });
            return ExitCodes.Error;
        }
    }

    public static Task<int> Run(
        ParseResult parseResult,
        CliContext context,
        Func<CommandScope, CancellationToken, int> body,
        CancellationToken cancellationToken) =>
        RunAsync(parseResult, context, (scope, ct) => Task.FromResult(body(scope, ct)), cancellationToken);
}
