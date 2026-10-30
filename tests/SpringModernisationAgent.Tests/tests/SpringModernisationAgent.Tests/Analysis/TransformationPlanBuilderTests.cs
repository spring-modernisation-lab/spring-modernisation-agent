using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class TransformationPlanBuilderTests
{
    [Fact]
    public void Build_ReadyRepository_CreatesTransformationPlan()
    {
        var repository = CreateRepositoryAssessment(
            readiness: Ready());

        var waves = new MigrationWavePlan(
        [
            new MigrationWave(
                Number: 3,
                Repositories: ["customer-service"])
        ]);

        var builder = new TransformationPlanBuilder();

        var result = builder.Build(repository, waves);

        Assert.Equal("customer-service", result.RepositoryName);
        Assert.Equal("abc123", result.SourceCommitSha);
        Assert.Equal(3, result.MigrationWave);

        Assert.Equal(
            RepositoryKind.SpringBootApplication,
            result.RepositoryKind);

        Assert.Equal(
            "spring-boot-application-2.7-to-3",
            result.MigrationRouteId);

        Assert.Equal(
            "3.5.x",
            result.Target.SpringBootVersion);

        Assert.Equal(
            "21",
            result.Target.JavaVersion);

        Assert.Collection(
            result.OpenRewrite,
            recipe =>
            {
                Assert.Equal(
                    "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    recipe.Recipe);
                Assert.Equal(
                    "org.openrewrite.recipe:rewrite-spring",
                    recipe.Artifact);
                Assert.Equal(
                    "6.43.0",
                    recipe.Version);
            },
            recipe =>
            {
                Assert.Equal(
                    "org.openrewrite.java.migrate.UpgradeToJava21",
                    recipe.Recipe);
                Assert.Equal(
                    "org.openrewrite.recipe:rewrite-migrate-java",
                    recipe.Artifact);
                Assert.Equal(
                    "3.47.0",
                    recipe.Version);
            });

        Assert.Equal(
            ["compile", "unit-tests", "application-context-test"],
            result.Validation);
    }

    [Fact]
    public void Build_BlockedRepository_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: new MigrationReadiness(
                MigrationReadinessStatus.Blocked,
                ["The baseline build failed."]));

        var waves = CreateMigrationWaves();

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "not ready for transformation",
            exception.Message);

        Assert.Contains(
            "The baseline build failed.",
            exception.Message);
    }

    [Fact]
    public void Build_ReadinessNotEvaluated_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: null);

        var waves = CreateMigrationWaves();

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "Migration readiness has not been established.",
            exception.Message);
    }

    [Fact]
    public void Build_MigrationRouteMissing_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: Ready()) with
        {
            MigrationRoute = null
        };

        var waves = CreateMigrationWaves();

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "has no approved migration route",
            exception.Message);
    }

    [Fact]
    public void Build_SourceCommitMissing_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: Ready()) with
        {
            CommitSha = string.Empty
        };

        var waves = CreateMigrationWaves();

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "has no source commit SHA",
            exception.Message);
    }

    [Fact]
    public void Build_RepositoryNotInMigrationWave_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: Ready());

        var waves = new MigrationWavePlan(
        [
            new MigrationWave(
                Number: 1,
                Repositories: ["platform-parent"])
        ]);

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "does not belong to a migration wave",
            exception.Message);
    }

    [Fact]
    public void Build_RepositoryInMultipleMigrationWaves_ThrowsInvalidOperationException()
    {
        var repository = CreateRepositoryAssessment(
            readiness: Ready());

        var waves = new MigrationWavePlan(
        [
            new MigrationWave(
                Number: 2,
                Repositories: ["customer-service"]),

            new MigrationWave(
                Number: 3,
                Repositories: ["customer-service"])
        ]);

        var builder = new TransformationPlanBuilder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.Build(repository, waves));

        Assert.Contains(
            "belongs to multiple migration waves",
            exception.Message);
    }

    private static MigrationWavePlan CreateMigrationWaves()
    {
        return new MigrationWavePlan(
        [
            new MigrationWave(
                Number: 3,
                Repositories: ["customer-service"])
        ]);
    }

    private static MigrationReadiness Ready()
    {
        return new MigrationReadiness(
            Status: MigrationReadinessStatus.Ready,
            Reasons: []);
    }

    private static RepositoryAssessment CreateRepositoryAssessment(
        MigrationReadiness? readiness)
    {
        var route = new SpringMigrationRoute
        {
            Id = "spring-boot-application-2.7-to-3",

            Source = new SpringSourceCriteria
            {
                RepositoryKinds =
                [
                    RepositoryKind.SpringBootApplication
                ],
                SpringBootMinimum = "2.7.0",
                SpringBootMaximum = "2.7.x"
            },

            Target = new SpringTarget
            {
                SpringBoot = "3.5.x",
                Java = "21"
            },

            OpenRewrite = new OpenRewriteConfiguration
            {
                Recipes =
                [
                    new OpenRewriteRecipeConfiguration
                    {
                        Recipe = "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                        Artifact = "org.openrewrite.recipe:rewrite-spring",
                        Version = "6.43.0"
                    },
                    new OpenRewriteRecipeConfiguration
                    {
                        Recipe = "org.openrewrite.java.migrate.UpgradeToJava21",
                        Artifact = "org.openrewrite.recipe:rewrite-migrate-java",
                        Version = "3.47.0"
                    }
                ]
            },

            Requirements = new MigrationRequirements
            {
                BuildSystems = ["maven"],
                BaselineBuildMustPass = true
            },

            Validation =
            [
                "compile",
                "unit-tests",
                "application-context-test"
            ]
        };

        return new RepositoryAssessment(
            RepositoryName: "customer-service",
            CommitSha: "abc123",
            Kind: RepositoryKind.SpringBootApplication,

            MavenProject: new MavenProject(
                Coordinate: new MavenCoordinate(
                    "com.example.services",
                    "customer-service",
                    "1.0.0"),
                Parent: null,
                Packaging: "jar",
                Name: "Customer Service",
                Properties: new Dictionary<string, string>(),
                Dependencies: []),

            Spring: new SpringPlatform(
                SpringBootVersion: "2.7.18",
                JavaVersion: "11",
                Starters: ["spring-boot-starter-web"]),

            InternalDependencies: [],
            ExternalDependencies: [],

            MigrationRoute: new MigrationRouteSelection(
                Status: MigrationRouteSelectionStatus.Matched,
                Route: route,
                Reason: null),

            BaselineBuild: null,
            BaselineBuildValidation: null,
            CommitProvenance: null,
            MigrationReadiness: readiness);
    }
}
