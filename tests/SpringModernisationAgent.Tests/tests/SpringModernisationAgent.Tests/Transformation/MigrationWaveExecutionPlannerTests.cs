using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class MigrationWaveExecutionPlannerTests
{
    [Fact]
    public void GetNextWave_EmptyPlans_ReturnsEmptyList()
    {
        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave([]);

        Assert.Empty(result);
    }

    [Fact]
    public void GetNextWave_NullPlans_ThrowsArgumentNullException()
    {
        var planner = new MigrationWaveExecutionPlanner();

        Assert.Throws<ArgumentNullException>(
            () => planner.GetNextWave(null!));
    }

    [Fact]
    public void GetNextWave_SelectsLowestWave()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "service-a",
                migrationWave: 3),

            CreatePlan(
                repositoryName: "shared-library",
                migrationWave: 2),

            CreatePlan(
                repositoryName: "foundation",
                migrationWave: 1)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        var plan = Assert.Single(result);

        Assert.Equal(
            "foundation",
            plan.RepositoryName);

        Assert.Equal(
            1,
            plan.MigrationWave);
    }

    [Fact]
    public void GetNextWave_PlansAreOutOfOrder_SelectsLowestWave()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "application-two",
                migrationWave: 5),

            CreatePlan(
                repositoryName: "foundation-two",
                migrationWave: 2),

            CreatePlan(
                repositoryName: "application-one",
                migrationWave: 4),

            CreatePlan(
                repositoryName: "foundation-one",
                migrationWave: 2),

            CreatePlan(
                repositoryName: "intermediate",
                migrationWave: 3)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        Assert.Equal(
            2,
            result.Count);

        Assert.All(
            result,
            plan => Assert.Equal(
                2,
                plan.MigrationWave));
    }

    [Fact]
    public void GetNextWave_MultipleRepositoriesInLowestWave_ReturnsAllRepositories()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "foundation-alpha",
                migrationWave: 1),

            CreatePlan(
                repositoryName: "foundation-beta",
                migrationWave: 1),

            CreatePlan(
                repositoryName: "application-service",
                migrationWave: 2)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        Assert.Equal(
            2,
            result.Count);

        Assert.Contains(
            result,
            plan => plan.RepositoryName == "foundation-alpha");

        Assert.Contains(
            result,
            plan => plan.RepositoryName == "foundation-beta");

        Assert.DoesNotContain(
            result,
            plan => plan.RepositoryName == "application-service");
    }

    [Fact]
    public void GetNextWave_MultipleRepositories_ReturnsDeterministicRepositoryOrder()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "zeta-foundation",
                migrationWave: 1),

            CreatePlan(
                repositoryName: "Alpha-foundation",
                migrationWave: 1),

            CreatePlan(
                repositoryName: "middle-foundation",
                migrationWave: 1),

            CreatePlan(
                repositoryName: "later-service",
                migrationWave: 2)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        Assert.Equal(
            [
                "Alpha-foundation",
                "middle-foundation",
                "zeta-foundation"
            ],
            result.Select(
                plan => plan.RepositoryName));
    }

    [Fact]
    public void GetNextWave_DoesNotDependOnRepositoryKind()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "first-application",
                migrationWave: 1,
                repositoryKind:
                    RepositoryKind.SpringBootApplication),

            CreatePlan(
                repositoryName: "later-parent",
                migrationWave: 2,
                repositoryKind:
                    RepositoryKind.ParentPom)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        var plan = Assert.Single(result);

        Assert.Equal(
            "first-application",
            plan.RepositoryName);

        Assert.Equal(
            RepositoryKind.SpringBootApplication,
            plan.RepositoryKind);
    }

    [Fact]
    public void GetNextWave_DoesNotDependOnSpecificRepositoryNames()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "some-company-bom",
                migrationWave: 4),

            CreatePlan(
                repositoryName: "common-runtime",
                migrationWave: 4),

            CreatePlan(
                repositoryName: "order-processing",
                migrationWave: 7)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        Assert.Equal(
            [
                "common-runtime",
                "some-company-bom"
            ],
            result.Select(
                plan => plan.RepositoryName));

        Assert.All(
            result,
            plan => Assert.Equal(
                4,
                plan.MigrationWave));
    }

    [Fact]
    public void GetNextWave_WaveNumbersDoNotStartAtOne_SelectsLowestAvailableWave()
    {
        var plans = new[]
        {
            CreatePlan(
                repositoryName: "service-three",
                migrationWave: 8),

            CreatePlan(
                repositoryName: "foundation",
                migrationWave: 5),

            CreatePlan(
                repositoryName: "service-two",
                migrationWave: 7)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(plans);

        var plan = Assert.Single(result);

        Assert.Equal(
            5,
            plan.MigrationWave);

        Assert.Equal(
            "foundation",
            plan.RepositoryName);
    }

    [Fact]
    public void GetNextWave_NoExecutionResults_ReturnsLowestWave()
    {
        var plans = new[]
        {
            CreatePlan("application", 3),
            CreatePlan("shared-component", 2),
            CreatePlan("foundation", 1)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            plans,
            []);

        var plan = Assert.Single(result);

        Assert.Equal(
            "foundation",
            plan.RepositoryName);

        Assert.Equal(
            1,
            plan.MigrationWave);
    }

    [Fact]
    public void GetNextWave_FirstWavePassed_ReturnsSecondWave()
    {
        var firstPlan = CreatePlan(
            "foundation",
            1);

        var secondPlan = CreatePlan(
            "shared-component",
            2);

        var thirdPlan = CreatePlan(
            "application",
            3);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                thirdPlan,
                firstPlan,
                secondPlan
            ],
            [
                CreateSuccessfulResult(firstPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            "shared-component",
            plan.RepositoryName);

        Assert.Equal(
            2,
            plan.MigrationWave);
    }

    [Fact]
    public void GetNextWave_PreviousWavesPassed_ReturnsLowestIncompleteWave()
    {
        var waveOnePlan = CreatePlan(
            "foundation",
            1);

        var waveTwoPlan = CreatePlan(
            "shared-component",
            2);

        var waveThreePlan = CreatePlan(
            "application",
            3);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                waveThreePlan,
                waveOnePlan,
                waveTwoPlan
            ],
            [
                CreateSuccessfulResult(waveOnePlan),
                CreateSuccessfulResult(waveTwoPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            "application",
            plan.RepositoryName);

        Assert.Equal(
            3,
            plan.MigrationWave);
    }

    [Fact]
    public void GetNextWave_MultipleRepositoriesInCompletedWave_AllMustPass()
    {
        var firstPlan = CreatePlan(
            "foundation-alpha",
            1);

        var secondPlan = CreatePlan(
            "foundation-beta",
            1);

        var nextPlan = CreatePlan(
            "application",
            2);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                firstPlan,
                secondPlan,
                nextPlan
            ],
            [
                CreateSuccessfulResult(firstPlan),
                CreateSuccessfulResult(secondPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            "application",
            plan.RepositoryName);

        Assert.Equal(
            2,
            plan.MigrationWave);
    }

    [Fact]
    public void GetNextWave_WavePartiallyCompleted_ReturnsSameWave()
    {
        var firstPlan = CreatePlan(
            "foundation-alpha",
            1);

        var secondPlan = CreatePlan(
            "foundation-beta",
            1);

        var laterPlan = CreatePlan(
            "application",
            2);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                firstPlan,
                secondPlan,
                laterPlan
            ],
            [
                CreateSuccessfulResult(firstPlan)
            ]);

        Assert.Equal(
            2,
            result.Count);

        Assert.Equal(
            [
                "foundation-alpha",
            "foundation-beta"
            ],
            result.Select(
                plan => plan.RepositoryName));

        Assert.All(
            result,
            plan => Assert.Equal(
                1,
                plan.MigrationWave));
    }

    [Fact]
    public void GetNextWave_FirstWaveFailed_DoesNotAdvance()
    {
        var firstPlan = CreatePlan(
            "foundation",
            1);

        var secondPlan = CreatePlan(
            "application",
            2);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                firstPlan,
                secondPlan
            ],
            [
                CreateFailedResult(firstPlan)
            ]);

        Assert.Empty(result);
    }

    [Fact]
    public void GetNextWave_OneRepositoryInWaveFailed_DoesNotAdvance()
    {
        var firstPlan = CreatePlan(
            "foundation-alpha",
            1);

        var secondPlan = CreatePlan(
            "foundation-beta",
            1);

        var laterPlan = CreatePlan(
            "application",
            2);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                firstPlan,
                secondPlan,
                laterPlan
            ],
            [
                CreateSuccessfulResult(firstPlan),
                CreateFailedResult(secondPlan)
            ]);

        Assert.Empty(result);
    }

    [Fact]
    public void GetNextWave_AllWavesPassed_ReturnsEmptyList()
    {
        var firstPlan = CreatePlan(
            "foundation",
            1);

        var secondPlan = CreatePlan(
            "shared-component",
            2);

        var applicationOne = CreatePlan(
            "application-one",
            3);

        var applicationTwo = CreatePlan(
            "application-two",
            3);

        var plans = new[]
        {
            firstPlan,
            secondPlan,
            applicationOne,
            applicationTwo
        };

        var results = new[]
        {
            CreateSuccessfulResult(firstPlan),
            CreateSuccessfulResult(secondPlan),
            CreateSuccessfulResult(applicationOne),
            CreateSuccessfulResult(applicationTwo)
        };

        var planner = new MigrationWaveExecutionPlanner();

        var nextWave = planner.GetNextWave(
            plans,
            results);

        Assert.Empty(nextWave);
    }

    [Fact]
    public void GetNextWave_ResultCommitDoesNotMatchPlan_ThrowsInvalidOperationException()
    {
        var firstPlan = CreatePlan(
            "foundation",
            1);

        var result = CreateSuccessfulResult(
            firstPlan,
            sourceCommitSha:
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var planner = new MigrationWaveExecutionPlanner();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => planner.GetNextWave(
                    [firstPlan],
                    [result]));

        Assert.Contains(
            "does not match transformation plan source commit",
            exception.Message);
    }

    [Fact]
    public void GetNextWave_DuplicateExecutionResults_ThrowsArgumentException()
    {
        var plan = CreatePlan(
            "foundation",
            1);

        var result = CreateSuccessfulResult(
            plan);

        var planner = new MigrationWaveExecutionPlanner();

        Assert.Throws<ArgumentException>(
            () => planner.GetNextWave(
                [plan],
                [
                    result,
                result
                ]));
    }

    [Fact]
    public void GetNextWave_NullExecutionResults_ThrowsArgumentNullException()
    {
        var planner = new MigrationWaveExecutionPlanner();

        Assert.Throws<ArgumentNullException>(
            () => planner.GetNextWave(
                [
                    CreatePlan(
                    "foundation",
                    1)
                ],
                null!));
    }

    [Fact]
    public void GetNextWave_WaveNumbersDoNotStartAtOne_AdvancesCorrectly()
    {
        var firstPlan = CreatePlan(
            "foundation",
            5);

        var secondPlan = CreatePlan(
            "shared-component",
            8);

        var planner = new MigrationWaveExecutionPlanner();

        var result = planner.GetNextWave(
            [
                firstPlan,
            secondPlan
            ],
            [
                CreateSuccessfulResult(
                firstPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            8,
            plan.MigrationWave);

        Assert.Equal(
            "shared-component",
            plan.RepositoryName);
    }

    private static TransformationExecutionResult CreateSuccessfulResult(
    TransformationPlan plan,
    string? sourceCommitSha = null)
    {
        var commitSha =
            sourceCommitSha ?? plan.SourceCommitSha;

        return new TransformationExecutionResult(
            Transformation:
                new TransformationResult(
                    RepositoryName:
                        plan.RepositoryName,
                    SourceCommitSha:
                        commitSha,
                    Status:
                        TransformationStatus.Succeeded,
                    AppliedRecipes:
                        plan.OpenRewrite
                            .Select(recipe => recipe.Recipe)
                            .ToList(),
                    ChangedFiles: [],
                    StandardOutput: null,
                    StandardError: null),

            Validation:
                new TransformationValidationResult(
                    RepositoryName:
                        plan.RepositoryName,
                    SourceCommitSha:
                        commitSha,
                    Status:
                        TransformationValidationStatus.Passed,
                    Checks: []));
    }

    private static TransformationExecutionResult CreateFailedResult(
        TransformationPlan plan)
    {
        return new TransformationExecutionResult(
            Transformation:
                new TransformationResult(
                    RepositoryName:
                        plan.RepositoryName,
                    SourceCommitSha:
                        plan.SourceCommitSha,
                    Status:
                        TransformationStatus.Succeeded,
                    AppliedRecipes:
                        plan.OpenRewrite
                            .Select(recipe => recipe.Recipe)
                            .ToList(),
                    ChangedFiles: [],
                    StandardOutput: null,
                    StandardError: null),

            Validation:
                new TransformationValidationResult(
                    RepositoryName:
                        plan.RepositoryName,
                    SourceCommitSha:
                        plan.SourceCommitSha,
                    Status:
                        TransformationValidationStatus.Failed,
                    Checks:
                    [
                        new TransformationValidationCheckResult(
                        Check: "compile",
                        Passed: false,
                        StandardOutput: null,
                        StandardError:
                            "Validation failed.")
                    ]));
    }

    private static TransformationPlan CreatePlan(
        string repositoryName,
        int migrationWave,
        RepositoryKind repositoryKind =
            RepositoryKind.SpringBootApplication)
    {
        return new TransformationPlan(
            RepositoryName: repositoryName,
            SourceCommitSha:
                "0123456789abcdef0123456789abcdef01234567",
            MigrationWave: migrationWave,
            RepositoryKind: repositoryKind,
            MigrationRouteId:
                "test-migration-route",
            Target:
                new TransformationTarget(
                    SpringBootVersion: "3.5.x",
                    JavaVersion: "21"),
            OpenRewrite:
            [
                new OpenRewriteTransformation(
                    Recipe:
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact:
                        "org.openrewrite.recipe:rewrite-spring",
                    Version: "6.43.0")
            ],
            Validation:
            [
                "compile"
            ]);
    }
}