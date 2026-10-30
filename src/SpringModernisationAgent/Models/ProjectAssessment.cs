namespace SpringModernisationAgent.Models;

public sealed record ProjectAssessment(
    string Organisation,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RepositoryAssessment> Repositories,
    RepositoryDependencyGraph DependencyGraph,
    MigrationWavePlan MigrationWaves);
