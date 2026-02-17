using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using CentralPackageMigrator.Cli.Commands;
using CentralPackageMigrator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand(
            "cpm-migrate - Migrate .NET solutions to Central Package Management");

        rootCommand.AddCommand(AnalyzeCommand.Create());
        rootCommand.AddCommand(PlanCommand.Create());
        rootCommand.AddCommand(ApplyCommand.Create());
        rootCommand.AddCommand(CheckCommand.Create());
        rootCommand.AddCommand(ResolveCommand.Create());

        // Build DI container
        var services = new ServiceCollection();
        services.AddCentralPackageMigrator();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Build the parser with middleware that injects the IServiceProvider
        // into the invocation context's binding context.
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .AddMiddleware(async (context, next) =>
            {
                context.BindingContext.AddService<IServiceProvider>(_ => serviceProvider);
                await next(context);
            })
            .Build();

        var result = await parser.InvokeAsync(args);

        // Dispose the service provider on exit
        await serviceProvider.DisposeAsync();

        return result;
    }
}
