using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

namespace CentralPackageMigrator.Cli;

/// <summary>
/// Prints the tool's informational version, which is derived from the release tag and equals the
/// NuGet package version.
/// </summary>
internal sealed class ToolVersionAction : SynchronousCommandLineAction
{
    public static string Version { get; } =
        typeof(ToolVersionAction).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ToolVersionAction).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public override int Invoke(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        parseResult.InvocationConfiguration.Output.WriteLine(Version);
        return ExitCodes.Success;
    }
}
