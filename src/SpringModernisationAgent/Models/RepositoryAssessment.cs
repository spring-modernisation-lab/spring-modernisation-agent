using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Models;

public sealed record RepositoryAssessment(
    string RepositoryName,
    RepositoryKind Kind,
    MavenProject MavenProject,
    SpringPlatform Spring,
    IReadOnlyList<RepositoryDependencyEdge> InternalDependencies,
    IReadOnlyList<MavenDependency> ExternalDependencies,
    MigrationRouteSelection? MigrationRoute = null);
