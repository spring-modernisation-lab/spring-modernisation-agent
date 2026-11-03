using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public static class TransformationPlansReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <summary>Reads the approved plans; they must exist.</summary>
    public static async Task<IReadOnlyList<TransformationPlan>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Transformation plans were not found.",
                path);
        }

        await using var stream = File.OpenRead(path);

        var plans = await JsonSerializer.DeserializeAsync<List<TransformationPlan>>(
            stream,
            JsonOptions,
            cancellationToken);

        return plans
            ?? throw new InvalidOperationException(
                "Transformation plans could not be deserialized.");
    }
}
