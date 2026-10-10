using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Determines the solution root directory.
/// </summary>
public interface ISolutionDetector
{
    /// <summary>
    /// Detects the solution root. When <paramref name="isExplicitRoot"/> is true,
    /// <paramref name="startPath"/> is used as the root as-is (a solution file directly inside it is
    /// still recorded). Otherwise walks up to the nearest .sln/.slnx, then the git root,
    /// falling back to <paramref name="startPath"/>.
    /// </summary>
    public SolutionInfo Detect(string startPath, bool isExplicitRoot = false);
}
