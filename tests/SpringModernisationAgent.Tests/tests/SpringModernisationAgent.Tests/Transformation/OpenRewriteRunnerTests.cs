using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Build;

namespace SpringModernisationAgent.Tests.Transformation;

public class OpenRewriteRunnerTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task RunAsync_RepositoryDoesNotExist_ReturnsNotRun()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var changeDetector = new FakeRepositoryChangeDetector();
        var runner = new OpenRewriteRunner(
            new FakeMavenProcessRunner(),
            changeDetector,
            mavenPluginVersion: "6.25.0");

        var result = await runner.RunAsync(
            CreateExecutionPlan(repositoryPath));

        Assert.Equal(
            "customer-service",
            result.RepositoryName);

        Assert.Equal(
            CommitSha,
            result.SourceCommitSha);

        Assert.Equal(
            TransformationStatus.NotRun,
            result.Status);

        Assert.Empty(result.AppliedRecipes);
        Assert.Empty(result.ChangedFiles);
        Assert.Null(result.StandardOutput);

        Assert.Contains(
            "Repository directory was not found",
            result.StandardError);
        Assert.False(changeDetector.Called);
    }

    [Fact]
    public async Task RunAsync_NoRecipes_ReturnsNotRun()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var executionPlan = CreateExecutionPlan(
                repositoryPath) with
            {
                Recipes = []
            };

            var changeDetector = new FakeRepositoryChangeDetector();
            var runner = new OpenRewriteRunner(
                new FakeMavenProcessRunner(),
                changeDetector,
                mavenPluginVersion: "6.25.0");

            var result = await runner.RunAsync(
                executionPlan);

            Assert.Equal(
                TransformationStatus.NotRun,
                result.Status);

            Assert.Empty(result.AppliedRecipes);
            Assert.Empty(result.ChangedFiles);

            Assert.Equal(
                "No OpenRewrite recipes were supplied.",
                result.StandardError);
            Assert.False(changeDetector.Called);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_NullExecutionPlan_ThrowsArgumentNullException()
    {
        var runner = new OpenRewriteRunner(
            new FakeMavenProcessRunner(),
            new FakeRepositoryChangeDetector(),
            mavenPluginVersion: "6.25.0");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runner.RunAsync(null!));
    }

    [Fact]
    public void Constructor_EmptyPluginVersion_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new OpenRewriteRunner(
                new FakeMavenProcessRunner(),
                new FakeRepositoryChangeDetector(),
                mavenPluginVersion: string.Empty));
    }

    [Fact]
    public async Task RunAsync_MissingRecipeName_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var executionPlan = CreateExecutionPlan(
                repositoryPath) with
            {
                Recipes =
                [
                    new OpenRewriteExecutionRecipe(
                        Recipe: string.Empty,
                        Artifact: "org.openrewrite.recipe:rewrite-spring",
                        Version: "6.43.0")
                ]
            };

            var runner = new OpenRewriteRunner(
                new FakeMavenProcessRunner(),
                new FakeRepositoryChangeDetector(),
                mavenPluginVersion: "6.25.0");

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => runner.RunAsync(executionPlan));

            Assert.Equal(
                "The OpenRewrite recipe name is missing.",
                exception.Message);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_MissingArtifact_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var executionPlan = CreateExecutionPlan(
                repositoryPath) with
            {
                Recipes =
                [
                    new OpenRewriteExecutionRecipe(
                        Recipe:
                            "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                        Artifact: string.Empty,
                        Version: "6.43.0")
                ]
            };

            var runner = new OpenRewriteRunner(
                new FakeMavenProcessRunner(),
                new FakeRepositoryChangeDetector(),
                mavenPluginVersion: "6.25.0");

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => runner.RunAsync(executionPlan));

            Assert.Contains(
                "has no artifact",
                exception.Message);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_MissingRecipeVersion_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var executionPlan = CreateExecutionPlan(
                repositoryPath) with
            {
                Recipes =
                [
                    new OpenRewriteExecutionRecipe(
                        Recipe:
                            "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                        Artifact:
                            "org.openrewrite.recipe:rewrite-spring",
                        Version: string.Empty)
                ]
            };

            var runner = new OpenRewriteRunner(
                new FakeMavenProcessRunner(),
                new FakeRepositoryChangeDetector(),
                mavenPluginVersion: "6.25.0");

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => runner.RunAsync(executionPlan));

            Assert.Contains(
                "has no pinned version",
                exception.Message);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_AllRecipesSucceed_ReturnsCombinedProcessResults()
    {
        var repositoryPath = CreateTemporaryDirectory();
        var processRunner = new FakeMavenProcessRunner(
        [
            new MavenProcessResult(
                ExitCode: 0,
                StandardOutput: "First recipe output",
                StandardError: "First recipe warning"),
            new MavenProcessResult(
                ExitCode: 0,
                StandardOutput: "Second recipe output",
                StandardError: string.Empty)
        ]);
        var changeDetector = new FakeRepositoryChangeDetector
        {
            ChangedFiles = ["src/Changed.java", "pom.xml"]
        };

        try
        {
            var runner = new OpenRewriteRunner(
                processRunner,
                changeDetector,
                mavenPluginVersion: "6.25.0");

            var result = await runner.RunAsync(
                CreateExecutionPlan(repositoryPath));

            Assert.Equal(TransformationStatus.Succeeded, result.Status);
            Assert.Equal(
                [
                    "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    "org.openrewrite.java.migrate.UpgradeToJava21"
                ],
                result.AppliedRecipes);
            Assert.Equal(
                $"First recipe output{Environment.NewLine}Second recipe output",
                result.StandardOutput);
            Assert.Equal("First recipe warning", result.StandardError);
            Assert.Equal(
                ["src/Changed.java", "pom.xml"],
                result.ChangedFiles);
            Assert.True(changeDetector.Called);
            Assert.Equal(repositoryPath, changeDetector.RepositoryPath);

            Assert.Collection(
                processRunner.Calls,
                call =>
                {
                    Assert.Equal(repositoryPath, call.WorkingDirectory);
                    Assert.Equal(
                        [
                            "--batch-mode",
                            "org.openrewrite.maven:rewrite-maven-plugin:6.25.0:run",
                            "-Drewrite.activeRecipes=org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                            "-Drewrite.recipeArtifactCoordinates=org.openrewrite.recipe:rewrite-spring:6.43.0"
                        ],
                        call.Arguments);
                },
                call =>
                {
                    Assert.Equal(repositoryPath, call.WorkingDirectory);
                    Assert.Equal(
                        [
                            "--batch-mode",
                            "org.openrewrite.maven:rewrite-maven-plugin:6.25.0:run",
                            "-Drewrite.activeRecipes=org.openrewrite.java.migrate.UpgradeToJava21",
                            "-Drewrite.recipeArtifactCoordinates=org.openrewrite.recipe:rewrite-migrate-java:3.47.0"
                        ],
                        call.Arguments);
                });
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_SecondRecipeFails_ReturnsFailureAndDoesNotDetectChanges()
    {
        var repositoryPath = CreateTemporaryDirectory();
        var processRunner = new FakeMavenProcessRunner(
        [
            new MavenProcessResult(
                ExitCode: 0,
                StandardOutput: "First recipe output",
                StandardError: string.Empty),
            new MavenProcessResult(
                ExitCode: 1,
                StandardOutput: "Second recipe output",
                StandardError: "Second recipe failed")
        ]);
        var changeDetector = new FakeRepositoryChangeDetector();

        try
        {
            var runner = new OpenRewriteRunner(
                processRunner,
                changeDetector,
                mavenPluginVersion: "6.25.0");

            var result = await runner.RunAsync(
                CreateExecutionPlan(repositoryPath));

            Assert.Equal(TransformationStatus.Failed, result.Status);
            Assert.Equal(
                ["org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5"],
                result.AppliedRecipes);
            Assert.Equal(
                $"First recipe output{Environment.NewLine}Second recipe output",
                result.StandardOutput);
            Assert.Equal("Second recipe failed", result.StandardError);
            Assert.Equal(2, processRunner.Calls.Count);
            Assert.False(changeDetector.Called);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunAsync_FirstRecipeFails_DoesNotRunRemainingRecipes()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                new MavenProcessResult(
                ExitCode: 1,
                StandardOutput: "First recipe output",
                StandardError: "First recipe failed")
            ]);

            var changeDetector = new FakeRepositoryChangeDetector();

            var runner = new OpenRewriteRunner(
                processRunner,
                changeDetector,
                mavenPluginVersion: "6.25.0");

            var result = await runner.RunAsync(
                CreateExecutionPlan(repositoryPath));

            Assert.Equal(
                TransformationStatus.Failed,
                result.Status);

            Assert.Empty(result.AppliedRecipes);
            Assert.Empty(result.ChangedFiles);

            Assert.Equal(
                "First recipe output",
                result.StandardOutput);

            Assert.Equal(
                "First recipe failed",
                result.StandardError);

            Assert.Single(processRunner.Calls);
            Assert.False(changeDetector.Called);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    private static OpenRewriteExecutionPlan CreateExecutionPlan(
        string repositoryPath)
    {
        return new OpenRewriteExecutionPlan(
            RepositoryName: "customer-service",
            RepositoryPath: repositoryPath,
            SourceCommitSha: CommitSha,
            Recipes:
            [
                new OpenRewriteExecutionRecipe(
                    Recipe:
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact:
                        "org.openrewrite.recipe:rewrite-spring",
                    Version: "6.43.0"),

                new OpenRewriteExecutionRecipe(
                    Recipe:
                        "org.openrewrite.java.migrate.UpgradeToJava21",
                    Artifact:
                        "org.openrewrite.recipe:rewrite-migrate-java",
                    Version: "3.47.0")
            ]);
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

    private sealed class FakeMavenProcessRunner(
        IEnumerable<MavenProcessResult>? results = null) : IMavenProcessRunner
    {
        private readonly Queue<MavenProcessResult> _results =
            new(results ?? []);

        public List<MavenProcessCall> Calls { get; } = [];

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new MavenProcessCall(
                workingDirectory,
                arguments,
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

    private sealed class FakeRepositoryChangeDetector
        : IRepositoryChangeDetector
    {
        public IReadOnlyList<string> ChangedFiles { get; set; } = [];

        public bool Called { get; private set; }

        public string? RepositoryPath { get; private set; }

        public Task<IReadOnlyList<string>> GetChangedFilesAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            Called = true;
            RepositoryPath = repositoryPath;

            return Task.FromResult(ChangedFiles);
        }
    }
}