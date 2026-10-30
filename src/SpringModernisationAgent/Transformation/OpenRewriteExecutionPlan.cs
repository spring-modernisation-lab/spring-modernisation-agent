namespace SpringModernisationAgent.Transformation;

public sealed record OpenRewriteExecutionPlan(
    string RepositoryName,
    string RepositoryPath,
    string SourceCommitSha,
    IReadOnlyList<OpenRewriteExecutionRecipe> Recipes);

public sealed record OpenRewriteExecutionRecipe(
    string Recipe,
    string Artifact,
    string Version);