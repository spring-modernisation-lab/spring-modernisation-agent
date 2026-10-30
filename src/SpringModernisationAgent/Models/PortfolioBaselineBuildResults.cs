namespace SpringModernisationAgent.Models;

public sealed record PortfolioBaselineBuildResults(
    IReadOnlyList<PortfolioBaselineBuildResult> Repositories)
{
    public bool Passed =>
        Repositories.Count > 0 &&
        Repositories.All(repository =>
            repository.Status == BaselineBuildStatus.Passed);
}

public sealed record PortfolioBaselineBuildResult(
    string RepositoryName,
    string? CommitSha,
    int Wave,
    BaselineBuildOperation Operation,
    BaselineBuildStatus Status);

public enum BaselineBuildOperation
{
    Install,
    Verify
}

