using CentralPackageMigrator.Core.Models;
using CentralPackageMigrator.Infrastructure.Serialization;

namespace CentralPackageMigrator.Cli.Output;

/// <summary>
/// Renders contract objects as JSON to standard output.
/// Delegates serialization to <see cref="PlanSerializer"/>.
/// </summary>
public sealed class JsonRenderer
{
    private readonly PlanSerializer _serializer;
    private readonly TextWriter _output;

    public JsonRenderer(PlanSerializer serializer, TextWriter output)
    {
        _serializer = serializer;
        _output = output;
    }

    /// <summary>
    /// Serializes the migration plan to JSON and writes it to stdout.
    /// </summary>
    public void RenderPlan(MigrationPlan plan) => _output.WriteLine(_serializer.Serialize(plan));

    /// <summary>
    /// Serializes the migration plan to JSON and writes it to a file.
    /// </summary>
    public void RenderPlanToFile(MigrationPlan plan, string filePath) =>
        File.WriteAllText(filePath, _serializer.Serialize(plan));

    /// <summary>Writes any contract object (apply result, check result) to stdout.</summary>
    public void Render<T>(T value) => _output.WriteLine(_serializer.SerializeObject(value));

    /// <summary>Writes a structured error to stdout.</summary>
    public void RenderError(MigrationError error) => Render(new ErrorOutput { Error = error });
}
