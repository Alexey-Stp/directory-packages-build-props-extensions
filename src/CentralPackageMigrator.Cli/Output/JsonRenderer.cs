using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Infrastructure.Serialization;

namespace CentralPackageMigrator.Cli.Output;

/// <summary>
/// Renders migration data as JSON to standard output.
/// Delegates serialization to <see cref="PlanSerializer"/>.
/// </summary>
public sealed class JsonRenderer
{
    private readonly PlanSerializer _serializer;

    public JsonRenderer(PlanSerializer serializer)
    {
        _serializer = serializer;
    }

    /// <summary>
    /// Serializes the migration plan to JSON and writes it to stdout.
    /// </summary>
    public void RenderPlan(MigrationPlan plan)
    {
        var json = _serializer.Serialize(plan);
        Console.WriteLine(json);
    }

    /// <summary>
    /// Serializes the migration plan to JSON and writes it to a file.
    /// </summary>
    public void RenderPlanToFile(MigrationPlan plan, string filePath)
    {
        var json = _serializer.Serialize(plan);
        File.WriteAllText(filePath, json);
    }
}
