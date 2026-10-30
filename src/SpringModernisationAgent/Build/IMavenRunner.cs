using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Build;

public interface IMavenRunner
{
    Task<BaselineBuildResult> RunBaselineBuildAsync(
        string repositoryName,
        string repositoryPath,
        CancellationToken cancellationToken = default);
}