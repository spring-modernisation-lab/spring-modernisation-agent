using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public sealed class MigrationAnalysisAgent
    : IMigrationAnalysisAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly IMigrationAnalysisModel _model;

    public MigrationAnalysisAgent(
        IMigrationAnalysisModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
    }

    public async Task<MigrationAnalysisResult> AnalyzeAsync(
        MigrationAnalysisInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var prompt = CreatePrompt(input);

        var result = await _model.AnalyzeAsync(
            prompt,
            cancellationToken);

        ValidateResult(input, result);

        return result;
    }

    private static string CreatePrompt(
        MigrationAnalysisInput input)
    {
        var evidence = JsonSerializer.Serialize(
            input,
            JsonOptions);

        return
            """
            Produce a migration strategy and repository-level analysis
            using only the deterministic evidence supplied below.

            Rules:

            1. Do not change migration wave numbers.
            2. Do not move repositories between migration waves.
            3. Do not add or remove repositories.
            4. Do not change readiness statuses.
            5. Do not claim that a blocked repository is ready.
            6. Do not claim that a failed or unexecuted validation passed.
            7. Do not invent build results, changed files, diagnostics,
               dependencies or transformation outcomes.
            8. Treat missing transformation evidence as not yet executed,
               not as success or failure.
            9. Explain the deterministic wave ordering and repository
               dependencies clearly.
            10. Ground risks and recommendations in the supplied evidence.
            11. Do not propose arbitrary shell, Git or Maven commands.
            12. Return only the requested structured result.

            The strategy must:
            - provide a concise portfolio-level summary;
            - contain exactly the supplied migration waves;
            - preserve repository membership and ordering within each wave;
            - provide an objective and rationale for every wave;
            - identify risks supported by the evidence;
            - provide practical engineering recommendations.

            Repository analyses must:
            - contain exactly one analysis for every supplied repository;
            - explain transformation and validation outcomes where available;
            - distinguish between not executed, succeeded and failed;
            - include recommended investigation steps for failures;
            - remain descriptive and read-only.

            Deterministic migration evidence:

            """ + evidence;
    }

    private static void ValidateResult(
        MigrationAnalysisInput input,
        MigrationAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result.Strategy);
        ArgumentNullException.ThrowIfNull(
            result.Strategy.Waves);
        ArgumentNullException.ThrowIfNull(
            result.RepositoryAnalyses);

        ValidateWaves(
            input.Waves,
            result.Strategy.Waves);

        ValidateRepositoryAnalyses(
            input.Repositories,
            result.RepositoryAnalyses);
    }

    private static void ValidateWaves(
        IReadOnlyList<MigrationAnalysisWaveInput> expectedWaves,
        IReadOnlyList<MigrationStrategyWave> actualWaves)
    {
        if (actualWaves.Count != expectedWaves.Count)
        {
            throw new InvalidOperationException(
                "The migration analysis changed the number of migration waves.");
        }

        for (var index = 0;
             index < expectedWaves.Count;
             index++)
        {
            var expected = expectedWaves[index];
            var actual = actualWaves[index];

            if (actual.Number != expected.Number)
            {
                throw new InvalidOperationException(
                    "The migration analysis changed a migration wave number.");
            }

            if (!actual.Repositories.SequenceEqual(
                    expected.Repositories,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The migration analysis changed repository membership " +
                    $"or ordering for wave {expected.Number}.");
            }
        }
    }

    private static void ValidateRepositoryAnalyses(
        IReadOnlyList<MigrationAnalysisRepositoryInput>
            expectedRepositories,
        IReadOnlyList<MigrationAnalysis> actualAnalyses)
    {
        var expectedNames = expectedRepositories
            .Select(repository =>
                repository.RepositoryName)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actualNames = actualAnalyses
            .Select(analysis =>
                analysis.RepositoryName)
            .OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!actualNames.SequenceEqual(
                expectedNames,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The migration analysis added, removed or duplicated " +
                "repository analyses.");
        }
    }
}