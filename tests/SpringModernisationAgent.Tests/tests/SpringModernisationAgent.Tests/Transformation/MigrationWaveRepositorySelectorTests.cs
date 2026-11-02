using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class MigrationWaveRepositorySelectorTests
{
    private const string CommitSha =
        "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void GetRepositoriesToExecute_NoPreviousResults_SelectsAllRepositories()
    {
        var plans = new[]
        {
            CreatePlan("repository-b", 2),
            CreatePlan("repository-a", 2)
        };

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            plans,
            []);

        Assert.Equal(
            [
                "repository-a",
                "repository-b"
            ],
            result.Select(plan => plan.RepositoryName));
    }

    [Fact]
    public void GetRepositoriesToExecute_SuccessfulRepository_SkipsRepository()
    {
        var firstPlan = CreatePlan(
            "repository-a",
            2);

        var secondPlan = CreatePlan(
            "repository-b",
            2);

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            [
                firstPlan,
                secondPlan
            ],
            [
                CreateSuccessfulResult(firstPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            "repository-b",
            plan.RepositoryName);
    }

    [Fact]
    public void GetRepositoriesToExecute_FailedRepository_DoesNotRetryAutomatically()
    {
        var failedPlan = CreatePlan(
            "repository-a",
            2);

        var pendingPlan = CreatePlan(
            "repository-b",
            2);

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            [
                failedPlan,
                pendingPlan
            ],
            [
                CreateFailedResult(failedPlan)
            ]);

        var plan = Assert.Single(result);

        Assert.Equal(
            "repository-b",
            plan.RepositoryName);
    }

    [Fact]
    public void GetRepositoriesToExecute_AllRepositoriesSucceeded_ReturnsEmptyList()
    {
        var firstPlan = CreatePlan(
            "repository-a",
            2);

        var secondPlan = CreatePlan(
            "repository-b",
            2);

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            [
                firstPlan,
                secondPlan
            ],
            [
                CreateSuccessfulResult(firstPlan),
                CreateSuccessfulResult(secondPlan)
            ]);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRepositoriesToExecute_AllRepositoriesFailed_ReturnsEmptyList()
    {
        var firstPlan = CreatePlan(
            "repository-a",
            2);

        var secondPlan = CreatePlan(
            "repository-b",
            2);

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            [
                firstPlan,
                secondPlan
            ],
            [
                CreateFailedResult(firstPlan),
                CreateFailedResult(secondPlan)
            ]);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRepositoriesToExecute_ReturnsPendingRepositoriesInDeterministicOrder()
    {
        var plans = new[]
        {
            CreatePlan(
                "zeta-repository",
                4),

            CreatePlan(
                "Alpha-repository",
                4),

            CreatePlan(
                "middle-repository",
                4)
        };

        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            plans,
            []);

        Assert.Equal(
            [
                "Alpha-repository",
                "middle-repository",
                "zeta-repository"
            ],
            result.Select(plan => plan.RepositoryName));
    }

    [Fact]
    public void GetRepositoriesToExecute_MixedWaves_ThrowsInvalidOperationException()
    {
        var selector = new MigrationWaveRepositorySelector();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => selector.GetRepositoriesToExecute(
                    [
                        CreatePlan(
                            "repository-a",
                            1),

                        CreatePlan(
                            "repository-b",
                            2)
                    ],
                    []));

        Assert.Contains(
            "exactly one migration wave",
            exception.Message);
    }

    [Fact]
    public void GetRepositoriesToExecute_TransformationCommitMismatch_ThrowsInvalidOperationException()
    {
        var plan = CreatePlan(
            "repository-a",
            1);

        var executionResult =
            CreateSuccessfulResult(
                plan,
                transformationCommitSha:
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var selector = new MigrationWaveRepositorySelector();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => selector.GetRepositoriesToExecute(
                    [plan],
                    [executionResult]));

        Assert.Contains(
            "does not match transformation plan source commit",
            exception.Message);
    }

    [Fact]
    public void GetRepositoriesToExecute_ValidationCommitMismatch_ThrowsInvalidOperationException()
    {
        var plan = CreatePlan(
            "repository-a",
            1);

        var executionResult =
            CreateSuccessfulResult(
                plan,
                validationCommitSha:
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

        var selector = new MigrationWaveRepositorySelector();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => selector.GetRepositoriesToExecute(
                    [plan],
                    [executionResult]));

        Assert.Contains(
            "Validation result",
            exception.Message);

        Assert.Contains(
            "does not match transformation plan source commit",
            exception.Message);
    }

    [Fact]
    public void GetRepositoriesToExecute_ValidationRepositoryMismatch_ThrowsInvalidOperationException()
    {
        var plan = CreatePlan(
            "repository-a",
            1);

        var executionResult =
            CreateSuccessfulResult(
                plan,
                validationRepositoryName:
                    "different-repository");

        var selector = new MigrationWaveRepositorySelector();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => selector.GetRepositoriesToExecute(
                    [plan],
                    [executionResult]));

        Assert.Contains(
            "Validation result repository",
            exception.Message);

        Assert.Contains(
            "does not match transformation plan repository",
            exception.Message);
    }

    [Fact]
    public void GetRepositoriesToExecute_DuplicateExecutionResults_ThrowsArgumentException()
    {
        var plan = CreatePlan(
            "repository-a",
            1);

        var executionResult =
            CreateSuccessfulResult(plan);

        var selector = new MigrationWaveRepositorySelector();

        Assert.Throws<ArgumentException>(
            () => selector.GetRepositoriesToExecute(
                [plan],
                [
                    executionResult,
                    executionResult
                ]));
    }

    [Fact]
    public void GetRepositoriesToExecute_EmptyActiveWave_ReturnsEmptyList()
    {
        var selector = new MigrationWaveRepositorySelector();

        var result = selector.GetRepositoriesToExecute(
            [],
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void GetRepositoriesToExecute_NullActiveWave_ThrowsArgumentNullException()
    {
        var selector = new MigrationWaveRepositorySelector();

        Assert.Throws<ArgumentNullException>(
            () => selector.GetRepositoriesToExecute(
                null!,
                []));
    }

    [Fact]
    public void GetRepositoriesToExecute_NullExecutionResults_ThrowsArgumentNullException()
    {
        var selector = new MigrationWaveRepositorySelector();

        Assert.Throws<ArgumentNullException>(
            () => selector.GetRepositoriesToExecute(
                [
                    CreatePlan(
                        "repository-a",
                        1)
                ],
                null!));
    }

    private static TransformationPlan CreatePlan(
        string repositoryName,
        int migrationWave)
    {
        return new TransformationPlan(
            RepositoryName: repositoryName,
            SourceCommitSha: CommitSha,
            MigrationWave: migrationWave,
            RepositoryKind:
                RepositoryKind.SpringBootApplication,
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
                "unit-tests"
            ]);
    }

    private static TransformationExecutionResult CreateSuccessfulResult(
        TransformationPlan plan,
        string? transformationCommitSha = null,
        string? validationCommitSha = null,
        string? validationRepositoryName = null)
    {
        return new TransformationExecutionResult(
            Transformation:
                new TransformationResult(
                    RepositoryName:
                        plan.RepositoryName,
                    SourceCommitSha:
                        transformationCommitSha
                        ?? plan.SourceCommitSha,
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
                        validationRepositoryName
                        ?? plan.RepositoryName,
                    SourceCommitSha:
                        validationCommitSha
                        ?? plan.SourceCommitSha,
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
}