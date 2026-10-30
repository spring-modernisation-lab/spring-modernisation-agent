using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class TransformationValidatorTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task ValidateAsync_AllChecksPass_ReturnsPassed()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                SuccessfulResult("Compile passed"),
                SuccessfulResult("Tests passed"),
                SuccessfulResult("Context test passed")
            ]);

            var validator = new TransformationValidator(
                processRunner);

            var result = await validator.ValidateAsync(
                CreateTransformationPlan(),
                repositoryPath);

            Assert.Equal(
                TransformationValidationStatus.Passed,
                result.Status);

            Assert.True(result.Passed);
            Assert.Equal(3, result.Checks.Count);

            Assert.All(
                result.Checks,
                check => Assert.True(check.Passed));

            Assert.Equal(
                ["compile", "unit-tests", "application-context-test"],
                result.Checks.Select(check => check.Check));
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_AllChecksPass_UsesExpectedMavenArguments()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                SuccessfulResult(),
                SuccessfulResult(),
                SuccessfulResult()
            ]);

            var validator = new TransformationValidator(
                processRunner);

            await validator.ValidateAsync(
                CreateTransformationPlan(),
                repositoryPath);

            Assert.Collection(
                processRunner.Calls,
                call =>
                {
                    Assert.Equal(repositoryPath, call.WorkingDirectory);

                    Assert.Equal(
                        ["--batch-mode", "compile"],
                        call.Arguments);
                },
                call =>
                {
                    Assert.Equal(repositoryPath, call.WorkingDirectory);

                    Assert.Equal(
                        ["--batch-mode", "test"],
                        call.Arguments);
                },
                call =>
                {
                    Assert.Equal(repositoryPath, call.WorkingDirectory);

                    Assert.Equal(
                        ["--batch-mode", "test"],
                        call.Arguments);
                });
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_FirstCheckFails_StopsValidation()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                new MavenProcessResult(
                    ExitCode: 1,
                    StandardOutput: "Compile output",
                    StandardError: "Compilation failed")
            ]);

            var validator = new TransformationValidator(
                processRunner);

            var result = await validator.ValidateAsync(
                CreateTransformationPlan(),
                repositoryPath);

            Assert.Equal(
                TransformationValidationStatus.Failed,
                result.Status);

            Assert.False(result.Passed);

            var check = Assert.Single(result.Checks);

            Assert.Equal("compile", check.Check);
            Assert.False(check.Passed);
            Assert.Equal("Compile output", check.StandardOutput);
            Assert.Equal("Compilation failed", check.StandardError);

            Assert.Single(processRunner.Calls);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_SecondCheckFails_RecordsCompletedChecksAndStops()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                SuccessfulResult("Compile passed"),

                new MavenProcessResult(
                    ExitCode: 1,
                    StandardOutput: "Test output",
                    StandardError: "Tests failed")
            ]);

            var validator = new TransformationValidator(
                processRunner);

            var result = await validator.ValidateAsync(
                CreateTransformationPlan(),
                repositoryPath);

            Assert.Equal(
                TransformationValidationStatus.Failed,
                result.Status);

            Assert.Equal(2, result.Checks.Count);

            Assert.True(result.Checks[0].Passed);
            Assert.False(result.Checks[1].Passed);

            Assert.Equal("compile", result.Checks[0].Check);
            Assert.Equal("unit-tests", result.Checks[1].Check);

            Assert.Equal(2, processRunner.Calls.Count);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_UnsupportedCheck_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var transformationPlan = CreateTransformationPlan() with
            {
                Validation = ["deploy"]
            };

            var processRunner = new FakeMavenProcessRunner();

            var validator = new TransformationValidator(
                processRunner);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => validator.ValidateAsync(
                        transformationPlan,
                        repositoryPath));

            Assert.Equal(
                "Unsupported transformation validation check 'deploy'.",
                exception.Message);

            Assert.Empty(processRunner.Calls);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_RepositoryDoesNotExist_ReturnsNotRun()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var validator = new TransformationValidator(
            new FakeMavenProcessRunner());

        var result = await validator.ValidateAsync(
            CreateTransformationPlan(),
            repositoryPath);

        Assert.Equal(
            TransformationValidationStatus.NotRun,
            result.Status);

        Assert.False(result.Passed);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public async Task ValidateAsync_NoValidationChecks_ReturnsNotRun()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var transformationPlan = CreateTransformationPlan() with
            {
                Validation = []
            };

            var processRunner = new FakeMavenProcessRunner();

            var validator = new TransformationValidator(
                processRunner);

            var result = await validator.ValidateAsync(
                transformationPlan,
                repositoryPath);

            Assert.Equal(
                TransformationValidationStatus.NotRun,
                result.Status);

            Assert.Empty(result.Checks);
            Assert.Empty(processRunner.Calls);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_PassesCancellationTokenToMavenRunner()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeMavenProcessRunner(
            [
                SuccessfulResult(),
                SuccessfulResult(),
                SuccessfulResult()
            ]);

            var validator = new TransformationValidator(
                processRunner);

            using var cancellationTokenSource =
                new CancellationTokenSource();

            var cancellationToken =
                cancellationTokenSource.Token;

            await validator.ValidateAsync(
                CreateTransformationPlan(),
                repositoryPath,
                cancellationToken);

            Assert.All(
                processRunner.Calls,
                call => Assert.Equal(
                    cancellationToken,
                    call.CancellationToken));
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_MavenOperationCancelled_PropagatesCancellation()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            using var cancellationTokenSource =
                new CancellationTokenSource();

            cancellationTokenSource.Cancel();

            var validator = new TransformationValidator(
                new CancellingMavenProcessRunner());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => validator.ValidateAsync(
                    CreateTransformationPlan(),
                    repositoryPath,
                    cancellationTokenSource.Token));
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task ValidateAsync_NullTransformationPlan_ThrowsArgumentNullException()
    {
        var validator = new TransformationValidator(
            new FakeMavenProcessRunner());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => validator.ValidateAsync(
                null!,
                "repository"));
    }

    [Fact]
    public async Task ValidateAsync_EmptyRepositoryPath_ThrowsArgumentException()
    {
        var validator = new TransformationValidator(
            new FakeMavenProcessRunner());

        await Assert.ThrowsAsync<ArgumentException>(
            () => validator.ValidateAsync(
                CreateTransformationPlan(),
                string.Empty));
    }

    [Fact]
    public void Constructor_NullMavenProcessRunner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TransformationValidator(null!));
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

    private static MavenProcessResult SuccessfulResult(
        string standardOutput = "")
    {
        return new MavenProcessResult(
            ExitCode: 0,
            StandardOutput: standardOutput,
            StandardError: string.Empty);
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

    private sealed class CancellingMavenProcessRunner
        : IMavenProcessRunner
    {
        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.FromCanceled<MavenProcessResult>(
                cancellationToken);
        }
    }

    private sealed record MavenProcessCall(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        CancellationToken CancellationToken);
}