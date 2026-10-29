using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class RepositoryClassifierTests
{
    [Fact]
    public void Classify_PomPackaging_ReturnsParentPom()
    {
        var project = CreateProject(
            packaging: "pom",
            dependencies: []);

        var classifier = new RepositoryClassifier();

        var result = classifier.Classify(project);

        Assert.Equal(RepositoryKind.ParentPom, result);
    }

    [Fact]
    public void Classify_JarWithSpringWebStarter_ReturnsSpringBootApplication()
    {
        var project = CreateProject(
            packaging: "jar",
            dependencies:
            [
                CreateDependency(
                    "org.springframework.boot",
                    "spring-boot-starter-web")
            ]);

        var classifier = new RepositoryClassifier();

        var result = classifier.Classify(project);

        Assert.Equal(RepositoryKind.SpringBootApplication, result);
    }

    [Fact]
    public void Classify_JarWithSpringSecurityStarter_ReturnsJavaLibrary()
    {
        var project = CreateProject(
            packaging: "jar",
            dependencies:
            [
                CreateDependency(
                    "org.springframework.boot",
                    "spring-boot-starter-security")
            ]);

        var classifier = new RepositoryClassifier();

        var result = classifier.Classify(project);

        Assert.Equal(RepositoryKind.JavaLibrary, result);
    }

    private static MavenProject CreateProject(
        string packaging,
        IReadOnlyList<MavenDependency> dependencies)
    {
        return new MavenProject(
            Coordinate: new MavenCoordinate(
                "com.example",
                "example",
                "1.0.0"),
            Parent: null,
            Packaging: packaging,
            Name: "Example",
            Properties: new Dictionary<string, string>(),
            Dependencies: dependencies);
    }

    private static MavenDependency CreateDependency(
        string groupId,
        string artifactId)
    {
        return new MavenDependency(
            Coordinate: new MavenCoordinate(
                groupId,
                artifactId,
                null),
            Scope: "compile",
            Optional: false);
    }
}
