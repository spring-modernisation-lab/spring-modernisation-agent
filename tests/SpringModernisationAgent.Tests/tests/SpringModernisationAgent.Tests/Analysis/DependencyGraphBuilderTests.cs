using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class DependencyGraphBuilderTests
{
    [Fact]
    public void Build_IdentifiesInternalRepositoryDependencies()
    {
        var platformParent = new RepositoryInventory(
            "platform-parent",
            "abc1234",
            new MavenProject(
                new MavenCoordinate(
                    "com.example.platform",
                    "platform-parent",
                    "1.0.0"),
                Parent: new MavenCoordinate(
                    "org.springframework.boot",
                    "spring-boot-starter-parent",
                    "2.7.18"),
                Packaging: "pom",
                Name: "Platform Parent",
                Properties: new Dictionary<string, string>(),
                Dependencies: []));

        var sharedSecurity = new RepositoryInventory(
            "shared-security",
            "abc1234",
            new MavenProject(
                new MavenCoordinate(
                    "com.example.platform",
                    "shared-security",
                    "1.0.0"),
                Parent: new MavenCoordinate(
                    "com.example.platform",
                    "platform-parent",
                    "1.0.0"),
                Packaging: "jar",
                Name: "Shared Security",
                Properties: new Dictionary<string, string>(),
                Dependencies: []));

        var customerService = new RepositoryInventory(
            "customer-service",
            "abc1234",
            new MavenProject(
                new MavenCoordinate(
                    "com.example.services",
                    "customer-service",
                    "1.0.0"),
                Parent: new MavenCoordinate(
                    "com.example.platform",
                    "platform-parent",
                    "1.0.0"),
                Packaging: "jar",
                Name: "Customer Service",
                Properties: new Dictionary<string, string>(),
                Dependencies:
                [
                    new MavenDependency(
                        new MavenCoordinate(
                            "com.example.platform",
                            "shared-security",
                            "1.0.0"),
                        Scope: "compile",
                        Optional: false),

                    new MavenDependency(
                        new MavenCoordinate(
                            "org.springframework.boot",
                            "spring-boot-starter-web",
                            null),
                        Scope: "compile",
                        Optional: false)
                ]));

        RepositoryInventory[] repositories =
        [
            platformParent,
            sharedSecurity,
            customerService
        ];

        var builder = new DependencyGraphBuilder();

        var graph = builder.Build(repositories);

        Assert.Contains(
            graph.Edges,
            edge =>
                edge.FromRepository == "customer-service" &&
                edge.ToRepository == "shared-security" &&
                edge.Type == RepositoryDependencyType.Compile);

        Assert.Contains(
            graph.Edges,
            edge =>
                edge.FromRepository == "customer-service" &&
                edge.ToRepository == "platform-parent" &&
                edge.Type == RepositoryDependencyType.Parent);
    }

    [Fact]
    public void Build_DoesNotTreatExternalDependenciesAsRepositories()
    {
        var customerService = new RepositoryInventory(
            "customer-service",
            "abc1234",
            new MavenProject(
                new MavenCoordinate(
                    "com.example.services",
                    "customer-service",
                    "1.0.0"),
                Parent: null,
                Packaging: "jar",
                Name: "Customer Service",
                Properties: new Dictionary<string, string>(),
                Dependencies:
                [
                    new MavenDependency(
                        new MavenCoordinate(
                            "org.springframework.boot",
                            "spring-boot-starter-web",
                            null),
                        Scope: "compile",
                        Optional: false)
                ]));

        var builder = new DependencyGraphBuilder();

        var graph = builder.Build([customerService]);

        Assert.Empty(graph.Edges);
    }
}
