using System.Xml;
using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Editing;
using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Core.Security;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class FileEditEngine : IFileEditEngine
{
    private const string CentralFileName = "Directory.Packages.props";

    private static readonly HashSet<string> EditableProjectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj"
    };

    private static readonly HashSet<string> EditableImportFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets"
    };

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FileEditEngine> _logger;

    public FileEditEngine(IFileSystem fileSystem, ILogger<FileEditEngine> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public IReadOnlyList<string> Apply(MigrationPlan plan, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.HasUnresolvedConflicts)
        {
            throw new MigrationException(
                ErrorCodes.UnresolvedConflicts,
                "Cannot apply plan with unresolved conflicts. Resolve all conflicts first.");
        }

        string root = _fileSystem.GetFullPath(plan.SolutionRoot);

        // Compute and verify every file before writing any of them.
        var pending = new List<PendingWrite>();

        foreach (var create in plan.Creates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = ResolvePath(root, create.RelativePath, isCreate: true);

            if (_fileSystem.FileExists(path))
            {
                throw new MigrationException(
                    ErrorCodes.StalePlan,
                    $"'{create.RelativePath}' already exists; the plan is out of date. Regenerate it.",
                    create.RelativePath);
            }

            EnsureWellFormed(create.Content, create.RelativePath);
            pending.Add(new PendingWrite(create.RelativePath, path, create.Content, OriginalContent: null));
        }

        foreach (var edit in plan.Edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = ResolvePath(root, edit.RelativePath, isCreate: false);
            ValidateOperations(edit);

            if (!_fileSystem.FileExists(path))
            {
                throw new MigrationException(
                    ErrorCodes.StalePlan,
                    $"File to edit no longer exists: '{edit.RelativePath}'. Regenerate the plan.",
                    edit.RelativePath);
            }

            string original = _fileSystem.ReadAllText(path);
            if (edit.OriginalSha256 is not null &&
                !string.Equals(TextFormat.Sha256(original), edit.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new MigrationException(
                    ErrorCodes.StalePlan,
                    $"'{edit.RelativePath}' changed after the plan was created. Regenerate the plan.",
                    edit.RelativePath);
            }

            string updated = XmlFileEditor.Apply(original, edit.Operations, edit.RelativePath);
            pending.Add(new PendingWrite(edit.RelativePath, path, updated, original));
        }

        foreach (var write in pending)
        {
            _logger.LogInformation(
                "{Action} {Path}",
                dryRun ? "[DRY-RUN] Would write" : "Writing",
                write.RelativePath);
        }

        if (!dryRun)
        {
            WriteAll(pending, cancellationToken);
        }

        return pending.Select(p => p.RelativePath).ToList().AsReadOnly();
    }

    private void WriteAll(List<PendingWrite> pending, CancellationToken cancellationToken)
    {
        var written = new List<PendingWrite>();
        try
        {
            foreach (var write in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _fileSystem.WriteAllText(write.AbsolutePath, write.Content);
                written.Add(write);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            RollBack(written);

            if (ex is OperationCanceledException)
            {
                throw;
            }

            var failed = pending[written.Count];
            throw new MigrationException(
                ErrorCodes.FileWriteFailed,
                $"Failed to write '{failed.RelativePath}'; {written.Count} already-written file(s) were restored.",
                failed.RelativePath,
                ex);
        }
    }

    private void RollBack(List<PendingWrite> written)
    {
        foreach (var write in Enumerable.Reverse(written))
        {
            try
            {
                if (write.OriginalContent is null)
                {
                    _fileSystem.DeleteFile(write.AbsolutePath);
                }
                else
                {
                    _fileSystem.WriteAllText(write.AbsolutePath, write.OriginalContent);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Failed to roll back {Path}", write.RelativePath);
            }
        }
    }

    /// <summary>
    /// Resolves a plan path to a full path, rejecting anything that is not a plain relative path
    /// inside the root or is not a file type the migration is allowed to touch.
    /// </summary>
    private string ResolvePath(string root, string relativePath, bool isCreate)
    {
        string? problem = PathSafety.ValidateRelativePath(relativePath);
        if (problem is not null)
        {
            throw new MigrationException(ErrorCodes.InvalidPath, problem, relativePath);
        }

        string fullPath = _fileSystem.GetFullPath(_fileSystem.Combine(root, relativePath));
        if (!PathSafety.IsUnderRoot(fullPath, root))
        {
            throw new MigrationException(
                ErrorCodes.InvalidPath,
                $"Path traversal detected: '{relativePath}' resolves outside solution root",
                relativePath);
        }

        string fileName = _fileSystem.GetFileName(fullPath);
        bool allowed = isCreate
            ? string.Equals(fileName, CentralFileName, StringComparison.OrdinalIgnoreCase)
            : string.Equals(fileName, CentralFileName, StringComparison.OrdinalIgnoreCase) ||
              EditableImportFiles.Contains(fileName) ||
              EditableProjectExtensions.Contains(Path.GetExtension(fileName));

        if (!allowed)
        {
            throw new MigrationException(
                ErrorCodes.InvalidPath,
                $"The migration may not {(isCreate ? "create" : "edit")} '{relativePath}'.",
                relativePath);
        }

        return fullPath;
    }

    private static void ValidateOperations(FileEdit edit)
    {
        bool isCentralFile = string.Equals(
            Path.GetFileName(edit.RelativePath), CentralFileName, StringComparison.OrdinalIgnoreCase);

        foreach (var op in edit.Operations)
        {
            bool allowed = isCentralFile
                ? op.Type is EditOperationType.AddPackageVersion
                    or EditOperationType.UpdatePackageVersion
                    or EditOperationType.EnableCentralPackageManagement
                : op.Type is EditOperationType.RemoveVersion;

            if (!allowed)
            {
                throw new MigrationException(
                    ErrorCodes.InvalidPlanFile,
                    $"Operation '{op.Type}' is not allowed on '{edit.RelativePath}'.",
                    edit.RelativePath);
            }
        }
    }

    private static void EnsureWellFormed(string content, string relativePath)
    {
        try
        {
            _ = XmlSource.Parse(content);
        }
        catch (XmlException ex)
        {
            throw new MigrationException(
                ErrorCodes.InvalidPlanFile,
                $"Content for '{relativePath}' is not valid XML: {ex.Message}",
                relativePath,
                ex);
        }
    }

    private sealed record PendingWrite(string RelativePath, string AbsolutePath, string Content, string? OriginalContent);
}
