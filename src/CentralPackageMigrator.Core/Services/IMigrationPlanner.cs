using CentralPackageMigrator.Core.Models;

namespace CentralPackageMigrator.Core.Services;

/// <summary>
/// Generates a complete migration plan from analyzed solution data.
/// The plan phase produces NO side effects — it only computes the plan.
/// </summary>
public interface IMigrationPlanner
{
    /// <summary>
    /// Creates a migration plan for the given solution.
    /// </summary>
    public MigrationPlan CreatePlan(
        SolutionInfo solution,
        IReadOnlyList<ConflictResolution> resolutions);
}
