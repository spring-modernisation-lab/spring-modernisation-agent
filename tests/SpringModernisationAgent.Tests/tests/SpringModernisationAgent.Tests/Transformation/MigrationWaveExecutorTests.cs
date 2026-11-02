using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class MigrationWaveExecutorTests
{
    private const string CommitSha =
        "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task ExecuteNextWaveAsync_NoPlans_ReturnsEmptyList()
    {
        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [],
            []);

        Assert.Empty(result);
        Assert.Empty(coordinator.Calls);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_NoExistingResults_ExecutesLowestWaveOnly()
    {
        var firstWave = CreatePlan(
            "foundation",
            1);

        var secondWave = CreatePlan(
            "application",
            2);

        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                secondWave,
                firstWave
            ],
            []);

        var executionResult = Assert.Single(result);

        Assert.Equal(
            "foundation",
            executionResult.Transformation.RepositoryName);

        var call = Assert.Single(coordinator.Calls);

        Assert.Equal(
            "foundation",
            call.Plan.RepositoryName);

        Assert.Equal(
            1,
            call.Plan.MigrationWave);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_PreviousWaveSucceeded_ExecutesNextWave()
    {
        var firstWave = CreatePlan(
            "foundation",
            1);

        var secondWave = CreatePlan(
            "shared-library",
            2);

        var thirdWave = CreatePlan(
            "application",
            3);

        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                thirdWave,
                secondWave,
                firstWave
            ],
            [
                CreateSuccessfulResult(firstWave)
            ]);

        var executionResult = Assert.Single(result);

        Assert.Equal(
            "shared-library",
            executionResult.Transformation.RepositoryName);

        var call = Assert.Single(coordinator.Calls);

        Assert.Equal(
            2,
            call.Plan.MigrationWave);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_PartiallyCompletedWave_ExecutesOnlyPendingRepositories()
    {
        var completedPlan = CreatePlan(
            "repository-a",
            4);

        var pendingPlan = CreatePlan(
            "repository-b",
            4);

        var laterPlan = CreatePlan(
            "application",
            5);

        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                completedPlan,
                pendingPlan,
                laterPlan
            ],
            [
                CreateSuccessfulResult(
                    completedPlan)
            ]);

        var executionResult = Assert.Single(result);

        Assert.Equal(
            "repository-b",
            executionResult.Transformation.RepositoryName);

        var call = Assert.Single(coordinator.Calls);

        Assert.Equal(
            "repository-b",
            call.Plan.RepositoryName);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_FailedRepository_IsNotRetriedAutomatically()
    {
        var failedPlan = CreatePlan(
            "repository-a",
            4);

        var pendingPlan = CreatePlan(
            "repository-b",
            4);

        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                failedPlan,
                pendingPlan
            ],
            [
                CreateFailedResult(failedPlan)
            ]);

        Assert.Empty(result);
        Assert.Empty(coordinator.Calls);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_MultipleRepositories_ExecutesInDeterministicOrder()
    {
        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                CreatePlan(
                    "zeta-library",
                    2),

                CreatePlan(
                    "Alpha-library",
                    2),

                CreatePlan(
                    "middle-library",
                    2),

                CreatePlan(
                    "later-application",
                    3)
            ],
            []);

        Assert.Equal(
            [
                "Alpha-library",
                "middle-library",
                "zeta-library"
            ],
            result.Select(
                item =>
                    item.Transformation.RepositoryName));

        Assert.Equal(
            [
                "Alpha-library",
                "middle-library",
                "zeta-library"
            ],
            coordinator.Calls.Select(
                call =>
                    call.Plan.RepositoryName));
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_FirstRepositoryFails_StillExecutesRestOfWave()
    {
        // Repositories in one wave do not depend on each other, so one
        // failure must not hide the outcome of its independent peers.
        var firstPlan = CreatePlan(
            "repository-a",
            2);

        var secondPlan = CreatePlan(
            "repository-b",
            2);

        var coordinator = new FakeExecutionCoordinator
        {
            ResultFactory = plan =>
                plan.RepositoryName == "repository-a"
                    ? CreateFailedResult(plan)
                    : CreateSuccessfulResult(plan)
        };

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                firstPlan,
                secondPlan
            ],
            []);

        Assert.Equal(
            2,
            result.Count);

        Assert.False(
            result[0].Succeeded);

        Assert.Equal(
            "repository-a",
            result[0].Transformation.RepositoryName);

        Assert.True(
            result[1].Succeeded);

        Assert.Equal(
            [
                "repository-a",
                "repository-b"
            ],
            coordinator.Calls.Select(
                call => call.Plan.RepositoryName));
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_MiddleRepositoryFails_ReturnsResultsForWholeWaveInOrder()
    {
        var firstPlan = CreatePlan(
            "repository-a",
            2);

        var secondPlan = CreatePlan(
            "repository-b",
            2);

        var thirdPlan = CreatePlan(
            "repository-c",
            2);

        var coordinator = new FakeExecutionCoordinator
        {
            ResultFactory = plan =>
                plan.RepositoryName == "repository-b"
                    ? CreateFailedResult(plan)
                    : CreateSuccessfulResult(plan)
        };

        var executor = CreateExecutor(coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                firstPlan,
                secondPlan,
                thirdPlan
            ],
            []);

        Assert.Equal(
            [
                "repository-a",
                "repository-b",
                "repository-c"
            ],
            result.Select(
                executionResult =>
                    executionResult.Transformation.RepositoryName));

        Assert.Equal(
            [true, false, true],
            result.Select(
                executionResult => executionResult.Succeeded));
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_FailureInWave_BlocksNextWaveOnFollowingInvocation()
    {
        var failingPlan = CreatePlan(
            "repository-a",
            1);

        var passingPlan = CreatePlan(
            "repository-b",
            1);

        var laterPlan = CreatePlan(
            "repository-c",
            2);

        var coordinator = new FakeExecutionCoordinator
        {
            ResultFactory = plan =>
                plan.RepositoryName == "repository-a"
                    ? CreateFailedResult(plan)
                    : CreateSuccessfulResult(plan)
        };

        var executor = CreateExecutor(coordinator);

        IReadOnlyList<TransformationPlan> plans =
        [
            failingPlan,
            passingPlan,
            laterPlan
        ];

        var firstRun = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            plans,
            []);

        var secondRun = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            plans,
            firstRun);

        Assert.Empty(secondRun);

        Assert.DoesNotContain(
            coordinator.Calls,
            call =>
                call.Plan.RepositoryName == "repository-c");
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_DoesNotAdvanceToLaterWave()
    {
        var firstPlan = CreatePlan(
            "repository-a",
            1);

        var laterPlan = CreatePlan(
            "repository-b",
            2);

        var coordinator = new FakeExecutionCoordinator();

        var executor = CreateExecutor(coordinator);

        await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                firstPlan,
                laterPlan
            ],
            []);

        var call = Assert.Single(
            coordinator.Calls);

        Assert.Equal(
            1,
            call.Plan.MigrationWave);

        Assert.Equal(
            "repository-a",
            call.Plan.RepositoryName);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_PassesOrganisationToCoordinator()
    {
        var coordinator =
            new FakeExecutionCoordinator();

        var executor = CreateExecutor(
            coordinator);

        await executor.ExecuteNextWaveAsync(
            "different-organisation",
            [
                CreatePlan(
                    "repository-a",
                    1)
            ],
            []);

        var call = Assert.Single(
            coordinator.Calls);

        Assert.Equal(
            "different-organisation",
            call.Organisation);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_PassesExactPlanToCoordinator()
    {
        var plan = CreatePlan(
            "repository-a",
            7);

        var coordinator =
            new FakeExecutionCoordinator();

        var executor = CreateExecutor(
            coordinator);

        await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [plan],
            []);

        var call = Assert.Single(
            coordinator.Calls);

        Assert.Same(
            plan,
            call.Plan);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_PassesCancellationTokenToCoordinator()
    {
        var coordinator =
            new FakeExecutionCoordinator();

        var executor = CreateExecutor(
            coordinator);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        var cancellationToken =
            cancellationTokenSource.Token;

        await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [
                CreatePlan(
                    "repository-a",
                    1)
            ],
            [],
            cancellationToken);

        var call = Assert.Single(
            coordinator.Calls);

        Assert.Equal(
            cancellationToken,
            call.CancellationToken);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_CancelledBeforeExecution_ThrowsOperationCanceledException()
    {
        var coordinator =
            new FakeExecutionCoordinator();

        var executor = CreateExecutor(
            coordinator);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteNextWaveAsync(
                "test-organisation",
                [
                    CreatePlan(
                        "repository-a",
                        1)
                ],
                [],
                cancellationTokenSource.Token));

        Assert.Empty(
            coordinator.Calls);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_AllPlansAlreadyCompleted_ReturnsEmptyList()
    {
        var plan = CreatePlan(
            "repository-a",
            1);

        var coordinator =
            new FakeExecutionCoordinator();

        var executor = CreateExecutor(
            coordinator);

        var result = await executor.ExecuteNextWaveAsync(
            "test-organisation",
            [plan],
            [
                CreateSuccessfulResult(plan)
            ]);

        Assert.Empty(result);
        Assert.Empty(coordinator.Calls);
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_EmptyOrganisation_ThrowsArgumentException()
    {
        var executor = CreateExecutor(
            new FakeExecutionCoordinator());

        await Assert.ThrowsAsync<ArgumentException>(
            () => executor.ExecuteNextWaveAsync(
                string.Empty,
                [
                    CreatePlan(
                        "repository-a",
                        1)
                ],
                []));
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_NullPlans_ThrowsArgumentNullException()
    {
        var executor = CreateExecutor(
            new FakeExecutionCoordinator());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => executor.ExecuteNextWaveAsync(
                "test-organisation",
                null!,
                []));
    }

    [Fact]
    public async Task ExecuteNextWaveAsync_NullExistingResults_ThrowsArgumentNullException()
    {
        var executor = CreateExecutor(
            new FakeExecutionCoordinator());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => executor.ExecuteNextWaveAsync(
                "test-organisation",
                [
                    CreatePlan(
                        "repository-a",
                        1)
                ],
                null!));
    }

    private static MigrationWaveExecutor CreateExecutor(
        IOpenRewriteExecutionCoordinator coordinator)
    {
        return new MigrationWaveExecutor(
            new MigrationWaveExecutionPlanner(),
            new MigrationWaveRepositorySelector(),
            coordinator);
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
                "test-route",
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

    private static TransformationExecutionResult CreateSuccessfulResult(
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
                            Check:
                                "compile",
                            Passed: false,
                            StandardOutput: null,
                            StandardError:
                                "Validation failed.")
                    ]));
    }

    private sealed class FakeExecutionCoordinator
        : IOpenRewriteExecutionCoordinator
    {
        public Func<
            TransformationPlan,
            TransformationExecutionResult>?
            ResultFactory
        { get; set; }

        public List<ExecutionCall> Calls { get; } = [];

        public Task<TransformationExecutionResult> ExecuteAsync(
            string organisation,
            TransformationPlan transformationPlan,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(
                new ExecutionCall(
                    organisation,
                    transformationPlan,
                    cancellationToken));

            var result =
                ResultFactory?.Invoke(
                    transformationPlan)
                ?? CreateSuccessfulResult(
                    transformationPlan);

            return Task.FromResult(result);
        }
    }

    private sealed record ExecutionCall(
        string Organisation,
        TransformationPlan Plan,
        CancellationToken CancellationToken);
}