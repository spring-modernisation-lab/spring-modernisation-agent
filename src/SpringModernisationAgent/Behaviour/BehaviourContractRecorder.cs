using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Behaviour;

/// <summary>Records one behaviour record (the probe, or a fake in tests).</summary>
public interface IBehaviourRecorder
{
    /// <summary>False when the repository has nothing the probe can start (for example a parent POM).</summary>
    bool CanRecord(
        string repositoryPath);

    Task<BehaviourRecord> RecordAsync(
        string repositoryPath,
        string repository,
        string commit,
        CancellationToken cancellationToken = default);
}

public sealed class ProbeRecorder(BehaviourProbe probe) : IBehaviourRecorder
{
    public bool CanRecord(
        string repositoryPath) =>
        BehaviourProbe.Discover(repositoryPath) is not null;

    public Task<BehaviourRecord> RecordAsync(
        string repositoryPath,
        string repository,
        string commit,
        CancellationToken cancellationToken = default) =>
        probe.RecordAsync(repositoryPath, repository, commit, cancellationToken);
}

public interface IBehaviourContractRecorder
{
    /// <summary>Before any change: null when the repository can't be probed. Never throws.</summary>
    Task<BehaviourReport?> RecordBaselineAsync(
        TransformationPlan plan,
        string repositoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>After migration: records again and compares. Never throws.</summary>
    Task<BehaviourReport> CompareAsync(
        TransformationPlan plan,
        string repositoryPath,
        BehaviourReport baseline,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The behaviour contract in the pipeline (docs/system-design.md §3): a
/// baseline on the assessed commit with the source platform's JDK, and a
/// record after migration with the target's, compared item by item. Its own
/// failures are recorded in the report and never stop a migration.
/// </summary>
public sealed class BehaviourContractRecorder : IBehaviourContractRecorder
{
    private readonly IBehaviourRecorder _baseline;
    private readonly IBehaviourRecorder _after;
    private readonly IMigrationProgress _progress;

    /// <param name="baseline">Records with the source platform's JDK (Java 11 for Boot 2.7).</param>
    /// <param name="after">Records with the target platform's JDK (Java 21).</param>
    public BehaviourContractRecorder(
        IBehaviourRecorder baseline,
        IBehaviourRecorder after,
        IMigrationProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(after);

        _baseline = baseline;
        _after = after;
        _progress = progress ?? NullMigrationProgress.Instance;
    }

    public async Task<BehaviourReport?> RecordBaselineAsync(
        TransformationPlan plan,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (!_baseline.CanRecord(repositoryPath))
        {
            return null;
        }

        try
        {
            _progress.Report(plan.RepositoryName, "behaviour contract: recording the baseline");

            var record = await _baseline.RecordAsync(repositoryPath, plan.RepositoryName, plan.SourceCommitSha, cancellationToken);

            _progress.Report(plan.RepositoryName, $"behaviour contract: baseline has {record.Items.Count} behaviours");

            return new BehaviourReport(record, null, null, null);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _progress.Report(plan.RepositoryName, $"behaviour contract: baseline failed: {exception.Message}");
            return new BehaviourReport(null, null, null, $"the baseline could not be recorded: {exception.Message}");
        }
        finally
        {
            // The baseline is built with the source JDK; its output must not
            // reach the target build.
            DeleteBuildOutput(repositoryPath);
        }
    }

    public async Task<BehaviourReport> CompareAsync(
        TransformationPlan plan,
        string repositoryPath,
        BehaviourReport baseline,
        TransformationValidationResult validation,
        CancellationToken cancellationToken = default)
    {
        if (baseline.Baseline is null)
        {
            return baseline;
        }

        if (validation.Checks.FirstOrDefault(check => check.Check == "compile") is { Passed: false })
        {
            return baseline with { Note = "not compared: the migrated code does not compile" };
        }

        try
        {
            _progress.Report(plan.RepositoryName, "behaviour contract: recording the migrated application");

            var after = await _after.RecordAsync(repositoryPath, plan.RepositoryName, plan.SourceCommitSha, cancellationToken);
            var comparison = BehaviourContract.Compare(baseline.Baseline, after);

            _progress.Report(
                plan.RepositoryName,
                comparison.Matches
                    ? $"behaviour contract holds ({comparison.Same} behaviours unchanged)"
                    : $"behaviour contract does not hold: {comparison.Differences.Count} differ, {comparison.Same} unchanged" +
                      (comparison.Unchecked == 0 ? "" : $", {comparison.Unchecked} not checked (an application does not start)"));

            return new BehaviourReport(baseline.Baseline, after, comparison, null);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            _progress.Report(plan.RepositoryName, $"behaviour contract: comparison failed: {exception.Message}");
            return baseline with { Note = $"not compared: {exception.Message}" };
        }
    }

    private static void DeleteBuildOutput(
        string repositoryPath)
    {
        var target = Path.Combine(repositoryPath, "target");

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
