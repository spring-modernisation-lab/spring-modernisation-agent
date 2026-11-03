using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class TransformationResultsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <summary>
    /// Reads previous execution results. A missing file means no repository
    /// has been executed yet, which is a valid state, so an empty list is
    /// returned.
    /// </summary>
    public async Task<IReadOnlyList<TransformationExecutionResult>> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);

        var results = await JsonSerializer.DeserializeAsync<
            List<TransformationExecutionResult>>(
            stream,
            JsonOptions,
            cancellationToken);

        return results ?? [];
    }
}
