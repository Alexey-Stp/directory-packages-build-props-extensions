namespace CentralPackageMigrator.Core.Abstractions;

/// <summary>
/// Abstraction over file system operations.
/// Enables testing without touching the real file system.
/// </summary>
public interface IFileSystem
{
    public bool FileExists(string path);
    public bool DirectoryExists(string path);
    public string ReadAllText(string path);

    /// <summary>
    /// Writes text as UTF-8. When overwriting, implementations keep the existing file's
    /// byte-order mark (present or absent).
    /// </summary>
    public void WriteAllText(string path, string content);

    /// <summary>Deletes a file. Used only to roll back files created earlier in the same apply.</summary>
    public void DeleteFile(string path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption);
    public IEnumerable<string> EnumerateDirectories(string directory);
    public string GetFullPath(string path);
    public string GetDirectoryName(string path);
    public string GetFileName(string path);
    public string GetFileNameWithoutExtension(string path);
    public string Combine(params string[] paths);
    public string GetRelativePath(string relativeTo, string path);

    /// <summary>
    /// Gets the parent directory path. Returns null for root directories.
    /// </summary>
    public string? GetParentDirectory(string path);
}
