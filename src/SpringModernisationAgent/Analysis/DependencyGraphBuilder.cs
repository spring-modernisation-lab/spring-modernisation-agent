using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class DependencyGraphBuilder
{
    public RepositoryDependencyGraph Build(
        IReadOnlyList<RepositoryInventory> inventories)
    {
        var repositoriesByArtifact =
            inventories.ToDictionary(
                inventory =>
                    inventory.MavenProject.Coordinate.Key,
                inventory => inventory.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        var edges = new List<RepositoryDependencyEdge>();

        foreach (var inventory in inventories)
        {
            AddParentEdge(
                inventory,
                repositoriesByArtifact,
                edges);

            AddDependencyEdges(
                inventory,
                repositoriesByArtifact,
                edges);
        }

        return new RepositoryDependencyGraph(
            Repositories: inventories
                .Select(x => x.RepositoryName)
                .OrderBy(x => x)
                .ToList(),

            Edges: edges);
    }

    private static void AddParentEdge(
        RepositoryInventory inventory,
        IReadOnlyDictionary<string, string> repositoriesByArtifact,
        ICollection<RepositoryDependencyEdge> edges)
    {
        var parent =
            inventory.MavenProject.Parent;

        if (parent is null)
        {
            return;
        }

        if (!repositoriesByArtifact.TryGetValue(
                parent.Key,
                out var parentRepository))
        {
            return;
        }

        edges.Add(
            new RepositoryDependencyEdge(
                FromRepository: inventory.RepositoryName,
                ToRepository: parentRepository,
                Type: RepositoryDependencyType.Parent,
                Version: parent.Version));
    }

    private static void AddDependencyEdges(
        RepositoryInventory inventory,
        IReadOnlyDictionary<string, string> repositoriesByArtifact,
        ICollection<RepositoryDependencyEdge> edges)
    {
        foreach (var dependency
                 in inventory.MavenProject.Dependencies)
        {
            if (!repositoriesByArtifact.TryGetValue(
                    dependency.Coordinate.Key,
                    out var dependencyRepository))
            {
                continue;
            }

            edges.Add(
                new RepositoryDependencyEdge(
                    FromRepository: inventory.RepositoryName,
                    ToRepository: dependencyRepository,
                    Type: ToDependencyType(
                        dependency.Scope),
                    Version: dependency.Coordinate.Version));
        }
    }

    private static RepositoryDependencyType ToDependencyType(
        string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "runtime" => RepositoryDependencyType.Runtime,
            "test" => RepositoryDependencyType.Test,
            _ => RepositoryDependencyType.Compile
        };
    }
}
