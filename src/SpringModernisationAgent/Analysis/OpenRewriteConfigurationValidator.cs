using SpringModernisationAgent.Config;

namespace SpringModernisationAgent.Analysis;

public sealed class OpenRewriteConfigurationValidator
{
    public void Validate(OpenRewriteConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Recipes.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one OpenRewrite recipe must be configured.");
        }

        for (var index = 0; index < configuration.Recipes.Count; index++)
        {
            var recipe = configuration.Recipes[index];

            if (string.IsNullOrWhiteSpace(recipe.Recipe))
            {
                throw new InvalidOperationException(
                    $"OpenRewrite recipe {index + 1} has no recipe name.");
            }

            if (string.IsNullOrWhiteSpace(recipe.Artifact))
            {
                throw new InvalidOperationException(
                    $"OpenRewrite recipe {index + 1} has no artifact.");
            }

            if (string.IsNullOrWhiteSpace(recipe.Version))
            {
                throw new InvalidOperationException(
                    $"OpenRewrite recipe {index + 1} has no pinned version.");
            }
        }
    }
}