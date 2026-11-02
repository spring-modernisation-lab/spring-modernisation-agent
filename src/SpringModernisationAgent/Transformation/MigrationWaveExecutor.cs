using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class MigrationWaveExecutor
{
    private readonly MigrationWaveExecutionPlanner _wavePlanner;
    private readonly MigrationWaveRepositorySelector _repositorySelector;
    private readonly IOpenRewriteExecutionCoordinator _executionCoordinator;

    public MigrationWaveExecutor(
        MigrationWaveExecutionPlanner wavePlanner,
        MigrationWaveRepositorySelector repositorySelector,
        IOpenRewriteExecutionCoordinator executionCoordinator)
    {
        ArgumentNullException.ThrowIfNull(wavePlanner);
        ArgumentNullException.ThrowIfNull(repositorySelector);
        ArgumentNullException.ThrowIfNull(executionCoordinator);

        _wavePlanner = wavePlanner;
        _repositorySelector = repositorySelector;
        _executionCoordinator = executionCoordinator;
    }

    public async Task<IReadOnlyList<TransformationExecutionResult>> ExecuteNextWaveAsync(
        string organisation,
        IReadOnlyList<TransformationPlan> transformationPlans,
        IReadOnlyList<TransformationExecutionResult> existingResults,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisation);
        ArgumentNullException.ThrowIfNull(transformationPlans);
        ArgumentNullException.ThrowIfNull(existingResults);

        var activeWave = _wavePlanner.GetNextWave(
            transformationPlans,
            existingResults);

        if (activeWave.Count == 0)
        {
            return [];
        }

        var repositoriesToExecute =
            _repositorySelector.GetRepositoriesToExecute(
                activeWave,
                existingResults);

        if (repositoriesToExecute.Count == 0)
        {
            return [];
        }

        var results =
            new List<TransformationExecutionResult>();

        // Repositories within one wave do not depend on each other, so every
        // one is executed even if a peer fails. A failure still blocks the
        // next wave: MigrationWaveExecutionPlanner treats a wave containing a
        // failed result as Failed and returns no later wave.
        foreach (var plan in repositoriesToExecute)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result =
                await _executionCoordinator.ExecuteAsync(
                    organisation,
                    plan,
                    cancellationToken);

            results.Add(result);
        }

        return results;
    }
}