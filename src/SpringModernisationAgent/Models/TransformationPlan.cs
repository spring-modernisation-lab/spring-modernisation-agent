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
    string Version);
