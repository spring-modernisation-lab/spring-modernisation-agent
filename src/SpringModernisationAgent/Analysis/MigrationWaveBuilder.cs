using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class MigrationWaveBuilder
{
    public MigrationWavePlan Build(RepositoryDependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var dependencies = graph.Repositories.ToDictionary(
            repository => repository,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var edge in graph.Edges)
        {
            if (!dependencies.ContainsKey(edge.FromRepository))
            {
                throw new InvalidOperationException(
                    $"Repository '{edge.FromRepository}' is missing from the graph.");
            }

            if (!dependencies.ContainsKey(edge.ToRepository))
            {
                throw new InvalidOperationException(
                    $"Repository '{edge.ToRepository}' is missing from the graph.");
            }

            dependencies[edge.FromRepository].Add(edge.ToRepository);
        }

        var remaining = new HashSet<string>(
            graph.Repositories,
            StringComparer.OrdinalIgnoreCase);

        var completed = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var waves = new List<MigrationWave>();
        var waveNumber = 1;

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(repository =>
                    dependencies[repository].All(completed.Contains))
                .OrderBy(repository => repository)
                .ToList();

            if (ready.Count == 0)
            {
                throw new InvalidOperationException(
                    "The repository dependency graph contains a cycle.");
            }

            waves.Add(new MigrationWave(
                Number: waveNumber,
                Repositories: ready));

            foreach (var repository in ready)
            {
                remaining.Remove(repository);
                completed.Add(repository);
            }

            waveNumber++;
        }

        return new MigrationWavePlan(waves);
    }
}
