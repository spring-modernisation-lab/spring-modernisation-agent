using System.Text.Json;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Transformation;

/// <summary>Pipeline-owned OpenRewrite recipes (Config/recipes), starting with T3.</summary>
public class PipelineRecipeTests
{
    private static string ConfigDirectory => Path.Combine(AppContext.BaseDirectory, "Config");

    [Fact]
    public void ApplicationRoute_RunsTheServletStringLiteralRecipeAfterTheBootAndJavaRecipes()
    {
        var policy = new SpringMigrationPolicyLoader().Load(Path.Combine(ConfigDirectory, "spring.yaml"));
        var route = policy.Routes.Single(candidate => candidate.Id == "spring-boot-application-2.7-to-3");

        var recipes = route.OpenRewrite.Recipes;

        Assert.Equal(
            [
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                "org.openrewrite.java.migrate.UpgradeToJava21",
                "lab.jakarta.ServletStringLiterals"
            ],
            recipes.Select(recipe => recipe.Recipe));

        var servlet = recipes[^1];
        Assert.Equal("recipes/lab-recipes.yml", servlet.Configuration);
        Assert.True(string.IsNullOrEmpty(servlet.Artifact) && string.IsNullOrEmpty(servlet.Version));

        new OpenRewriteConfigurationValidator().Validate(route.OpenRewrite);
    }

    [Fact]
    public void ShippedRecipeFile_DefinesTheRecipeFromTheCoreStringLiteralRename()
    {
        var text = File.ReadAllText(Path.Combine(ConfigDirectory, "recipes", "lab-recipes.yml"));

        Assert.Contains("name: lab.jakarta.ServletStringLiterals", text);
        Assert.Contains("- org.openrewrite.java.ChangePackageInStringLiteral:", text);
        Assert.Contains("oldPackageName: javax.servlet", text);
        Assert.Contains("newPackageName: jakarta.servlet", text);
    }

    [Fact]
    public void Validator_RejectsAPipelineRecipeThatAlsoNamesAnArtifact()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(new OpenRewriteRecipeConfiguration
        {
            Recipe = "lab.jakarta.ServletStringLiterals",
            Artifact = "org.openrewrite:rewrite-java",
            Version = "8.89.0",
            Configuration = "recipes/lab-recipes.yml"
        }));

        Assert.Contains("has a configuration file and an artifact", exception.Message);
    }

    [Theory]
    [InlineData("../secrets.yml")]
    [InlineData("/etc/rewrite.yml")]
    [InlineData("recipes\\lab-recipes.yml")]
    [InlineData("recipes//lab-recipes.yml")]
    public void Validator_RejectsAConfigurationOutsideOrAmbiguousWithinTheConfigFolder(
        string configuration)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Validate(new OpenRewriteRecipeConfiguration
        {
            Recipe = "lab.x",
            Configuration = configuration
        }));

        Assert.Contains("must be a relative path inside the Config folder", exception.Message);
    }

    [Fact]
    public void ApprovedPlans_KeepTheConfigurationThroughJson()
    {
        var recipe = new OpenRewriteTransformation("lab.jakarta.ServletStringLiterals", "", "")
        {
            Configuration = "recipes/lab-recipes.yml"
        };

        var json = JsonSerializer.Serialize(recipe);
        var back = JsonSerializer.Deserialize<OpenRewriteTransformation>(json)!;

        Assert.Equal("recipes/lab-recipes.yml", back.Configuration);

        // Plans written before pipeline recipes existed still read.
        var old = JsonSerializer.Deserialize<OpenRewriteTransformation>(
            """{"Recipe":"r","Artifact":"a","Version":"1"}""")!;

        Assert.Null(old.Configuration);
    }

    private static void Validate(
        OpenRewriteRecipeConfiguration recipe) =>
        new OpenRewriteConfigurationValidator().Validate(new OpenRewriteConfiguration { Recipes = [recipe] });
}
