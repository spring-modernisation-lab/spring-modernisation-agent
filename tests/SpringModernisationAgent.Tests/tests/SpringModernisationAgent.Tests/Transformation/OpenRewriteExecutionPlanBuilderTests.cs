using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class OpenRewriteExecutionPlanBuilderTests
{
    [Fact]
    public void Build_ValidTransformationPlan_CreatesExecutionPlan()
    {
        var transformationPlan = CreateTransformationPlan();
        var builder = new OpenRewriteExecutionPlanBuilder();

        var result = builder.Build(
            transformationPlan,
            "/workspace/customer-service");

        Assert.Equal(
            "customer-service",
            result.RepositoryName);

        Assert.Equal(
            "/workspace/customer-service",
            result.RepositoryPath);

        Assert.Equal(
            "abc123",
            result.SourceCommitSha);

        Assert.Equal(
            2,
            result.Recipes.Count);
    }

    [Fact]
    public void Build_CopiesRecipesInConfiguredOrder()
    {
        var transformationPlan = CreateTransformationPlan();
        var builder = new OpenRewriteExecutionPlanBuilder();

        var result = builder.Build(
            transformationPlan,
            "/workspace/customer-service");

        Assert.Equal(
            "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
            result.Recipes[0].Recipe);

        Assert.Equal(
            "org.openrewrite.recipe:rewrite-spring",
            result.Recipes[0].Artifact);

        Assert.Equal(
            "6.43.0",
            result.Recipes[0].Version);

        Assert.Equal(
            "org.openrewrite.java.migrate.UpgradeToJava21",
            result.Recipes[1].Recipe);

        Assert.Equal(
            "org.openrewrite.recipe:rewrite-migrate-java",
            result.Recipes[1].Artifact);

        Assert.Equal(
            "3.47.0",
            result.Recipes[1].Version);
    }

    [Fact]
    public void Build_NoRecipes_ThrowsInvalidOperationException()
    {
        var transformationPlan = CreateTransformationPlan() with
        {
            OpenRewrite = []
        };

        var builder = new OpenRewriteExecutionPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(
                transformationPlan,
                "/workspace/customer-service"));

        Assert.Equal(
            "Repository 'customer-service' has no OpenRewrite recipes to execute.",
            exception.Message);
    }

    [Fact]
    public void Build_EmptyRepositoryPath_ThrowsArgumentException()
    {
        var transformationPlan = CreateTransformationPlan();
        var builder = new OpenRewriteExecutionPlanBuilder();

        Assert.Throws<ArgumentException>(
            () => builder.Build(
                transformationPlan,
                string.Empty));
    }

    [Fact]
    public void Build_NullTransformationPlan_ThrowsArgumentNullException()
    {
        var builder = new OpenRewriteExecutionPlanBuilder();

        Assert.Throws<ArgumentNullException>(
            () => builder.Build(
                null!,
                "/workspace/customer-service"));
    }

    private static TransformationPlan CreateTransformationPlan()
    {
        return new TransformationPlan(
            RepositoryName: "customer-service",
            SourceCommitSha: "abc123",
            MigrationWave: 3,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget(
                SpringBootVersion: "3.5.x",
                JavaVersion: "21"),
            OpenRewrite:
            [
                new OpenRewriteTransformation(
                    Recipe:
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact:
                        "org.openrewrite.recipe:rewrite-spring",
                    Version: "6.43.0"),

                new OpenRewriteTransformation(
                    Recipe:
                        "org.openrewrite.java.migrate.UpgradeToJava21",
                    Artifact:
                        "org.openrewrite.recipe:rewrite-migrate-java",
                    Version: "3.47.0")
            ],
            Validation:
            [
                "compile",
                "unit-tests",
                "application-context-test"
            ]);
    }
}