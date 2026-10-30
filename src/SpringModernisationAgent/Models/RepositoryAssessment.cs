using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;

namespace SpringModernisationAgent.Models;

public sealed record RepositoryAssessment(
    string RepositoryName,
    string CommitSha,
    RepositoryKind Kind,
    MavenProject MavenProject,
    SpringPlatform Spring,
    IReadOnlyList<RepositoryDependencyEdge> InternalDependencies,
    IReadOnlyList<MavenDependency> ExternalDependencies,
    MigrationRouteSelection? MigrationRoute = null,
    PortfolioBaselineBuildResult? BaselineBuild = null,
    BaselineBuildValidation? BaselineBuildValidation = null,
    CommitProvenance? CommitProvenance = null,
    MigrationReadiness? MigrationReadiness = null);
