namespace CentralPackageMigrator.Core.Abstractions;

/// <summary>
/// Abstraction over file system operations.
/// Enables testing without touching the real file system.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string content);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption);
    IEnumerable<string> EnumerateDirectories(string directory);
    string GetFullPath(string path);
    string GetDirectoryName(string path);
    string GetFileName(string path);
    string GetFileNameWithoutExtension(string path);
    string Combine(params string[] paths);
    string GetRelativePath(string relativeTo, string path);

    /// <summary>
    /// Gets the parent directory path. Returns null for root directories.
    /// </summary>
    string? GetParentDirectory(string path);
}
