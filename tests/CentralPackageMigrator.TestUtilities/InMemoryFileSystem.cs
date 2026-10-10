using CentralPackageMigrator.Core.Abstractions;

namespace CentralPackageMigrator.TestUtilities;

/// <summary>
/// In-memory implementation of <see cref="IFileSystem"/> for unit testing.
/// Backed by a dictionary for file contents and a hash set for directories.
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes to these full paths throw <see cref="IOException"/>, to exercise failure handling.</summary>
    public HashSet<string> FailingWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds a file with the given content. Also registers all ancestor directories.
    /// </summary>
    public void AddFile(string path, string content)
    {
        string normalized = NormalizePath(path);
        _files[normalized] = content;
        RegisterAncestors(normalized);
    }

    /// <summary>
    /// Explicitly registers a directory and its ancestors.
    /// </summary>
    public void AddDirectory(string path)
    {
        string normalized = NormalizePath(path);
        _directories.Add(normalized);
        RegisterAncestors(normalized);
    }

    /// <summary>
    /// Gets the content of a file previously added via <see cref="AddFile"/> or <see cref="WriteAllText"/>.
    /// </summary>
    public string GetFileContent(string path) => ReadAllText(path);

    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));

    public bool DirectoryExists(string path)
    {
        string normalized = NormalizePath(path);

        if (_directories.Contains(normalized))
        {
            return true;
        }

        // Infer directory existence from file paths
        string prefix = normalized.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
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
        if (FailingWrites.Contains(normalized))
        {
            throw new IOException($"Simulated write failure: {path}");
        }

        _files[normalized] = content;
        RegisterAncestors(normalized);
    }

    public void DeleteFile(string path) => _files.Remove(NormalizePath(path));

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);
        string prefix = NormalizePath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        // Supports "*", "*.*" and "*.ext" patterns
        string extension = searchPattern.StartsWith("*.", StringComparison.Ordinal)
            ? searchPattern[1..]
            : searchPattern;

        foreach (string filePath in _files.Keys.ToList())
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

            if (searchPattern is "*" or "*.*" ||
                filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return filePath;
            }
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        string prefix = NormalizePath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in _directories.Concat(_files.Keys.Select(Path.GetDirectoryName).OfType<string>()))
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length)
            {
                string relativePart = path[prefix.Length..];
                int sepIndex = relativePart.IndexOf(Path.DirectorySeparatorChar);
                string childName = sepIndex >= 0 ? relativePart[..sepIndex] : relativePart;
                childDirs.Add(prefix + childName);
            }
        }

        return childDirs;
    }

    public string GetFullPath(string path) => Path.GetFullPath(path);

    public string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);

    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? path;

    public string GetFileName(string path) => Path.GetFileName(path);

    public string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(path);

    public string Combine(params string[] paths) => Path.Combine(paths);

    public string? GetParentDirectory(string path) => Path.GetDirectoryName(path);

    private void RegisterAncestors(string normalized)
    {
        string? dir = Path.GetDirectoryName(normalized);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);
}
