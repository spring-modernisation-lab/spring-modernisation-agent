using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

/// <summary>
/// Decides which transformation plans may be released for execution.
/// A repository without a plan is not ready, so the earliest wave containing
/// such a repository is "blocked": its ready members may still run (members
/// of one wave do not depend on each other), but no later wave may run until
/// the blocked repositories are resolved. Fails closed on inconsistent input.
/// </summary>
public sealed class MigrationWaveGate
{
    public MigrationWaveGateResult Apply(
        MigrationWavePlan waves,
        IReadOnlyList<TransformationPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(waves);
        ArgumentNullException.ThrowIfNull(plans);

        var waveByRepository = waves.Waves
            .SelectMany(wave => wave.Repositories
                .Select(repository => (repository, wave.Number)))
            .ToDictionary(
                entry => entry.repository,
                entry => entry.Number,
                StringComparer.OrdinalIgnoreCase);

        EnsurePlansMatchTopology(plans, waveByRepository);

        var plannedRepositories = plans
            .Select(plan => plan.RepositoryName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var earliestBlockedWave = waves.Waves
            .OrderBy(wave => wave.Number)
            .FirstOrDefault(wave => wave.Repositories
                .Any(repository => !plannedRepositories.Contains(repository)));

        if (earliestBlockedWave is null)
        {
            return new MigrationWaveGateResult(
                ExecutablePlans: plans.ToList(),
                WithheldPlans: [],
                EarliestBlockedWave: null,
                BlockedRepositories: []);
        }

        var blockedRepositories = earliestBlockedWave.Repositories
            .Where(repository => !plannedRepositories.Contains(repository))
            .OrderBy(repository => repository, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MigrationWaveGateResult(
            ExecutablePlans: plans
                .Where(plan => plan.MigrationWave <= earliestBlockedWave.Number)
                .ToList(),
            WithheldPlans: plans
                .Where(plan => plan.MigrationWave > earliestBlockedWave.Number)
                .ToList(),
            EarliestBlockedWave: earliestBlockedWave.Number,
            BlockedRepositories: blockedRepositories);
    }

    private static void EnsurePlansMatchTopology(
        IReadOnlyList<TransformationPlan> plans,
        IReadOnlyDictionary<string, int> waveByRepository)
    {
        foreach (var plan in plans)
        {
            if (!waveByRepository.TryGetValue(
                    plan.RepositoryName,
                    out var expectedWave))
            {
                throw new InvalidOperationException(
                    $"Transformation plan for '{plan.RepositoryName}' " +
                    "is not part of the migration wave topology.");
            }

            if (plan.MigrationWave != expectedWave)
            {
                throw new InvalidOperationException(
                    $"Transformation plan for '{plan.RepositoryName}' " +
                    $"is in wave {plan.MigrationWave}, but the migration " +
                    $"wave topology places it in wave {expectedWave}.");
            }
        }
    }
}

public sealed record MigrationWaveGateResult(
    IReadOnlyList<TransformationPlan> ExecutablePlans,
    IReadOnlyList<TransformationPlan> WithheldPlans,
    int? EarliestBlockedWave,
    IReadOnlyList<string> BlockedRepositories);
