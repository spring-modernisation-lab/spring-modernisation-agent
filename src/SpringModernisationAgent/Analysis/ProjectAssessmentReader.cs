using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class ProjectAssessmentReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<ProjectAssessment> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Project assessment was not found.",
                path);
        }

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<ProjectAssessment>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Project assessment '{path}' could not be deserialized.");
    }
}
