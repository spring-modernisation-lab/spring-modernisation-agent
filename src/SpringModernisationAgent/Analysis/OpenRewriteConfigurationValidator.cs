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

            if (!string.IsNullOrWhiteSpace(recipe.Configuration))
            {
                // A pipeline recipe: pinned by the plugin version, so an
                // artifact here would be a second, conflicting pin.
                if (!string.IsNullOrWhiteSpace(recipe.Artifact) || !string.IsNullOrWhiteSpace(recipe.Version))
                {
                    throw new InvalidOperationException(
                        $"OpenRewrite recipe {index + 1} ('{recipe.Recipe}') has a configuration file and an " +
                        "artifact; a pipeline recipe uses only the plugin's core recipes and has no artifact.");
                }

                if (!IsSafeRelativePath(recipe.Configuration))
                {
                    throw new InvalidOperationException(
                        $"OpenRewrite recipe {index + 1} ('{recipe.Recipe}') configuration " +
                        $"'{recipe.Configuration}' must be a relative path inside the Config folder.");
                }

                continue;
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

    internal static bool IsSafeRelativePath(
        string path) =>
        !Path.IsPathRooted(path)
        && !path.Contains('\\')
        && path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
}
