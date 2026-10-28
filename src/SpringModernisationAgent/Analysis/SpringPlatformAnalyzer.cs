using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class SpringPlatformAnalyzer
{
    private const string SpringBootGroupId =
        "org.springframework.boot";

    private const string SpringBootParent =
        "spring-boot-starter-parent";

    private const string SpringBootStarterPrefix =
        "spring-boot-starter";

    public SpringPlatform Analyze(
        RepositoryInventory repository,
        IReadOnlyDictionary<string, RepositoryInventory>
            repositoriesByArtifact)
    {
        var bootVersion =
            FindSpringBootVersion(
                repository,
                repositoriesByArtifact,
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase));

        var javaVersion =
            FindProperty(
                repository,
                "java.version",
                repositoriesByArtifact,
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase));

        var starters =
            repository.MavenProject.Dependencies
                .Where(dependency =>
                    dependency.Coordinate.GroupId.Equals(
                        SpringBootGroupId,
                        StringComparison.OrdinalIgnoreCase))
                .Where(dependency =>
                    dependency.Coordinate.ArtifactId.StartsWith(
                        SpringBootStarterPrefix,
                        StringComparison.OrdinalIgnoreCase))
                .Select(dependency =>
                    dependency.Coordinate.ArtifactId)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();

        return new SpringPlatform(
            SpringBootVersion: bootVersion,
            JavaVersion: javaVersion,
            Starters: starters);
    }

    private static string? FindSpringBootVersion(
        RepositoryInventory repository,
        IReadOnlyDictionary<string, RepositoryInventory>
            repositoriesByArtifact,
        ISet<string> visited)
    {
        if (!visited.Add(repository.RepositoryName))
        {
            return null;
        }

        var parent = repository.MavenProject.Parent;

        if (parent is null)
        {
            return null;
        }

        // Direct Spring Boot parent.
        if (parent.GroupId.Equals(
                SpringBootGroupId,
                StringComparison.OrdinalIgnoreCase)
            &&
            parent.ArtifactId.Equals(
                SpringBootParent,
                StringComparison.OrdinalIgnoreCase))
        {
            return parent.Version;
        }

        // Is the parent produced by another repository?
        if (!repositoriesByArtifact.TryGetValue(
                parent.Key,
                out var parentRepository))
        {
            return null;
        }

        return FindSpringBootVersion(
            parentRepository,
            repositoriesByArtifact,
            visited);
    }

    private static string? FindProperty(
        RepositoryInventory repository,
        string propertyName,
        IReadOnlyDictionary<string, RepositoryInventory>
            repositoriesByArtifact,
        ISet<string> visited)
    {
        if (!visited.Add(repository.RepositoryName))
        {
            return null;
        }

        if (repository.MavenProject.Properties.TryGetValue(
                propertyName,
                out var value))
        {
            return value;
        }

        var parent = repository.MavenProject.Parent;

        if (parent is null)
        {
            return null;
        }

        if (!repositoriesByArtifact.TryGetValue(
                parent.Key,
                out var parentRepository))
        {
            return null;
        }

        return FindProperty(
            parentRepository,
            propertyName,
            repositoriesByArtifact,
            visited);
    }
}