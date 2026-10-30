namespace SpringModernisationAgent.Models;

public enum TransformationValidationStatus
{
    NotRun,
    Passed,
    Failed
}

public sealed record TransformationValidationResult(
    string RepositoryName,
    string SourceCommitSha,
    TransformationValidationStatus Status,
    IReadOnlyList<TransformationValidationCheckResult> Checks)
{
    public bool Passed =>
        Status == TransformationValidationStatus.Passed;
}

public sealed record TransformationValidationCheckResult(
    string Check,
    bool Passed,
    string? StandardOutput,
    string? StandardError);