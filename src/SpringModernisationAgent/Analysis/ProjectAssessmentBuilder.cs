using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class ProjectAssessmentBuilder
{
    public ProjectAssessment Build(
        string organisation,
        IReadOnlyList<RepositoryAssessment> repositories,
        RepositoryDependencyGraph dependencyGraph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisation);
        ArgumentNullException.ThrowIfNull(repositories);
        ArgumentNullException.ThrowIfNull(dependencyGraph);

        return new ProjectAssessment(
            Organisation: organisation,
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: repositories
                .OrderBy(repository => repository.RepositoryName)
                .ToList(),
            DependencyGraph: dependencyGraph);
    }
}