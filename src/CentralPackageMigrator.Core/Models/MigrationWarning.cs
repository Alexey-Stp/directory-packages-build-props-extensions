namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// A non-fatal warning produced during analysis or planning.
/// </summary>
public sealed record MigrationWarning
{
    /// <summary>Machine-readable warning code (e.g., "CPM001").</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable warning message.</summary>
    public required string Message { get; init; }

    /// <summary>Related project path, if applicable.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Related package ID, if applicable.</summary>
    public string? PackageId { get; init; }
}

/// <summary>
/// Well-known warning codes.
/// </summary>
public static class WarningCodes
{
    public const string LegacyProjectFormat = "CPM001";
    public const string NoPackageReferences = "CPM002";
    public const string ExistingCentralManagement = "CPM003";
    public const string ConditionalVersionReference = "CPM004";
    public const string MalformedXml = "CPM005";
    public const string UpdateReferenceDetected = "CPM006";
    public const string ExistingDirectoryPackagesProps = "CPM007";
    public const string VersionOverrideDetected = "CPM008";
}
