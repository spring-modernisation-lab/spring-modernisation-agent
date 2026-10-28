using System.Text.Json;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Serialization;

public class ProjectAssessmentSerializationTests
{
    [Fact]
    public void Serialize_ProducesExpectedProjectAssessmentJson()
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
            Dependencies:
            [
                new MavenDependency(
                    new MavenCoordinate(
                        "org.springframework.boot",
                        "spring-boot-starter-web",
                        null),
                    Scope: "compile",
                    Optional: false)
            ]);

        var repositoryAssessment = new RepositoryAssessment(
            RepositoryName: "customer-service",
            MavenProject: mavenProject,
            Spring: new SpringPlatform(
                SpringBootVersion: "2.7.18",
                JavaVersion: "11",
                Starters:
                [
                    "spring-boot-starter-web"
                ]),
            InternalDependencies:
            [
                new RepositoryDependencyEdge(
                    FromRepository: "customer-service",
                    ToRepository: "platform-parent",
                    Type: RepositoryDependencyType.Parent,
                    Version: "1.0.0")
            ],
            ExternalDependencies: mavenProject.Dependencies);

        var graph = new RepositoryDependencyGraph(
            Repositories:
            [
                "customer-service",
                "platform-parent"
            ],
            Edges:
            [
                new RepositoryDependencyEdge(
                    FromRepository: "customer-service",
                    ToRepository: "platform-parent",
                    Type: RepositoryDependencyType.Parent,
                    Version: "1.0.0")
            ]);

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: new DateTimeOffset(
                2026, 10, 28,
                12, 0, 0,
                TimeSpan.Zero),
            Repositories:
            [
                repositoryAssessment
            ],
            DependencyGraph: graph);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(
            assessment,
            options);

        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        Assert.Equal(
            "spring-modernisation-lab",
            root.GetProperty("Organisation").GetString());

        Assert.Equal(
            1,
            root.GetProperty("Repositories").GetArrayLength());

        var repository =
            root.GetProperty("Repositories")[0];

        Assert.Equal(
            "customer-service",
            repository
                .GetProperty("RepositoryName")
                .GetString());

        Assert.Equal(
            "2.7.18",
            repository
                .GetProperty("Spring")
                .GetProperty("SpringBootVersion")
                .GetString());

        Assert.Equal(
            "11",
            repository
                .GetProperty("Spring")
                .GetProperty("JavaVersion")
                .GetString());

        Assert.Equal(
            "spring-boot-starter-web",
            repository
                .GetProperty("Spring")
                .GetProperty("Starters")[0]
                .GetString());

        var dependencyGraph =
            root.GetProperty("DependencyGraph");

        Assert.Equal(
            1,
            dependencyGraph
                .GetProperty("Edges")
                .GetArrayLength());

        var edge =
            dependencyGraph.GetProperty("Edges")[0];

        Assert.Equal(
            "customer-service",
            edge.GetProperty("FromRepository").GetString());

        Assert.Equal(
            "platform-parent",
            edge.GetProperty("ToRepository").GetString());
    }
}