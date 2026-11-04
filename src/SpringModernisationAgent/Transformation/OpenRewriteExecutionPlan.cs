namespace SpringModernisationAgent.Transformation;

public sealed record OpenRewriteExecutionPlan(
    string RepositoryName,
    string RepositoryPath,
    string SourceCommitSha,
    IReadOnlyList<OpenRewriteExecutionRecipe> Recipes);

/// <param name="Configuration">A pipeline recipe's file, relative to the Config folder; null for published recipes.</param>
public sealed record OpenRewriteExecutionRecipe(
    string Recipe,
    string Artifact,
    string Version,
    string? Configuration = null);
