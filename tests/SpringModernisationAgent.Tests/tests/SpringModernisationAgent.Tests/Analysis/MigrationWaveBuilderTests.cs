using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class MigrationWaveBuilderTests
{
    [Fact]
    public void Build_CreatesMigrationWavesInDependencyOrder()
    {
        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "account-service",
                "customer-service",
                "platform-parent",
                "shared-security"
            ],
            Edges:
            [
                new RepositoryDependencyEdge(
                    "account-service",
                    "platform-parent",
                    RepositoryDependencyType.Parent,
                    "1.0.0"),

                new RepositoryDependencyEdge(
                    "account-service",
                    "shared-security",
                    RepositoryDependencyType.Compile,
                    "1.0.0"),

                new RepositoryDependencyEdge(
                    "customer-service",
                    "platform-parent",
                    RepositoryDependencyType.Parent,
                    "1.0.0"),

                new RepositoryDependencyEdge(
                    "customer-service",
                    "shared-security",
                    RepositoryDependencyType.Compile,
                    "1.0.0"),

                new RepositoryDependencyEdge(
                    "shared-security",
                    "platform-parent",
                    RepositoryDependencyType.Parent,
                    "1.0.0")
            ]);

        var builder = new MigrationWaveBuilder();

        var result = builder.Build(graph);

        Assert.Equal(3, result.Waves.Count);

        Assert.Equal(
            ["platform-parent"],
            result.Waves[0].Repositories);

        Assert.Equal(
            ["shared-security"],
            result.Waves[1].Repositories);

        Assert.Equal(
            ["account-service", "customer-service"],
            result.Waves[2].Repositories);
    }

    [Fact]
    public void Build_NumbersWavesStartingAtOne()
    {
        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "platform-parent",
                "shared-security"
            ],
            Edges:
            [
                new RepositoryDependencyEdge(
                    "shared-security",
                    "platform-parent",
                    RepositoryDependencyType.Parent,
                    "1.0.0")
            ]);

        var builder = new MigrationWaveBuilder();

        var result = builder.Build(graph);

        Assert.Equal(1, result.Waves[0].Number);
        Assert.Equal(2, result.Waves[1].Number);
    }

    [Fact]
    public void Build_IndependentRepositoriesPlacesThemInSameWave()
    {
        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "account-service",
                "customer-service"
            ],
            Edges: []);

        var builder = new MigrationWaveBuilder();

        var result = builder.Build(graph);

        var wave = Assert.Single(result.Waves);

        Assert.Equal(1, wave.Number);

        Assert.Equal(
            ["account-service", "customer-service"],
            wave.Repositories);
    }

    [Fact]
    public void Build_CyclicDependencies_ThrowsInvalidOperationException()
    {
        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "service-a",
                "service-b"
            ],
            Edges:
            [
                new RepositoryDependencyEdge(
                    "service-a",
                    "service-b",
                    RepositoryDependencyType.Compile,
                    "1.0.0"),

                new RepositoryDependencyEdge(
                    "service-b",
                    "service-a",
                    RepositoryDependencyType.Compile,
                    "1.0.0")
            ]);

        var builder = new MigrationWaveBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(graph));

        Assert.Equal(
            "The repository dependency graph contains a cycle.",
            exception.Message);
    }

    [Fact]
    public void Build_MissingRepositoryInEdge_ThrowsInvalidOperationException()
    {
        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "customer-service"
            ],
            Edges:
            [
                new RepositoryDependencyEdge(
                    "customer-service",
                    "missing-library",
                    RepositoryDependencyType.Compile,
                    "1.0.0")
            ]);

        var builder = new MigrationWaveBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(graph));

        Assert.Contains(
            "missing-library",
            exception.Message);
    }
}
