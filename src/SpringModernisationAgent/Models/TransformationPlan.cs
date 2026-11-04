namespace SpringModernisationAgent.Models;

public sealed record TransformationPlan(
    string RepositoryName,
    string SourceCommitSha,
    int MigrationWave,
    RepositoryKind RepositoryKind,
    string MigrationRouteId,
    TransformationTarget Target,
    IReadOnlyList<OpenRewriteTransformation> OpenRewrite,
    IReadOnlyList<string> Validation);

public sealed record TransformationTarget(
    string SpringBootVersion,
    string JavaVersion);

public sealed record OpenRewriteTransformation(
    string Recipe,
    string Artifact,
    string Version)
{
    /// <summary>A pipeline recipe's file, relative to the Config folder; null for published recipes.</summary>
    public string? Configuration { get; init; }
}
