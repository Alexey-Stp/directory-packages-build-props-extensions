using System.Text;
using CentralPackageMigrator.Core.Abstractions;

namespace CentralPackageMigrator.Infrastructure.FileSystem;

/// <summary>
/// Production implementation of <see cref="IFileSystem"/> that delegates
/// to <see cref="System.IO.File"/>, <see cref="System.IO.Directory"/>,
/// and <see cref="System.IO.Path"/>.
/// </summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    public string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllText(path);
    }

    public void WriteAllText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        // Ensure the parent directory exists before writing.
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Keep the existing file's byte-order mark so edits don't produce encoding-only diffs.
        bool writeBom = File.Exists(path) && StartsWithUtf8Bom(path);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: writeBom));
    }

    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(path);
    }

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, SearchOption searchOption)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        return Directory.EnumerateFiles(directory, searchPattern, searchOption);
    }

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.EnumerateDirectories(directory);
    }

    public string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    public string GetDirectoryName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetDirectoryName(path) ?? path;
    }

    public string GetFileName(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFileName(path);
    }

    public string GetFileNameWithoutExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFileNameWithoutExtension(path);
    }

    public string Combine(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Path.Combine(paths);
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeTo);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetRelativePath(relativeTo, path);
    }

    public string? GetParentDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DirectoryInfo? parent = Directory.GetParent(path);
        return parent?.FullName;
    }

    private static bool StartsWithUtf8Bom(string path)
    {
        Span<byte> buffer = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read == 3 && buffer.SequenceEqual(Utf8Bom);
    }
}
