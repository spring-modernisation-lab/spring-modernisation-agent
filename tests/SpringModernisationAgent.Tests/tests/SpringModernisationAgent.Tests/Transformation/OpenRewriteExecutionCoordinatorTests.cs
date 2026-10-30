using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Transformation;

public class OpenRewriteExecutionCoordinatorTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task CreateExecutionPlanAsync_ValidWorkspace_CreatesExecutionPlan()
    {
        var workspaceProvider = CreateWorkspaceProvider();
        var cleaner = new FakeRepositoryWorkspaceCleaner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner);

        var result = await coordinator.CreateExecutionPlanAsync(
            "spring-modernisation-lab",
            CreateTransformationPlan());

        Assert.Equal("customer-service", result.RepositoryName);
        Assert.Equal("/workspace/customer-service", result.RepositoryPath);
        Assert.Equal(CommitSha, result.SourceCommitSha);
        Assert.Equal(2, result.Recipes.Count);

        // CreateExecutionPlanAsync does not own workspace cleanup.
        Assert.False(cleaner.Called);
    }

    [Fact]
    public async Task CreateExecutionPlanAsync_PassesExactRequestToWorkspaceProvider()
    {
        var workspaceProvider = CreateWorkspaceProvider();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            new FakeRepositoryWorkspaceCleaner());

        await coordinator.CreateExecutionPlanAsync(
            "spring-modernisation-lab",
            CreateTransformationPlan());

        Assert.Equal(
            "spring-modernisation-lab",
            workspaceProvider.Organisation);

        Assert.Equal(
            "customer-service",
            workspaceProvider.RepositoryName);

        Assert.Equal(
            CommitSha,
            workspaceProvider.CommitSha);
    }

    [Fact]
    public async Task CreateExecutionPlanAsync_WorkspaceRepositoryDoesNotMatch_ThrowsInvalidOperationException()
    {
        var workspaceProvider = new FakeWorkspaceProvider
        {
            Workspace = new RepositoryWorkspace(
                RepositoryName: "account-service",
                RepositoryPath: "/workspace/account-service",
                CommitSha: CommitSha)
        };

        var runner = new FakeOpenRewriteRunner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            new FakeRepositoryWorkspaceCleaner(),
            runner);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.CreateExecutionPlanAsync(
                    "spring-modernisation-lab",
                    CreateTransformationPlan()));

        Assert.Contains(
            "does not match transformation repository",
            exception.Message);

        Assert.False(runner.Called);
    }

    [Fact]
    public async Task CreateExecutionPlanAsync_WorkspaceCommitDoesNotMatch_ThrowsInvalidOperationException()
    {
        var workspaceProvider = new FakeWorkspaceProvider
        {
            Workspace = new RepositoryWorkspace(
                RepositoryName: "customer-service",
                RepositoryPath: "/workspace/customer-service",
                CommitSha:
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
        };

        var runner = new FakeOpenRewriteRunner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            new FakeRepositoryWorkspaceCleaner(),
            runner);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.CreateExecutionPlanAsync(
                    "spring-modernisation-lab",
                    CreateTransformationPlan()));

        Assert.Contains(
            "does not match transformation source commit",
            exception.Message);

        Assert.False(runner.Called);
    }

    [Fact]
    public async Task ExecuteAsync_Success_CleansWorkspace()
    {
        var workspacePath = CreateTemporaryDirectory();

        try
        {
            var workspaceProvider = new FakeWorkspaceProvider
            {
                Workspace = new RepositoryWorkspace(
                    RepositoryName: "customer-service",
                    RepositoryPath: workspacePath,
                    CommitSha: CommitSha)
            };

            var cleaner = new FakeRepositoryWorkspaceCleaner();
            var runner = new FakeOpenRewriteRunner();

            var coordinator = CreateCoordinator(
                workspaceProvider,
                cleaner,
                runner);

            var result = await coordinator.ExecuteAsync(
                "spring-modernisation-lab",
                CreateTransformationPlan());

            Assert.True(result.Succeeded);

            Assert.Equal(
                TransformationStatus.Succeeded,
                result.Transformation.Status);

            Assert.Equal(
                TransformationValidationStatus.Passed,
                result.Validation.Status);

            Assert.True(runner.Called);
            Assert.True(cleaner.Called);

            Assert.Same(
                workspaceProvider.Workspace,
                cleaner.Workspace);
        }
        finally
        {
            DeleteDirectory(workspacePath);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RunnerReturnsFailedResult_CleansWorkspace()
    {
        var workspaceProvider = CreateWorkspaceProvider();
        var cleaner = new FakeRepositoryWorkspaceCleaner();

        var runner = new FakeOpenRewriteRunner
        {
            Result = new TransformationResult(
                RepositoryName: "customer-service",
                SourceCommitSha: CommitSha,
                Status: TransformationStatus.Failed,
                AppliedRecipes: [],
                ChangedFiles: [],
                StandardOutput: null,
                StandardError: "Transformation failed.")
        };

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        var result = await coordinator.ExecuteAsync(
            "spring-modernisation-lab",
            CreateTransformationPlan());

        Assert.False(result.Succeeded);

        Assert.Equal(
            TransformationStatus.Failed,
            result.Transformation.Status);

        Assert.Equal(
            TransformationValidationStatus.NotRun,
            result.Validation.Status);

        Assert.Empty(result.Validation.Checks);

        Assert.True(cleaner.Called);

        Assert.Same(
            workspaceProvider.Workspace,
            cleaner.Workspace);
    }

    [Fact]
    public async Task ExecuteAsync_RunnerThrows_CleansWorkspace()
    {
        var workspaceProvider = CreateWorkspaceProvider();
        var cleaner = new FakeRepositoryWorkspaceCleaner();

        var runner = new FakeOpenRewriteRunner
        {
            Exception = new InvalidOperationException(
                "OpenRewrite failed.")
        };

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ExecuteAsync(
                    "spring-modernisation-lab",
                    CreateTransformationPlan()));

        Assert.Equal(
            "OpenRewrite failed.",
            exception.Message);

        Assert.True(cleaner.Called);

        Assert.Same(
            workspaceProvider.Workspace,
            cleaner.Workspace);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledRunner_StillCleansWorkspace()
    {
        var workspaceProvider = CreateWorkspaceProvider();
        var cleaner = new FakeRepositoryWorkspaceCleaner();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        var runner = new FakeOpenRewriteRunner
        {
            Exception = new OperationCanceledException(
                cancellationTokenSource.Token)
        };

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ExecuteAsync(
                "spring-modernisation-lab",
                CreateTransformationPlan(),
                cancellationTokenSource.Token));

        Assert.True(cleaner.Called);

        Assert.False(
            cleaner.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceCreationFails_DoesNotCleanWorkspace()
    {
        var workspaceProvider = new FakeWorkspaceProvider
        {
            Exception = new InvalidOperationException(
                "Workspace creation failed.")
        };

        var cleaner = new FakeRepositoryWorkspaceCleaner();
        var runner = new FakeOpenRewriteRunner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(
                "spring-modernisation-lab",
                CreateTransformationPlan()));

        Assert.False(runner.Called);
        Assert.False(cleaner.Called);
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceCommitMismatch_DoesNotCallRunnerButCleansWorkspace()
    {
        var workspaceProvider = new FakeWorkspaceProvider
        {
            Workspace = new RepositoryWorkspace(
                RepositoryName: "customer-service",
                RepositoryPath: "/workspace/customer-service",
                CommitSha:
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
        };

        var cleaner = new FakeRepositoryWorkspaceCleaner();
        var runner = new FakeOpenRewriteRunner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ExecuteAsync(
                "spring-modernisation-lab",
                CreateTransformationPlan()));

        Assert.False(runner.Called);
        Assert.True(cleaner.Called);
    }

    [Fact]
    public async Task ExecuteAsync_PassesCancellationTokenToWorkspaceAndRunner()
    {
        var workspaceProvider = CreateWorkspaceProvider();
        var cleaner = new FakeRepositoryWorkspaceCleaner();
        var runner = new FakeOpenRewriteRunner();

        var coordinator = CreateCoordinator(
            workspaceProvider,
            cleaner,
            runner);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        var cancellationToken =
            cancellationTokenSource.Token;

        await coordinator.ExecuteAsync(
            "spring-modernisation-lab",
            CreateTransformationPlan(),
            cancellationToken);

        Assert.Equal(
            cancellationToken,
            workspaceProvider.CancellationToken);

        Assert.Equal(
            cancellationToken,
            runner.CancellationToken);

        Assert.False(
            cleaner.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ExecuteAsync_TransformationSucceedsButValidationFails_ReturnsFailedExecution()
    {
        var workspacePath = CreateTemporaryDirectory();

        try
        {
            var workspaceProvider = new FakeWorkspaceProvider
            {
                Workspace = new RepositoryWorkspace(
                    RepositoryName: "customer-service",
                    RepositoryPath: workspacePath,
                    CommitSha: CommitSha)
            };

            var cleaner = new FakeRepositoryWorkspaceCleaner();

            var mavenRunner = new FakeMavenProcessRunner(
            [
                new MavenProcessResult(
                ExitCode: 0,
                StandardOutput: "Compile passed",
                StandardError: string.Empty),

            new MavenProcessResult(
                ExitCode: 1,
                StandardOutput: "Test output",
                StandardError: "Tests failed")
            ]);

            var coordinator = CreateCoordinator(
                workspaceProvider,
                cleaner,
                new FakeOpenRewriteRunner(),
                mavenRunner);

            var result = await coordinator.ExecuteAsync(
                "spring-modernisation-lab",
                CreateTransformationPlan());

            Assert.False(result.Succeeded);

            Assert.Equal(
                TransformationStatus.Succeeded,
                result.Transformation.Status);

            Assert.Equal(
                TransformationValidationStatus.Failed,
                result.Validation.Status);

            Assert.Equal(
                2,
                result.Validation.Checks.Count);

            Assert.Equal(
                "compile",
                result.Validation.Checks[0].Check);

            Assert.True(
                result.Validation.Checks[0].Passed);

            Assert.Equal(
                "unit-tests",
                result.Validation.Checks[1].Check);

            Assert.False(
                result.Validation.Checks[1].Passed);

            Assert.Equal(
                "Tests failed",
                result.Validation.Checks[1].StandardError);

            Assert.Equal(
                2,
                mavenRunner.Calls.Count);

            Assert.True(cleaner.Called);

            Assert.Same(
                workspaceProvider.Workspace,
                cleaner.Workspace);
        }
        finally
        {
            DeleteDirectory(workspacePath);
        }
    }

    private static OpenRewriteExecutionCoordinator CreateCoordinator(
        IRepositoryWorkspaceProvider workspaceProvider,
        IRepositoryWorkspaceCleaner workspaceCleaner,
        IOpenRewriteRunner? openRewriteRunner = null,
        IMavenProcessRunner? mavenProcessRunner = null)
    {
        var executionPlanBuilder =
            new OpenRewriteExecutionPlanBuilder();

        var transformationValidator =
            new TransformationValidator(
                mavenProcessRunner
                ?? new FakeMavenProcessRunner(
                [
                    SuccessfulMavenResult(),
                SuccessfulMavenResult(),
                SuccessfulMavenResult()
                ]));

        return new OpenRewriteExecutionCoordinator(
            workspaceProvider,
            workspaceCleaner,
            executionPlanBuilder,
            openRewriteRunner ?? new FakeOpenRewriteRunner(),
            transformationValidator);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }

    private static FakeWorkspaceProvider CreateWorkspaceProvider()
    {
        return new FakeWorkspaceProvider
        {
            Workspace = new RepositoryWorkspace(
                RepositoryName: "customer-service",
                RepositoryPath: "/workspace/customer-service",
                CommitSha: CommitSha)
        };
    }

    private static TransformationPlan CreateTransformationPlan()
    {
        return new TransformationPlan(
            RepositoryName: "customer-service",
            SourceCommitSha: CommitSha,
            MigrationWave: 3,
            RepositoryKind:
                RepositoryKind.SpringBootApplication,
            MigrationRouteId:
                "spring-boot-application-2.7-to-3",
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

    private sealed class FakeWorkspaceProvider
        : IRepositoryWorkspaceProvider
    {
        public RepositoryWorkspace? Workspace { get; set; }

        public Exception? Exception { get; set; }

        public string? Organisation { get; private set; }

        public string? RepositoryName { get; private set; }

        public string? CommitSha { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<RepositoryWorkspace> CreateAsync(
            string organisation,
            string repositoryName,
            string commitSha,
            CancellationToken cancellationToken = default)
        {
            Organisation = organisation;
            RepositoryName = repositoryName;
            CommitSha = commitSha;
            CancellationToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(
                Workspace
                ?? new RepositoryWorkspace(
                    repositoryName,
                    "/workspace/repository",
                    commitSha));
        }
    }

    private sealed class FakeRepositoryWorkspaceCleaner
        : IRepositoryWorkspaceCleaner
    {
        public bool Called { get; private set; }

        public RepositoryWorkspace? Workspace { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Exception? Exception { get; set; }

        public Task DeleteAsync(
            RepositoryWorkspace workspace,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            Workspace = workspace;
            CancellationToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOpenRewriteRunner
        : IOpenRewriteRunner
    {
        public TransformationResult? Result { get; set; }

        public Exception? Exception { get; set; }

        public OpenRewriteExecutionPlan? ExecutionPlan { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public bool Called { get; private set; }

        public Task<TransformationResult> RunAsync(
            OpenRewriteExecutionPlan executionPlan,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            ExecutionPlan = executionPlan;
            CancellationToken = cancellationToken;

            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(
                Result
                ?? new TransformationResult(
                    RepositoryName: executionPlan.RepositoryName,
                    SourceCommitSha: executionPlan.SourceCommitSha,
                    Status: TransformationStatus.Succeeded,
                    AppliedRecipes: executionPlan.Recipes
                        .Select(recipe => recipe.Recipe)
                        .ToList(),
                    ChangedFiles: [],
                    StandardOutput: null,
                    StandardError: null));
        }
    }

    private sealed class FakeMavenProcessRunner(
        IEnumerable<MavenProcessResult>? results = null)
        : IMavenProcessRunner
    {
        private readonly Queue<MavenProcessResult> _results =
            new(results ?? []);

        public List<MavenProcessCall> Calls { get; } = [];

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(
                new MavenProcessCall(
                    workingDirectory,
                    arguments.ToList(),
                    cancellationToken));

            if (!_results.TryDequeue(out var result))
            {
                throw new InvalidOperationException(
                    "No fake Maven process result was configured.");
            }

            return Task.FromResult(result);
        }
    }

    private sealed record MavenProcessCall(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        CancellationToken CancellationToken);

    private static MavenProcessResult SuccessfulMavenResult()
    {
        return new MavenProcessResult(
            ExitCode: 0,
            StandardOutput: string.Empty,
            StandardError: string.Empty);
    }

}