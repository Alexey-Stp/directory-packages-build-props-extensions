namespace CentralPackageMigrator.Core.Models;

/// <summary>
/// Uniquely identifies a NuGet package by its ID.
/// Comparison is case-insensitive per NuGet conventions.
/// </summary>
public sealed record PackageIdentity
{
    public string Id { get; }

    public PackageIdentity(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public bool Equals(PackageIdentity? other)
    {
        return other is not null &&
               string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode()
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
    }

    public override string ToString() => Id;
}
