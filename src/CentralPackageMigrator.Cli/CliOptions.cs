using System.CommandLine;

namespace CentralPackageMigrator.Cli;

/// <summary>
/// Global options, created once per invocation. They are recursive, so they are
/// accepted before or after the command name.
/// </summary>
internal sealed class GlobalOptions
{
    public Option<string?> Root { get; } = new("--root")
    {
        Description = "Solution root path. Used as-is; without it the root is detected from the current directory",
        Recursive = true
    };

    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Write machine-readable JSON to stdout (logs go to stderr)",
        Recursive = true
    };

    public Option<bool> Verbose { get; } = new("--verbose")
    {
        Description = "Show verbose output and debug logs",
        Recursive = true
    };

    public void AddTo(RootCommand command)
    {
        command.Options.Add(Root);
        command.Options.Add(Json);
        command.Options.Add(Verbose);
    }
}

/// <summary>
/// Factories for options used by more than one command.
/// </summary>
internal static class CliOptions
{
    public static Option<string[]> Resolve(bool required = false) => new("--resolve")
    {
        Description = "Conflict resolution in the form Package=Version (repeatable)",
        AllowMultipleArgumentsPerToken = true,
        Required = required
    };

    public static Option<string?> Output(string description) => new("--output")
    {
        Description = description
    };

    public static Option<bool> DryRun(string description) => new("--dry-run")
    {
        Description = description
    };
}
