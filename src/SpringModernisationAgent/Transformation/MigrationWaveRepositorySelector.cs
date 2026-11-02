using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class MigrationWaveRepositorySelector
{
    public IReadOnlyList<TransformationPlan> GetRepositoriesToExecute(
        IReadOnlyList<TransformationPlan> activeWavePlans,
        IReadOnlyList<TransformationExecutionResult> executionResults)
    {
        ArgumentNullException.ThrowIfNull(activeWavePlans);
        ArgumentNullException.ThrowIfNull(executionResults);

        if (activeWavePlans.Count == 0)
        {
            return [];
        }

        EnsureSingleWave(activeWavePlans);

        var resultsByRepository = executionResults
            .ToDictionary(
                result => result.Transformation.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        var repositoriesToExecute =
            new List<TransformationPlan>();

        foreach (var plan in activeWavePlans)
        {
            if (!resultsByRepository.TryGetValue(
                    plan.RepositoryName,
                    out var executionResult))
            {
                repositoriesToExecute.Add(plan);
                continue;
            }

            EnsureResultMatchesPlan(
                plan,
                executionResult);

            if (executionResult.Succeeded)
            {
                continue;
            }

            // Failed repositories are intentionally not retried
            // automatically. Remediation/retry requires a separate
            // explicit decision.
        }

        return repositoriesToExecute
            .OrderBy(
                plan => plan.RepositoryName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void EnsureSingleWave(
        IReadOnlyList<TransformationPlan> plans)
    {
        var waveNumbers = plans
            .Select(plan => plan.MigrationWave)
            .Distinct()
            .ToList();

        if (waveNumbers.Count == 1)
        {
            return;
        }

        throw new InvalidOperationException(
            "Repository execution selection must contain plans " +
            "from exactly one migration wave.");
    }

    private static void EnsureResultMatchesPlan(
        TransformationPlan plan,
        TransformationExecutionResult executionResult)
    {
        if (!string.Equals(
                plan.RepositoryName,
                executionResult.Transformation.RepositoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Execution result repository " +
                $"'{executionResult.Transformation.RepositoryName}' " +
                $"does not match transformation plan repository " +
                $"'{plan.RepositoryName}'.");
        }

        if (!string.Equals(
                plan.SourceCommitSha,
                executionResult.Transformation.SourceCommitSha,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Execution result for repository " +
                $"'{plan.RepositoryName}' does not match " +
                "transformation plan source commit.");
        }

        if (!string.Equals(
                plan.SourceCommitSha,
                executionResult.Validation.SourceCommitSha,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Validation result for repository " +
                $"'{plan.RepositoryName}' does not match " +
                "transformation plan source commit.");
        }

        if (!string.Equals(
                plan.RepositoryName,
                executionResult.Validation.RepositoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Validation result repository " +
                $"'{executionResult.Validation.RepositoryName}' " +
                $"does not match transformation plan repository " +
                $"'{plan.RepositoryName}'.");
        }
    }
}