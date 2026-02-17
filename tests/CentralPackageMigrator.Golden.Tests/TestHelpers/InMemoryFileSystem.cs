using CentralPackageMigrator.Core.Abstractions;

namespace CentralPackageMigrator.Golden.Tests.TestHelpers;

public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string content)
    {
        string normalized = NormalizePath(path);
        _files[normalized] = content;
        RegisterAncestors(normalized);
    }

    public void AddDirectory(string path)
    {
        string normalized = NormalizePath(path);
        _directories.Add(normalized);
        RegisterAncestors(normalized);
    }

    public string GetFileContent(string path) => ReadAllText(path);

    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));

    public bool DirectoryExists(string path)
    {
        string normalized = NormalizePath(path);
        if (_directories.Contains(normalized))
        {
            return true;
        }
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
        _files[normalized] = content;
        RegisterAncestors(normalized);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(searchPattern);
        string prefix = NormalizePath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string extension = searchPattern.StartsWith("*.") ? searchPattern[1..] : searchPattern;

        foreach (string filePath in _files.Keys)
        {
            if (!filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string relativePart = filePath[prefix.Length..];
            if (searchOption == SearchOption.TopDirectoryOnly && relativePart.Contains(Path.DirectorySeparatorChar))
            {
                continue;
            }
            if (searchPattern is "*" or "*.*" || filePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return filePath;
            }
        }
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        string prefix = NormalizePath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in _directories)
        {
            if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && dir.Length > prefix.Length)
            {
                string rel = dir[prefix.Length..];
                int sep = rel.IndexOf(Path.DirectorySeparatorChar);
                childDirs.Add(prefix + (sep >= 0 ? rel[..sep] : rel));
            }
        }
        foreach (string f in _files.Keys)
        {
            if (f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string rel = f[prefix.Length..];
                int sep = rel.IndexOf(Path.DirectorySeparatorChar);
                if (sep >= 0)
                {
                    childDirs.Add(prefix + rel[..sep]);
                }
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
