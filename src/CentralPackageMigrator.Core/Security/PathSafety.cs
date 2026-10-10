namespace CentralPackageMigrator.Core.Security;

/// <summary>
/// Path containment checks used to keep every read and write inside the solution root.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Comparison matching the platform's usual file system case sensitivity.
    /// Linux is case-sensitive, so a case-insensitive check there would accept a different directory.
    /// </summary>
    public static StringComparison PathComparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// True if <paramref name="fullPath"/> is strictly inside <paramref name="fullRoot"/>.
    /// Compares on a directory boundary, so <c>/repo-evil/x</c> is not inside <c>/repo</c>.
    /// Both arguments must already be normalized full paths.
    /// </summary>
    public static bool IsUnderRoot(string fullPath, string fullRoot)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(fullRoot);

        string root = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;

        return fullPath.Length > root.Length && fullPath.StartsWith(root, PathComparison);
    }

    /// <summary>True if two normalized full paths refer to the same location.</summary>
    public static bool PathsEqual(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return string.Equals(
            a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            PathComparison);
    }

    /// <summary>
    /// Returns an error message if <paramref name="relativePath"/> is not a plain relative path
    /// (rooted, drive-qualified, or containing <c>..</c> segments); otherwise null.
    /// </summary>
    public static string? ValidateRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "Path is empty.";
        }

        if (Path.IsPathRooted(relativePath) ||
            relativePath.StartsWith('/') ||
            relativePath.StartsWith('\\') ||
            relativePath.Contains(':', StringComparison.Ordinal))
        {
            return $"Path must be relative to the solution root: '{relativePath}'.";
        }

        bool hasParentSegment = relativePath
            .Split('/', '\\')
            .Any(segment => segment == "..");

        return hasParentSegment ? $"Path contains '..' segments: '{relativePath}'." : null;
    }
}
