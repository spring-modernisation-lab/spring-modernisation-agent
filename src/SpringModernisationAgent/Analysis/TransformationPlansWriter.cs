using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class TransformationPlansWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task WriteAsync(
        IReadOnlyList<TransformationPlan> plans,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);

        await JsonSerializer.SerializeAsync(
            stream,
            plans,
            JsonOptions,
            cancellationToken);
    }
}