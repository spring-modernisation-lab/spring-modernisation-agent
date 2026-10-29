using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class RepositoryClassifier
{
    public RepositoryKind Classify(MavenProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (string.Equals(project.Packaging, "pom", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryKind.ParentPom;
        }

        if (HasSpringBootApplicationCharacteristics(project))
        {
            return RepositoryKind.SpringBootApplication;
        }

        if (string.Equals(project.Packaging, "jar", StringComparison.OrdinalIgnoreCase))
        {
            return RepositoryKind.JavaLibrary;
        }

        return RepositoryKind.Unknown;
    }

    private static bool HasSpringBootApplicationCharacteristics(MavenProject project)
    {
        return project.Dependencies.Any(dependency =>
            dependency.Coordinate.GroupId == "org.springframework.boot" &&
            dependency.Coordinate.ArtifactId is
                "spring-boot-starter-web" or
                "spring-boot-starter-webflux");
    }
}
