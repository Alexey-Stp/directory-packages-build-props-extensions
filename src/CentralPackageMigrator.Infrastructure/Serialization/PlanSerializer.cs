using System.Text.Json;
using System.Text.Json.Serialization;
using CentralPackageMigrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace CentralPackageMigrator.Infrastructure.Serialization;

/// <summary>
/// Serializes and deserializes <see cref="MigrationPlan"/> instances to/from JSON.
/// Produces the stable IDE integration contract format with camelCase property naming.
/// </summary>
public sealed class PlanSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private readonly ILogger<PlanSerializer> _logger;

    public PlanSerializer(ILogger<PlanSerializer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Serializes a <see cref="MigrationPlan"/> to a formatted JSON string.
    /// </summary>
    public string Serialize(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _logger.LogDebug("Serializing migration plan (schema {SchemaVersion})", plan.SchemaVersion);

        try
        {
            string json = JsonSerializer.Serialize(plan, SerializerOptions);

            _logger.LogDebug("Serialized migration plan ({Length} chars)", json.Length);
            return json;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to serialize migration plan");
            throw;
        }
    }

    /// <summary>
    /// Deserializes a JSON string to a <see cref="MigrationPlan"/>.
    /// </summary>
    public MigrationPlan Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        _logger.LogDebug("Deserializing migration plan from JSON ({Length} chars)", json.Length);

        try
        {
            MigrationPlan? plan = JsonSerializer.Deserialize<MigrationPlan>(json, SerializerOptions);

            if (plan is null)
            {
                throw new JsonException("Deserialized MigrationPlan was null.");
            }

            _logger.LogDebug(
                "Deserialized migration plan (schema {SchemaVersion}, {Creates} creates, {Edits} edits)",
                plan.SchemaVersion, plan.Creates.Count, plan.Edits.Count);

            return plan;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize migration plan from JSON");
            throw;
        }
    }

    /// <summary>
    /// Serializes a <see cref="MigrationPlan"/> and writes the JSON to a file.
    /// </summary>
    public void SerializeToFile(MigrationPlan plan, string filePath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string json = Serialize(plan);
        File.WriteAllText(filePath, json);

        _logger.LogInformation("Migration plan written to: {FilePath}", filePath);
    }

    /// <summary>
    /// Reads JSON from a file and deserializes it to a <see cref="MigrationPlan"/>.
    /// </summary>
    public MigrationPlan DeserializeFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Migration plan file not found: {filePath}", filePath);
        }

        _logger.LogDebug("Reading migration plan from: {FilePath}", filePath);

        string json = File.ReadAllText(filePath);
        return Deserialize(json);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        return options;
    }
}
