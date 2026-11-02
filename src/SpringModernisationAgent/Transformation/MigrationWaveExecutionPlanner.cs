using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class MigrationWaveExecutionPlanner
{
    public IReadOnlyList<TransformationPlan> GetNextWave(
        IReadOnlyList<TransformationPlan> transformationPlans)
    {
        ArgumentNullException.ThrowIfNull(transformationPlans);

        if (transformationPlans.Count == 0)
        {
            return [];
        }

        var nextWaveNumber = transformationPlans
            .Min(plan => plan.MigrationWave);

        return GetPlansForWave(
            transformationPlans,
            nextWaveNumber);
    }

    public IReadOnlyList<TransformationPlan> GetNextWave(
        IReadOnlyList<TransformationPlan> transformationPlans,
        IReadOnlyList<TransformationExecutionResult> executionResults)
    {
        ArgumentNullException.ThrowIfNull(transformationPlans);
        ArgumentNullException.ThrowIfNull(executionResults);

        if (transformationPlans.Count == 0)
        {
            return [];
        }

        var resultsByRepository = executionResults
            .ToDictionary(
                result => result.Transformation.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        var waves = transformationPlans
            .Select(plan => plan.MigrationWave)
            .Distinct()
            .OrderBy(wave => wave)
            .ToList();

        foreach (var waveNumber in waves)
        {
            var wavePlans = GetPlansForWave(
                transformationPlans,
                waveNumber);

            var waveStatus = GetWaveStatus(
                wavePlans,
                resultsByRepository);

            switch (waveStatus)
            {
                case MigrationWaveExecutionStatus.Passed:
                    continue;

                case MigrationWaveExecutionStatus.NotRun:
                    return wavePlans;

                case MigrationWaveExecutionStatus.Failed:
                    return [];
            }
        }

        return [];
    }

    private static MigrationWaveExecutionStatus GetWaveStatus(
    IReadOnlyList<TransformationPlan> wavePlans,
    IReadOnlyDictionary<string, TransformationExecutionResult>
        resultsByRepository)
    {
        var hasMissingResult = false;
        var hasFailedResult = false;

        foreach (var plan in wavePlans)
        {
            if (!resultsByRepository.TryGetValue(
                    plan.RepositoryName,
                    out var result))
            {
                hasMissingResult = true;
                continue;
            }

            if (!string.Equals(
                    plan.SourceCommitSha,
                    result.Transformation.SourceCommitSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Execution result for repository " +
                    $"'{plan.RepositoryName}' does not match " +
                    "transformation plan source commit.");
            }

            if (!string.Equals(
                    plan.RepositoryName,
                    result.Validation.RepositoryName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Validation result repository " +
                    $"'{result.Validation.RepositoryName}' does not match " +
                    $"transformation plan repository " +
                    $"'{plan.RepositoryName}'.");
            }

            if (!string.Equals(
                    plan.SourceCommitSha,
                    result.Validation.SourceCommitSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Validation result for repository " +
                    $"'{plan.RepositoryName}' does not match " +
                    "transformation plan source commit.");
            }

            if (!result.Succeeded)
            {
                hasFailedResult = true;
            }
        }

        // A known failure takes precedence over repositories that have not run.
        if (hasFailedResult)
        {
            return MigrationWaveExecutionStatus.Failed;
        }

        if (hasMissingResult)
        {
            return MigrationWaveExecutionStatus.NotRun;
        }

        return MigrationWaveExecutionStatus.Passed;
    }

    private static IReadOnlyList<TransformationPlan> GetPlansForWave(
        IReadOnlyList<TransformationPlan> transformationPlans,
        int waveNumber)
    {
        return transformationPlans
            .Where(plan =>
                plan.MigrationWave == waveNumber)
            .OrderBy(
                plan => plan.RepositoryName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private enum MigrationWaveExecutionStatus
    {
        NotRun,
        Passed,
        Failed
    }
}