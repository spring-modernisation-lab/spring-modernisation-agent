using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Models;

public sealed record RepositoryAssessment(
    string RepositoryName,
    MavenProject MavenProject,
    SpringPlatform Spring,
    IReadOnlyList<RepositoryDependencyEdge> InternalDependencies,
    IReadOnlyList<MavenDependency> ExternalDependencies);