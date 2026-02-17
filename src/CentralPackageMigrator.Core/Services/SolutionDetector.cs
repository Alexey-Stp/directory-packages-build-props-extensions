using CentralPackageMigrator.Core.Abstractions;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Core.Services;

public sealed class SolutionDetector : ISolutionDetector
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SolutionDetector> _logger;

    public SolutionDetector(IFileSystem fileSystem, ILogger<SolutionDetector> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public SolutionInfo Detect(string startPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);

        string fullPath = _fileSystem.GetFullPath(startPath);

        if (!_fileSystem.DirectoryExists(fullPath))
        {
            throw new DirectoryNotFoundException($"Start path does not exist: {fullPath}");
        }

        _logger.LogDebug("Detecting solution root from: {StartPath}", fullPath);

        // Strategy 1: Look for .sln file walking up
        var (slnPath, slnRoot) = FindSolutionFile(fullPath);
        if (slnPath is not null && slnRoot is not null)
        {
            _logger.LogInformation("Found solution file: {SlnPath}", slnPath);
            return BuildSolutionInfo(slnRoot, slnPath, RootDetectionMethod.SolutionFile);
        }

        // Strategy 2: Look for .git directory walking up
        string? gitRoot = FindGitRoot(fullPath);
        if (gitRoot is not null)
        {
            _logger.LogInformation("Found git root: {GitRoot}", gitRoot);
            return BuildSolutionInfo(gitRoot, null, RootDetectionMethod.GitRoot);
        }

        // Strategy 3: Use current directory
        _logger.LogInformation("Using start path as solution root: {Path}", fullPath);
        return BuildSolutionInfo(fullPath, null, RootDetectionMethod.CurrentDirectory);
    }

    private (string? SlnPath, string? RootDir) FindSolutionFile(string startDir)
    {
        string? current = startDir;
        int depth = 0;
        const int maxDepth = 20;

        while (current is not null && depth < maxDepth)
        {
            var slnFiles = _fileSystem.EnumerateFiles(current, "*.sln", SearchOption.TopDirectoryOnly).ToList();

            if (slnFiles.Count > 0)
            {
                string chosen = slnFiles.Count == 1
                    ? slnFiles[0]
                    : slnFiles.OrderBy(f => _fileSystem.GetFileName(f)).First();

                if (slnFiles.Count > 1)
                {
                    _logger.LogWarning(
                        "Multiple .sln files found in {Dir}. Using: {Chosen}",
                        current, _fileSystem.GetFileName(chosen));
                }

                return (chosen, current);
            }

            current = _fileSystem.GetParentDirectory(current);
            depth++;
        }

        return (null, null);
    }

    private string? FindGitRoot(string startDir)
    {
        string? current = startDir;
        int depth = 0;
        const int maxDepth = 20;

        while (current is not null && depth < maxDepth)
        {
            string gitDir = _fileSystem.Combine(current, ".git");
            if (_fileSystem.DirectoryExists(gitDir) || _fileSystem.FileExists(gitDir))
            {
                return current;
            }

            current = _fileSystem.GetParentDirectory(current);
            depth++;
        }

        return null;
    }

    private SolutionInfo BuildSolutionInfo(string rootPath, string? slnPath, RootDetectionMethod method)
    {
        bool hasExistingDpp = _fileSystem.FileExists(
            _fileSystem.Combine(rootPath, "Directory.Packages.props"));
        bool hasExistingDbp = _fileSystem.FileExists(
            _fileSystem.Combine(rootPath, "Directory.Build.props"));

        return new SolutionInfo
        {
            RootPath = rootPath,
            SolutionFilePath = slnPath,
            DetectionMethod = method,
            HasExistingDirectoryPackagesProps = hasExistingDpp,
            HasExistingDirectoryBuildProps = hasExistingDbp
        };
    }
}
