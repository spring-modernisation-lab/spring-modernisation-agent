using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class ProjectAssessmentBuilderTests
{
    [Fact]
    public void Build_CreatesProjectAssessment()
    {
        var mavenProject = new MavenProject(
            Coordinate: new MavenCoordinate(
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
            Dependencies: []);

        var repositoryAssessment = new RepositoryAssessment(
            RepositoryName: "customer-service",
            CommitSha: "abc1234",
            Kind: RepositoryKind.SpringBootApplication,
            MavenProject: mavenProject,
            Spring: new SpringPlatform(
                SpringBootVersion: "2.7.18",
                JavaVersion: "11",
                Starters: ["spring-boot-starter-web"]),
            InternalDependencies: [],
            ExternalDependencies: []);

        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "customer-service"
            ],
            Edges: []);

        var builder = new ProjectAssessmentBuilder(new MigrationWaveBuilder());

        var result = builder.Build(
            organisation: "spring-modernisation-lab",
            repositories:
            [
                repositoryAssessment
            ],
            dependencyGraph: graph);

        Assert.Equal(
            "spring-modernisation-lab",
            result.Organisation);

        Assert.Single(result.Repositories);

        Assert.Equal(
            "customer-service",
            result.Repositories[0].RepositoryName);

        Assert.Equal(
            "2.7.18",
            result.Repositories[0].Spring.SpringBootVersion);

        Assert.Equal(
            "11",
            result.Repositories[0].Spring.JavaVersion);

        Assert.Same(
            graph,
            result.DependencyGraph);
    }

    [Fact]
    public void Build_OrdersRepositoriesByName()
    {
        static RepositoryAssessment CreateAssessment(string name)
        {
            return new RepositoryAssessment(
                RepositoryName: name,
                CommitSha: "abc1234",
                Kind: RepositoryKind.JavaLibrary,
                MavenProject: new MavenProject(
                    Coordinate: new MavenCoordinate(
                        "com.example",
                        name,
                        "1.0.0"),
                    Parent: null,
                    Packaging: "jar",
                    Name: name,
                    Properties: new Dictionary<string, string>(),
                    Dependencies: []),
                Spring: new SpringPlatform(
                    SpringBootVersion: null,
                    JavaVersion: null,
                    Starters: []),
                InternalDependencies: [],
                ExternalDependencies: []);
        }

        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "shared-security",
                "account-service"
            ],
            Edges: []);

        var builder = new ProjectAssessmentBuilder(new MigrationWaveBuilder());

        var result = builder.Build(
            "spring-modernisation-lab",
            [
                CreateAssessment("shared-security"),
                CreateAssessment("account-service")
            ],
            graph);

        Assert.Equal(
            [
                "account-service",
                "shared-security"
            ],
            result.Repositories
                .Select(repository => repository.RepositoryName)
                .ToArray());
    }
}
