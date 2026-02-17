using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Cli.Output;

/// <summary>
/// Renders human-readable, colorized output for CLI commands.
/// </summary>
public sealed class ConsoleRenderer
{
    /// <summary>
    /// Renders the analysis results: projects found, packages, and conflicts.
    /// </summary>
    public void RenderAnalysis(SolutionInfo solution, IReadOnlyList<ConflictGroup> conflicts)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(conflicts);

        WriteHeader("Solution Analysis");

        WriteLabel("Root path:       ");
        Console.WriteLine(solution.RootPath);

        WriteLabel("Detection method: ");
        Console.WriteLine(solution.DetectionMethod.ToString());

        if (solution.SolutionFilePath is not null)
        {
            WriteLabel("Solution file:   ");
            Console.WriteLine(solution.SolutionFilePath);
        }

        WriteLabel("Projects found:  ");
        Console.WriteLine(solution.Projects.Count.ToString());

        var sdkCount = solution.Projects.Count(p => p.IsSdkStyle);
        var legacyCount = solution.Projects.Count - sdkCount;

        WriteLabel("  SDK-style:     ");
        Console.WriteLine(sdkCount.ToString());

        if (legacyCount > 0)
        {
            WriteLabel("  Legacy:        ");
            WriteWarning(legacyCount.ToString());
            Console.WriteLine();
        }

        var totalRefs = solution.Projects.Sum(p => p.PackageReferences.Count);
        var uniquePackages = solution.Projects
            .SelectMany(p => p.PackageReferences)
            .Select(r => r.Package.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        WriteLabel("Package references: ");
        Console.WriteLine($"{totalRefs} total, {uniquePackages} unique");

        if (solution.HasExistingDirectoryPackagesProps)
        {
            Console.WriteLine();
            WriteWarning("WARNING: Directory.Packages.props already exists.");
            Console.WriteLine();
        }

        Console.WriteLine();

        // List projects
        WriteSubHeader("Projects");
        foreach (var project in solution.Projects)
        {
            var styleTag = project.IsSdkStyle ? "SDK" : "Legacy";
            var refCount = project.PackageReferences.Count;
            Console.Write("  ");
            Console.Write(project.RelativePath);
            WriteLabel($" [{styleTag}, {refCount} refs]");
            Console.WriteLine();
        }

        Console.WriteLine();

        // Show conflicts
        if (conflicts.Count > 0)
        {
            RenderConflicts(conflicts);
        }
        else
        {
            WriteSuccess("No version conflicts detected.");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Renders the migration plan summary.
    /// </summary>
    public void RenderPlan(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        WriteHeader("Migration Plan");

        WriteLabel("Schema version: ");
        Console.WriteLine(plan.SchemaVersion);

        WriteLabel("Solution root:  ");
        Console.WriteLine(plan.SolutionRoot);

        WriteLabel("Timestamp:      ");
        Console.WriteLine(plan.Timestamp.ToString("u"));

        Console.WriteLine();

        // Stats
        WriteSubHeader("Statistics");
        var stats = plan.Stats;
        Console.WriteLine($"  Total projects:      {stats.TotalProjects}");
        Console.WriteLine($"  SDK-style projects:  {stats.SdkStyleProjects}");
        Console.WriteLine($"  Legacy projects:     {stats.LegacyProjects}");
        Console.WriteLine($"  Package references:  {stats.TotalPackageReferences}");
        Console.WriteLine($"  Unique packages:     {stats.UniquePackages}");
        Console.WriteLine($"  Conflicts:           {stats.ConflictCount}");
        Console.WriteLine($"  Files to create:     {stats.FilesCreated}");
        Console.WriteLine($"  Files to modify:     {stats.FilesModified}");
        Console.WriteLine($"  Versions to remove:  {stats.VersionsRemoved}");
        Console.WriteLine();

        if (plan.IsNoOp)
        {
            WriteSuccess("No changes needed - solution appears to already use Central Package Management.");
            Console.WriteLine();
            return;
        }

        // Files to create
        if (plan.Creates.Count > 0)
        {
            WriteSubHeader("Files to Create");
            foreach (var create in plan.Creates)
            {
                WriteSuccess("  + ");
                Console.WriteLine($"{create.RelativePath} - {create.Description}");
            }
            Console.WriteLine();
        }

        // Files to edit
        if (plan.Edits.Count > 0)
        {
            WriteSubHeader("Files to Edit");
            foreach (var edit in plan.Edits)
            {
                WriteLabel("  ~ ");
                Console.Write(edit.RelativePath);
                Console.WriteLine($" ({edit.Operations.Count} operations)");
            }
            Console.WriteLine();
        }

        // Conflicts
        if (plan.Conflicts.Count > 0)
        {
            RenderConflicts(plan.Conflicts);
        }

        // Warnings
        if (plan.Warnings.Count > 0)
        {
            RenderWarnings(plan.Warnings);
        }

        if (plan.HasUnresolvedConflicts)
        {
            Console.WriteLine();
            WriteError("There are unresolved conflicts. Use --resolve Package=Version to resolve them.");
            Console.WriteLine();
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
            WriteWarning("No files were modified (dry run).");
            Console.WriteLine();
        }
        else
        {
            WriteHeader("Migration Applied");
        }

        if (writtenFiles.Count == 0)
        {
            Console.WriteLine("  No files were written.");
            return;
        }

        Console.WriteLine($"  {writtenFiles.Count} file(s) {(dryRun ? "would be" : "were")} written:");
        Console.WriteLine();

        foreach (var file in writtenFiles)
        {
            WriteSuccess("  + ");
            Console.WriteLine(file);
        }

        Console.WriteLine();

        if (!dryRun)
        {
            WriteSuccess("Migration complete.");
            Console.WriteLine(" Run 'cpm-migrate check' to verify the solution builds.");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Renders a list of migration warnings.
    /// </summary>
    public void RenderWarnings(IReadOnlyList<MigrationWarning> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        if (warnings.Count == 0)
            return;

        WriteSubHeader("Warnings");
        foreach (var warning in warnings)
        {
            WriteWarning($"  [{warning.Code}] ");
            Console.Write(warning.Message);

            if (warning.ProjectPath is not null)
            {
                WriteLabel($" ({warning.ProjectPath})");
            }

            if (warning.PackageId is not null)
            {
                WriteLabel($" [{warning.PackageId}]");
            }

            Console.WriteLine();
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Renders version conflicts in a readable table-like format.
    /// </summary>
    public void RenderConflicts(IReadOnlyList<ConflictGroup> conflicts)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        if (conflicts.Count == 0)
            return;

        WriteSubHeader($"Version Conflicts ({conflicts.Count})");

        foreach (var conflict in conflicts)
        {
            var resolved = conflict.ResolvedVersion is not null;
            var marker = resolved ? "[Resolved]" : "[Unresolved]";

            if (resolved)
            {
                WriteSuccess($"  {marker} ");
            }
            else
            {
                WriteError($"  {marker} ");
            }

            Console.WriteLine(conflict.Package.Id);

            foreach (var entry in conflict.Entries)
            {
                var isChosen = resolved && entry.Version == conflict.ResolvedVersion;
                Console.Write("    ");

                if (isChosen)
                {
                    WriteSuccess($"-> {entry.Version}");
                }
                else
                {
                    Console.Write($"   {entry.Version}");
                }

                WriteLabel($" (used by {entry.Projects.Count} project(s))");
                Console.WriteLine();

                foreach (var project in entry.Projects)
                {
                    Console.WriteLine($"      - {project}");
                }
            }

            Console.WriteLine();
        }
    }

    /// <summary>
    /// Renders an error message in red.
    /// </summary>
    public void RenderError(string message)
    {
        WriteError($"ERROR: {message}");
        Console.WriteLine();
    }

    private static void WriteHeader(string text)
    {
        Console.WriteLine();
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== {text} ===");
        Console.ForegroundColor = saved;
        Console.WriteLine();
    }

    private static void WriteSubHeader(string text)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"--- {text} ---");
        Console.ForegroundColor = saved;
    }

    private static void WriteLabel(string text)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(text);
        Console.ForegroundColor = saved;
    }

    private static void WriteSuccess(string text)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(text);
        Console.ForegroundColor = saved;
    }

    private static void WriteWarning(string text)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write(text);
        Console.ForegroundColor = saved;
    }

    private static void WriteError(string text)
    {
        var saved = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write(text);
        Console.ForegroundColor = saved;
    }
}
