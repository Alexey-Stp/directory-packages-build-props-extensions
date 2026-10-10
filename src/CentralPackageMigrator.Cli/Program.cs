using System.CommandLine;
using CentralPackageMigrator.Cli.Commands;
using CentralPackageMigrator.Cli.Output;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Infrastructure;
using CentralPackageMigrator.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CentralPackageMigrator.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error);

    /// <summary>
    /// Runs the CLI with explicit output writers. Command results go to <paramref name="output"/>
    /// (JSON in --json mode); errors go to <paramref name="error"/>, or to
    /// <paramref name="output"/> as JSON in --json mode. Logs always go to the process's stderr.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        Action<IServiceCollection>? configureServices = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var options = new GlobalOptions();
        var context = new CliContext(output, error, options)
        {
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        var rootCommand = new RootCommand("cpm-migrate - Migrate .NET solutions to Central Package Management");
        options.AddTo(rootCommand);
        rootCommand.Subcommands.Add(AnalyzeCommand.Create(context));
        rootCommand.Subcommands.Add(PlanCommand.Create(context));
        rootCommand.Subcommands.Add(ApplyCommand.Create(context));
        rootCommand.Subcommands.Add(CheckCommand.Create(context));
        rootCommand.Subcommands.Add(ResolveCommand.Create(context));

        // Report this tool's version, not the entry assembly's (which differs when hosted in-process).
        foreach (var versionOption in rootCommand.Options.OfType<VersionOption>())
        {
            versionOption.Action = new ToolVersionAction();
        }

        var parseResult = rootCommand.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            return ReportParseErrors(parseResult, context);
        }

        bool verbose = parseResult.GetValue(options.Verbose);
        await using var services = BuildServices(verbose, configureServices);
        context.Services = services;

        var invocation = new InvocationConfiguration { Output = output, Error = error };
        return await parseResult.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    private static ServiceProvider BuildServices(bool verbose, Action<IServiceCollection>? configureServices)
    {
        var services = new ServiceCollection();

        // Hosts and tests may pre-register IFileSystem / IProcessRunner / TimeProvider substitutes.
        configureServices?.Invoke(services);
        services.AddCentralPackageMigrator();
        services.AddLogging(builder =>
        {
            // All log levels go to stderr so stdout carries only command output (JSON in --json mode).
            builder.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
        });

        return services.BuildServiceProvider();
    }

    private static int ReportParseErrors(ParseResult parseResult, CliContext context)
    {
        string message = string.Join(Environment.NewLine, parseResult.Errors.Select(e => e.Message));

        if (parseResult.GetValue(context.Options.Json))
        {
            var renderer = new JsonRenderer(new PlanSerializer(NullLogger<PlanSerializer>.Instance), context.Output);
            renderer.RenderError(new MigrationError { Code = ErrorCodes.InvalidArguments, Message = message });
        }
        else
        {
            context.Error.WriteLine(message);
            context.Error.WriteLine("Run 'cpm-migrate --help' for usage.");
        }

        return ExitCodes.InvalidArguments;
    }
}
