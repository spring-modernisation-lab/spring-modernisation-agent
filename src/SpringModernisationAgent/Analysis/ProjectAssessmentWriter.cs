using System.Text.Json;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class ProjectAssessmentWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task WriteAsync(
        ProjectAssessment assessment,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);

        await JsonSerializer.SerializeAsync(
            stream,
            assessment,
            JsonOptions,
            cancellationToken);
    }
}