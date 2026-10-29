namespace SpringModernisationAgent.Maven;

public sealed record MavenCoordinate(
    string GroupId,
    string ArtifactId,
    string? Version)
{
    public string Key => $"{GroupId}:{ArtifactId}";

    public override string ToString()
    {
        return Version is null
            ? Key
            : $"{Key}:{Version}";
    }
}

public sealed record MavenDependency(
    MavenCoordinate Coordinate,
    string Scope,
    bool Optional);

public sealed record MavenProject(
    MavenCoordinate Coordinate,
    MavenCoordinate? Parent,
    string Packaging,
    string? Name,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<MavenDependency> Dependencies);
