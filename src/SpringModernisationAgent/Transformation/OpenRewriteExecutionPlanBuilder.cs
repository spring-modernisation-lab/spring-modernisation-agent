using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Transformation;

public sealed class OpenRewriteExecutionPlanBuilder
{
    public OpenRewriteExecutionPlan Build(
        TransformationPlan transformationPlan,
        string repositoryPath)
    {
        ArgumentNullException.ThrowIfNull(transformationPlan);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (transformationPlan.OpenRewrite.Count == 0)
        {
            throw new InvalidOperationException(
                $"Repository '{transformationPlan.RepositoryName}' has no OpenRewrite recipes to execute.");
        }

        var recipes = transformationPlan.OpenRewrite
            .Select(recipe => new OpenRewriteExecutionRecipe(
                Recipe: recipe.Recipe,
                Artifact: recipe.Artifact,
                Version: recipe.Version))
            .ToList();

        return new OpenRewriteExecutionPlan(
            RepositoryName: transformationPlan.RepositoryName,
            RepositoryPath: repositoryPath,
            SourceCommitSha: transformationPlan.SourceCommitSha,
            Recipes: recipes);
    }
}