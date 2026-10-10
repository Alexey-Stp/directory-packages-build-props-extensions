using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// A user-provided resolution for a version conflict.
/// </summary>
public sealed partial record ConflictResolution
{
    private const int MaxPackageIdLength = 100;

    /// <summary>The package ID to resolve.</summary>
    public required string PackageId { get; init; }

    /// <summary>The version to use for the centralized reference.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Parses <c>Package=Version</c> entries. Validates package IDs and NuGet version syntax,
    /// collapses exact duplicates and rejects contradictory duplicates.
    /// </summary>
    /// <exception cref="MigrationException">With <see cref="ErrorCodes.InvalidArguments"/> on invalid input.</exception>
    public static IReadOnlyList<ConflictResolution> ParseMany(IEnumerable<string>? entries)
    {
        if (entries is null)
        {
            return [];
        }

        var byId = new Dictionary<string, ConflictResolution>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ConflictResolution>();

        foreach (string entry in entries)
        {
            var resolution = Parse(entry);

            if (byId.TryGetValue(resolution.PackageId, out var existing))
            {
                if (!string.Equals(existing.Version, resolution.Version, StringComparison.OrdinalIgnoreCase))
                {
                    throw new MigrationException(
                        ErrorCodes.InvalidArguments,
                        $"Conflicting resolutions for '{resolution.PackageId}': '{existing.Version}' and '{resolution.Version}'.");
                }

                continue;
            }

            byId[resolution.PackageId] = resolution;
            ordered.Add(resolution);
        }

        return ordered.AsReadOnly();
    }

    /// <summary>Parses a single <c>Package=Version</c> entry.</summary>
    public static ConflictResolution Parse(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        int eqIndex = entry.IndexOf('=', StringComparison.Ordinal);
        if (eqIndex <= 0 || eqIndex >= entry.Length - 1)
        {
            throw new MigrationException(
                ErrorCodes.InvalidArguments,
                $"Invalid resolution format: '{entry}'. Expected format: Package=Version");
        }

        string packageId = entry[..eqIndex].Trim();
        string version = entry[(eqIndex + 1)..].Trim();

        if (!IsValidPackageId(packageId))
        {
            throw new MigrationException(
                ErrorCodes.InvalidArguments,
                $"Invalid package ID in resolution: '{packageId}'.");
        }

        if (!IsValidVersion(version))
        {
            throw new MigrationException(
                ErrorCodes.InvalidArguments,
                $"Invalid version in resolution for '{packageId}': '{version}'. Expected a NuGet version or version range.");
        }

        return new ConflictResolution { PackageId = packageId, Version = version };
    }

    /// <summary>True if <paramref name="packageId"/> is a syntactically valid NuGet package ID.</summary>
    public static bool IsValidPackageId(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        return packageId.Length <= MaxPackageIdLength && PackageIdRegex().IsMatch(packageId);
    }

    /// <summary>True if <paramref name="version"/> is a NuGet version, range or floating version.</summary>
    public static bool IsValidVersion(string version) =>
        VersionRange.TryParse(version, allowFloating: true, out _);

    [GeneratedRegex(@"^\w+(?:[.-]\w+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex();
}
