using System.Text.Json;
using System.Text.Json.Serialization;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class PortfolioBaselineBuildResultsReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<PortfolioBaselineBuildResults> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Portfolio baseline build results file was not found.",
                path);
        }

        await using var stream = File.OpenRead(path);

        var results = await JsonSerializer.DeserializeAsync<PortfolioBaselineBuildResults>(
            stream,
            JsonOptions,
            cancellationToken);

        if (results is null)
        {
            throw new InvalidOperationException(
                "Portfolio baseline build results could not be read.");
        }

        Validate(results);

        return results;
    }

    private static void Validate(PortfolioBaselineBuildResults results)
    {
        if (results.Repositories is null || results.Repositories.Count == 0)
        {
            throw new InvalidOperationException(
                "Portfolio baseline build results contain no repositories.");
        }

        var duplicateRepository = results.Repositories
            .GroupBy(
                repository => repository.RepositoryName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateRepository is not null)
        {
            throw new InvalidOperationException(
                $"Portfolio baseline build results contain duplicate repository '{duplicateRepository.Key}'.");
        }

        foreach (var repository in results.Repositories)
        {
            if (string.IsNullOrWhiteSpace(repository.RepositoryName))
            {
                throw new InvalidOperationException(
                    "Portfolio baseline build results contain a repository without a name.");
            }

            if (repository.Wave < 1)
            {
                throw new InvalidOperationException(
                    $"Repository '{repository.RepositoryName}' has invalid migration wave '{repository.Wave}'.");
            }

            if (repository.Status == BaselineBuildStatus.NotRun)
            {
                continue;
            }

            if (repository.Status is not BaselineBuildStatus.Passed
                and not BaselineBuildStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Repository '{repository.RepositoryName}' has an unsupported build status.");
            }
        }
    }
}