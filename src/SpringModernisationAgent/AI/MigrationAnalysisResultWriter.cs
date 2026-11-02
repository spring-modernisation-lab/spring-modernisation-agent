using System.Text.Json;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public sealed class MigrationAnalysisResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task WriteAsync(
        MigrationAnalysisResult result,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);

        await JsonSerializer.SerializeAsync(
            stream,
            result,
            JsonOptions,
            cancellationToken);
    }
}
