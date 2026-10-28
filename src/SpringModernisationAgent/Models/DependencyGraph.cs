namespace SpringModernisationAgent.Models;

public enum RepositoryDependencyType
{
    Parent,
    Compile,
    Runtime,
    Test
}

public sealed record RepositoryDependencyEdge(
    string FromRepository,
    string ToRepository,
    RepositoryDependencyType Type,
    string? Version);

public sealed record RepositoryDependencyGraph(
    IReadOnlyList<string> Repositories,
    IReadOnlyList<RepositoryDependencyEdge> Edges);