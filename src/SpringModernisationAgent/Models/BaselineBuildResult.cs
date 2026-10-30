namespace SpringModernisationAgent.Models;

public enum BaselineBuildStatus
{
    NotRun,
    Passed,
    Failed
}

public sealed record BaselineBuildResult(
    string RepositoryName,
    BaselineBuildStatus Status,
    int? ExitCode,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? StandardOutput,
    string? StandardError)
{
    public bool Passed => Status == BaselineBuildStatus.Passed;
}
