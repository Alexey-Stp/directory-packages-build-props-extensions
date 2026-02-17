using CentralPackageMigrator.Core.Abstractions;

namespace CentralPackageMigrator.Core.Tests.TestHelpers;

/// <summary>
/// In-memory implementation of <see cref="IFileSystem"/> for unit testing.
/// Backed by a dictionary for file contents and a hash set for directories.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a file with the given content. Also registers all ancestor directories.
    /// </summary>
    public void AddFile(string path, string content)
    {
        string normalized = NormalizePath(path);
        _files[normalized] = content;

        // Register all ancestor directories implied by this file path
        string? dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    /// <summary>
    /// Explicitly registers a directory.
    /// </summary>
    public void AddDirectory(string path)
    {
        string normalized = NormalizePath(path);
        _directories.Add(normalized);

        // Register ancestor directories
        string? parent = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(parent))
        {
            _directories.Add(parent);
            parent = Path.GetDirectoryName(parent);
        }
    }

    /// <summary>
    /// Gets the content of a file previously added via <see cref="AddFile"/> or <see cref="WriteAllText"/>.
    /// </summary>
    public string GetFileContent(string path)
    {
        string normalized = NormalizePath(path);
        if (_files.TryGetValue(normalized, out string? content))
        {
            return content;
        }
        throw new FileNotFoundException($"File not found: {path}", path);
    }

    public bool FileExists(string path)
    {
        string normalized = NormalizePath(path);
        return _files.ContainsKey(normalized);
    }

    public bool DirectoryExists(string path)
    {
        string normalized = NormalizePath(path);

        if (_directories.Contains(normalized))
        {
            return true;
        }

        // Infer directory existence from file paths
        string prefix = normalized.EndsWith(Path.DirectorySeparatorChar)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;

        return _files.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public string ReadAllText(string path)
    {
        string normalized = NormalizePath(path);
        if (_files.TryGetValue(normalized, out string? content))
        {
            return content;
        }
        throw new FileNotFoundException($"File not found: {path}", path);
    }

    public void WriteAllText(string path, string content)
    {
        string normalized = NormalizePath(path);
        _files[normalized] = content;

        // Register ancestor directories
        string? dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);
        string normalizedDir = NormalizePath(directory);
        string prefix = normalizedDir.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedDir
            : normalizedDir + Path.DirectorySeparatorChar;

        // Convert search pattern to a simple matcher
        // Supports *.ext patterns
        string extension = searchPattern.StartsWith("*.")
            ? searchPattern[1..] // e.g., ".csproj"
            : searchPattern;

        foreach (string filePath in _files.Keys)
        {
            if (!filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativePart = filePath[prefix.Length..];

            // For TopDirectoryOnly, skip files in subdirectories
            if (searchOption == SearchOption.TopDirectoryOnly &&
                relativePart.Contains(Path.DirectorySeparatorChar))
            {
                continue;
            }

            // Match the pattern
            if (searchPattern == "*" || searchPattern == "*.*")
            {
                yield return filePath;
            }
            else if (filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return filePath;
            }
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        string normalizedDir = NormalizePath(directory);
        string prefix = normalizedDir.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedDir
            : normalizedDir + Path.DirectorySeparatorChar;

        var childDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect from explicitly registered directories
        foreach (string dir in _directories)
        {
            if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && dir.Length > prefix.Length)
            {
                string relativePart = dir[prefix.Length..];
                int sepIndex = relativePart.IndexOf(Path.DirectorySeparatorChar);
                string childName = sepIndex >= 0 ? relativePart[..sepIndex] : relativePart;
                string childFullPath = prefix + childName;
                childDirs.Add(childFullPath);
            }
        }

        // Also collect from file paths
        foreach (string filePath in _files.Keys)
        {
            if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string relativePart = filePath[prefix.Length..];
                int sepIndex = relativePart.IndexOf(Path.DirectorySeparatorChar);
                if (sepIndex >= 0)
                {
                    string childName = relativePart[..sepIndex];
                    string childFullPath = prefix + childName;
                    childDirs.Add(childFullPath);
                }
            }
        }

        return childDirs;
    }

    public string GetFullPath(string path)
    {
        return Path.GetFullPath(path);
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        return Path.GetRelativePath(relativeTo, path);
    }

    public string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path) ?? path;
    }

    public string GetFileName(string path)
    {
        return Path.GetFileName(path);
    }

    public string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }

    public string Combine(params string[] paths)
    {
        return Path.Combine(paths);
    }

    public string? GetParentDirectory(string path)
    {
        return Path.GetDirectoryName(path);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }
}
