using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class TransformationValidator
{
    private readonly IMavenProcessRunner _mavenProcessRunner;
    private readonly IMigrationProgress _progress;

    public TransformationValidator(
        IMavenProcessRunner mavenProcessRunner,
        IMigrationProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(mavenProcessRunner);

        _mavenProcessRunner = mavenProcessRunner;
        _progress = progress ?? NullMigrationProgress.Instance;
    }

    public async Task<TransformationValidationResult> ValidateAsync(
        TransformationPlan transformationPlan,
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transformationPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            return new TransformationValidationResult(
                RepositoryName: transformationPlan.RepositoryName,
                SourceCommitSha: transformationPlan.SourceCommitSha,
                Status: TransformationValidationStatus.NotRun,
                Checks: []);
        }

        if (transformationPlan.Validation.Count == 0)
        {
            return new TransformationValidationResult(
                RepositoryName: transformationPlan.RepositoryName,
                SourceCommitSha: transformationPlan.SourceCommitSha,
                Status: TransformationValidationStatus.NotRun,
                Checks: []);
        }

        var checkResults =
            new List<TransformationValidationCheckResult>();

        foreach (var check in transformationPlan.Validation)
        {
            var arguments =
                TransformationValidationMapping.GetMavenArguments(
                    check);

            // Allow for coarse file timestamp resolution.
            var checkStartedUtc = DateTime.UtcNow.AddSeconds(-2);

            _progress.Report(
                transformationPlan.RepositoryName,
                $"check {check}: running");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var processResult =
                await _mavenProcessRunner.RunAsync(
                    repositoryPath,
                    arguments,
                    cancellationToken);

            var passed = processResult.ExitCode == 0;

            _progress.Report(
                transformationPlan.RepositoryName,
                $"check {check}: {(passed ? "passed" : "FAILED")} ({stopwatch.Elapsed.TotalSeconds:F0}s)");

            checkResults.Add(
                new TransformationValidationCheckResult(
                    Check: check,
                    Passed: passed,
                    StandardOutput:
                        NullIfEmpty(processResult.StandardOutput),
                    StandardError:
                        NullIfEmpty(processResult.StandardError))
                {
                    FailingTests = passed
                        ? []
                        : SurefireReportReader.Read(
                            repositoryPath,
                            checkStartedUtc)
                });

            if (!passed)
            {
                return new TransformationValidationResult(
                    RepositoryName: transformationPlan.RepositoryName,
                    SourceCommitSha: transformationPlan.SourceCommitSha,
                    Status: TransformationValidationStatus.Failed,
                    Checks: checkResults);
            }
        }

        return new TransformationValidationResult(
            RepositoryName: transformationPlan.RepositoryName,
            SourceCommitSha: transformationPlan.SourceCommitSha,
            Status: TransformationValidationStatus.Passed,
            Checks: checkResults);
    }

    private static string? NullIfEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }
}
