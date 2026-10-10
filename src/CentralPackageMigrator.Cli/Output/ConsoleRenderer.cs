using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Cli.Output;

/// <summary>
/// Renders human-readable output. Results go to the output writer, errors to the error
/// writer. Colors are used only when writing to an interactive console.
/// </summary>
public sealed class ConsoleRenderer
{
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly bool _outColor;
    private readonly bool _errColor;

    public ConsoleRenderer(TextWriter output, TextWriter error)
    {
        _out = output;
        _err = error;
        _outColor = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        _errColor = ReferenceEquals(error, Console.Error) && !Console.IsErrorRedirected;
    }

    /// <summary>
    /// Renders the analysis results: projects found, packages, conflicts and warnings.
    /// </summary>
    public void RenderAnalysis(SolutionInfo solution, MigrationPlan plan, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(plan);

        WriteHeader("Solution Analysis");

        WriteLabel("Root path:        ");
        _out.WriteLine(solution.RootPath);

        WriteLabel("Detection method: ");
        _out.WriteLine(solution.DetectionMethod.ToString());

        if (solution.SolutionFilePath is not null)
        {
            WriteLabel("Solution file:    ");
            _out.WriteLine(solution.SolutionFilePath);
        }

        var stats = plan.Stats;
        WriteLabel("Projects found:   ");
        _out.WriteLine(stats.TotalProjects.ToString());

        WriteLabel("  SDK-style:      ");
        _out.WriteLine(stats.SdkStyleProjects.ToString());

        if (stats.LegacyProjects > 0)
        {
            WriteLabel("  Legacy:         ");
            Write(_out, stats.LegacyProjects.ToString(), ConsoleColor.Yellow);
            _out.WriteLine();
        }

        WriteLabel("Package references: ");
        _out.WriteLine($"{stats.TotalPackageReferences} total, {stats.UniquePackages} to centralize");

        if (solution.HasExistingDirectoryPackagesProps)
        {
            WriteLabel("Directory.Packages.props: ");
            _out.WriteLine("exists (new versions will be merged)");
        }

        _out.WriteLine();

        WriteSubHeader("Projects");
        foreach (var project in solution.Projects)
        {
            string tag = project switch
            {
                { IsOutsideRoot: true } => "outside root",
                { ParseError: not null } => "malformed",
                { Kind: ProjectKind.ImportFile } => "import",
                { IsSdkStyle: true } => "SDK",
                _ => "legacy"
            };

            _out.Write("  ");
            _out.Write(project.RelativePath);
            WriteLabel($" [{tag}, {project.PackageReferences.Count} refs]");
            _out.WriteLine();

            if (verbose)
            {
                foreach (var reference in project.PackageReferences)
                {
                    string version = reference.Version ?? "(no version)";
                    string condition = reference.Condition is not null ? $" [Condition: {reference.Condition}]" : string.Empty;
                    _out.WriteLine($"      {reference.Package.Id} {version}{condition}");
                }
            }
        }

        _out.WriteLine();

        if (plan.Conflicts.Count > 0)
        {
            RenderConflicts(plan.Conflicts);
        }
        else
        {
            Write(_out, "No version conflicts detected.", ConsoleColor.Green);
            _out.WriteLine();
            _out.WriteLine();
        }

        RenderWarnings(plan.Warnings);
    }

    /// <summary>
    /// Renders the migration plan summary.
    /// </summary>
    public void RenderPlan(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        WriteHeader("Migration Plan");

        WriteLabel("Schema version: ");
        _out.WriteLine(plan.SchemaVersion);

        WriteLabel("Solution root:  ");
        _out.WriteLine(plan.SolutionRoot);

        _out.WriteLine();

        WriteSubHeader("Statistics");
        var stats = plan.Stats;
        _out.WriteLine($"  Total projects:      {stats.TotalProjects}");
        _out.WriteLine($"  SDK-style projects:  {stats.SdkStyleProjects}");
        _out.WriteLine($"  Legacy projects:     {stats.LegacyProjects}");
        _out.WriteLine($"  Package references:  {stats.TotalPackageReferences}");
        _out.WriteLine($"  Unique packages:     {stats.UniquePackages}");
        _out.WriteLine($"  Conflicts:           {stats.ConflictCount}");
        _out.WriteLine($"  Files to create:     {stats.FilesCreated}");
        _out.WriteLine($"  Files to modify:     {stats.FilesModified}");
        _out.WriteLine($"  Versions to remove:  {stats.VersionsRemoved}");
        _out.WriteLine();

        if (plan.IsNoOp && plan.Conflicts.Count == 0)
        {
            Write(_out, "No changes needed - solution appears to already use Central Package Management.", ConsoleColor.Green);
            _out.WriteLine();
            RenderWarnings(plan.Warnings);
            return;
        }

        if (plan.Creates.Count > 0)
        {
            WriteSubHeader("Files to Create");
            foreach (var create in plan.Creates)
            {
                Write(_out, "  + ", ConsoleColor.Green);
                _out.WriteLine($"{create.RelativePath} - {create.Description}");
            }

            _out.WriteLine();
        }

        if (plan.Edits.Count > 0)
        {
            WriteSubHeader("Files to Edit");
            foreach (var edit in plan.Edits)
            {
                WriteLabel("  ~ ");
                _out.Write(edit.RelativePath);
                _out.WriteLine($" ({edit.Operations.Count} operation(s))");
            }

            _out.WriteLine();
        }

        RenderConflicts(plan.Conflicts);
        RenderWarnings(plan.Warnings);

        if (plan.HasUnresolvedConflicts)
        {
            Write(_out, "There are unresolved conflicts. Use --resolve Package=Version to resolve them.", ConsoleColor.Red);
            _out.WriteLine();
        }
    }

    /// <summary>
    /// Renders the result of applying a migration plan.
    /// </summary>
    public void RenderApplyResult(IReadOnlyList<string> writtenFiles, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(writtenFiles);

        if (dryRun)
        {
            WriteHeader("Dry Run Results");
            Write(_out, "No files were modified (dry run). All checks passed.", ConsoleColor.Yellow);
            _out.WriteLine();
        }
        else
        {
            WriteHeader("Migration Applied");
        }

        if (writtenFiles.Count == 0)
        {
            _out.WriteLine("  No files were written.");
            return;
        }

        _out.WriteLine($"  {writtenFiles.Count} file(s) {(dryRun ? "would be" : "were")} written:");
        _out.WriteLine();

        foreach (string file in writtenFiles)
        {
            Write(_out, "  + ", ConsoleColor.Green);
            _out.WriteLine(file);
        }

        _out.WriteLine();

        if (!dryRun)
        {
            Write(_out, "Migration complete.", ConsoleColor.Green);
            _out.WriteLine(" Run 'cpm-migrate check' to verify the solution builds.");
            _out.WriteLine();
        }
    }

    /// <summary>
    /// Renders a list of migration warnings.
    /// </summary>
    public void RenderWarnings(IReadOnlyList<MigrationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        if (warnings.Count == 0)
        {
            return;
        }

        WriteSubHeader("Warnings");
        foreach (var warning in warnings)
        {
            bool isWarning = warning.Severity == WarningSeverity.Warning;
            Write(_out, $"  [{warning.Code}] ", isWarning ? ConsoleColor.Yellow : ConsoleColor.DarkGray);
            _out.Write(warning.Message);
            _out.WriteLine();
        }

        _out.WriteLine();
    }

    /// <summary>
    /// Renders version conflicts in a readable table-like format.
    /// </summary>
    public void RenderConflicts(IReadOnlyList<ConflictGroup> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        if (conflicts.Count == 0)
        {
            return;
        }

        WriteSubHeader($"Version Conflicts ({conflicts.Count})");

        foreach (var conflict in conflicts)
        {
            bool resolved = conflict.ResolvedVersion is not null;
            Write(_out, resolved ? "  [Resolved] " : "  [Unresolved] ", resolved ? ConsoleColor.Green : ConsoleColor.Red);
            _out.Write(conflict.Package.Id);

            if (!resolved && conflict.SuggestedVersion is not null)
            {
                WriteLabel($" (suggested: --resolve {conflict.Package.Id}={conflict.SuggestedVersion})");
            }

            _out.WriteLine();

            foreach (var entry in conflict.Entries)
            {
                bool isChosen = resolved && entry.Version == conflict.ResolvedVersion;
                _out.Write("    ");

                if (isChosen)
                {
                    Write(_out, $"-> {entry.Version}", ConsoleColor.Green);
                }
                else
                {
                    _out.Write($"   {entry.Version}");
                }

                WriteLabel($" (used by {entry.Projects.Count} file(s))");
                _out.WriteLine();

                foreach (string project in entry.Projects)
                {
                    _out.WriteLine($"      - {project}");
                }
            }

            if (resolved && !conflict.Entries.Any(e => e.Version == conflict.ResolvedVersion))
            {
                Write(_out, $"    -> {conflict.ResolvedVersion}", ConsoleColor.Green);
                _out.WriteLine();
            }

            _out.WriteLine();
        }
    }

    /// <summary>
    /// Renders an error message to the error writer.
    /// </summary>
    public void RenderError(string message)
    {
        Write(_err, $"ERROR: {message}", ConsoleColor.Red, _errColor);
        _err.WriteLine();
    }

    /// <summary>Renders additional error detail (e.g., an inner exception) to the error writer.</summary>
    public void RenderErrorDetail(string detail) => _err.WriteLine(detail);

    /// <summary>Writes a plain line to the output writer.</summary>
    public void WriteLine(string text = "") => _out.WriteLine(text);

    /// <summary>Writes a PASS/FAIL status line to the output writer.</summary>
    public void RenderStatus(bool success, string message)
    {
        Write(_out, success ? "  PASS" : "  FAIL", success ? ConsoleColor.Green : ConsoleColor.Red);
        _out.WriteLine($" {message}");
    }

    private void WriteHeader(string text)
    {
        _out.WriteLine();
        Write(_out, $"=== {text} ===", ConsoleColor.Cyan);
        _out.WriteLine();
        _out.WriteLine();
    }

    private void WriteSubHeader(string text)
    {
        Write(_out, $"--- {text} ---", ConsoleColor.Cyan);
        _out.WriteLine();
    }

    private void WriteLabel(string text) => Write(_out, text, ConsoleColor.DarkGray);

    private void Write(TextWriter writer, string text, ConsoleColor color) =>
        Write(writer, text, color, ReferenceEquals(writer, _out) ? _outColor : _errColor);

    private static void Write(TextWriter writer, string text, ConsoleColor color, bool useColor)
    {
        if (!useColor)
        {
            writer.Write(text);
            return;
        }

        var saved = Console.ForegroundColor;
        Console.ForegroundColor = color;
        writer.Write(text);
        Console.ForegroundColor = saved;
    }
}
