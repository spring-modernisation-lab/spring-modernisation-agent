using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;

namespace SpringModernisationAgent.Tests.Analysis;

public class OpenRewriteConfigurationValidatorTests
{
    [Fact]
    public void Validate_ValidConfiguration_DoesNotThrow()
    {
        var configuration = CreateValidConfiguration();
        var validator = new OpenRewriteConfigurationValidator();

        var exception = Record.Exception(
            () => validator.Validate(configuration));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_NoRecipes_ThrowsInvalidOperationException()
    {
        var configuration = new OpenRewriteConfiguration
        {
            Recipes = []
        };

        var validator = new OpenRewriteConfigurationValidator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(configuration));

        Assert.Equal(
            "At least one OpenRewrite recipe must be configured.",
            exception.Message);
    }

    [Fact]
    public void Validate_MissingRecipeName_ThrowsInvalidOperationException()
    {
        var configuration = CreateValidConfiguration();

        configuration.Recipes[0].Recipe = string.Empty;

        var validator = new OpenRewriteConfigurationValidator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(configuration));

        Assert.Equal(
            "OpenRewrite recipe 1 has no recipe name.",
            exception.Message);
    }

    [Fact]
    public void Validate_MissingArtifact_ThrowsInvalidOperationException()
    {
        var configuration = CreateValidConfiguration();

        configuration.Recipes[0].Artifact = string.Empty;

        var validator = new OpenRewriteConfigurationValidator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(configuration));

        Assert.Equal(
            "OpenRewrite recipe 1 has no artifact.",
            exception.Message);
    }

    [Fact]
    public void Validate_MissingVersion_ThrowsInvalidOperationException()
    {
        var configuration = CreateValidConfiguration();

        configuration.Recipes[0].Version = string.Empty;

        var validator = new OpenRewriteConfigurationValidator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(configuration));

        Assert.Equal(
            "OpenRewrite recipe 1 has no pinned version.",
            exception.Message);
    }

    [Fact]
    public void Validate_InvalidSecondRecipe_IdentifiesRecipeNumber()
    {
        var configuration = CreateValidConfiguration();

        configuration.Recipes[1].Version = string.Empty;

        var validator = new OpenRewriteConfigurationValidator();

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate(configuration));

        Assert.Equal(
            "OpenRewrite recipe 2 has no pinned version.",
            exception.Message);
    }

    private static OpenRewriteConfiguration CreateValidConfiguration()
    {
        return new OpenRewriteConfiguration
        {
            Recipes =
            [
                new OpenRewriteRecipeConfiguration
                {
                    Recipe =
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact =
                        "org.openrewrite.recipe:rewrite-spring",
                    Version = "6.43.0"
                },

                new OpenRewriteRecipeConfiguration
                {
                    Recipe =
                        "org.openrewrite.java.migrate.UpgradeToJava21",
                    Artifact =
                        "org.openrewrite.recipe:rewrite-migrate-java",
                    Version = "3.47.0"
                }
            ]
        };
    }
}