using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// The only component that writes to disk. Applies a <see cref="MigrationPlan"/>.
/// </summary>
public interface IFileEditEngine
{
    /// <summary>
    /// Validates and applies the plan. Every path must stay inside the solution root, every
    /// edited file must match the hash it was planned from, and every edited file is re-parsed
    /// and verified before anything is written. In dry-run mode all checks still run but
    /// nothing is written. On a write failure, files already written are restored.
    /// </summary>
    /// <returns>Relative paths of files written (or that would be written in dry-run mode).</returns>
    /// <exception cref="MigrationException">On unresolved conflicts, invalid paths, stale plans or failed verification.</exception>
    public IReadOnlyList<string> Apply(MigrationPlan plan, bool dryRun = false, CancellationToken cancellationToken = default);
}
