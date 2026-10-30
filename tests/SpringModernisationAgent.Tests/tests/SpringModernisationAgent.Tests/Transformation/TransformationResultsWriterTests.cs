using System.Text.Json;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class TransformationResultsWriterTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task WriteAsync_SuccessfulResult_WritesTransformationResult()
    {
        var outputDirectory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                outputDirectory,
                "transformation-results.json");

            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                outputPath);

            Assert.True(File.Exists(outputPath));

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                JsonValueKind.Array,
                document.RootElement.ValueKind);

            var result = document.RootElement[0];

            Assert.Equal(
                "customer-service",
                result
                    .GetProperty("Transformation")
                    .GetProperty("RepositoryName")
                    .GetString());

            Assert.Equal(
                CommitSha,
                result
                    .GetProperty("Transformation")
                    .GetProperty("SourceCommitSha")
                    .GetString());

            Assert.Equal(
                "Succeeded",
                result
                    .GetProperty("Transformation")
                    .GetProperty("Status")
                    .GetString());

            Assert.True(
                result
                    .GetProperty("Succeeded")
                    .GetBoolean());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_SuccessfulResult_WritesAppliedRecipes()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            var recipes = document.RootElement[0]
                .GetProperty("Transformation")
                .GetProperty("AppliedRecipes");

            Assert.Equal(2, recipes.GetArrayLength());

            Assert.Equal(
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                recipes[0].GetString());

            Assert.Equal(
                "org.openrewrite.java.migrate.UpgradeToJava21",
                recipes[1].GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_SuccessfulResult_WritesChangedFiles()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            var changedFiles = document.RootElement[0]
                .GetProperty("Transformation")
                .GetProperty("ChangedFiles");

            Assert.Equal(2, changedFiles.GetArrayLength());

            Assert.Equal(
                "pom.xml",
                changedFiles[0].GetString());

            Assert.Equal(
                "src/main/java/com/example/CustomerService.java",
                changedFiles[1].GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_SuccessfulResult_WritesValidationChecks()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            var validation = document.RootElement[0]
                .GetProperty("Validation");

            Assert.Equal(
                "Passed",
                validation
                    .GetProperty("Status")
                    .GetString());

            var checks = validation.GetProperty("Checks");

            Assert.Equal(2, checks.GetArrayLength());

            Assert.Equal(
                "compile",
                checks[0]
                    .GetProperty("Check")
                    .GetString());

            Assert.True(
                checks[0]
                    .GetProperty("Passed")
                    .GetBoolean());

            Assert.Equal(
                "unit-tests",
                checks[1]
                    .GetProperty("Check")
                    .GetString());

            Assert.True(
                checks[1]
                    .GetProperty("Passed")
                    .GetBoolean());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_FailedValidation_WritesOverallFailure()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var executionResult =
                new TransformationExecutionResult(
                    Transformation:
                        CreateTransformationResult(),
                    Validation:
                        new TransformationValidationResult(
                            RepositoryName: "customer-service",
                            SourceCommitSha: CommitSha,
                            Status:
                                TransformationValidationStatus.Failed,
                            Checks:
                            [
                                new TransformationValidationCheckResult(
                                    Check: "compile",
                                    Passed: true,
                                    StandardOutput: "Compile passed",
                                    StandardError: null),

                                new TransformationValidationCheckResult(
                                    Check: "unit-tests",
                                    Passed: false,
                                    StandardOutput: "Test output",
                                    StandardError: "Tests failed")
                            ]));

            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [executionResult],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            var result = document.RootElement[0];

            Assert.False(
                result
                    .GetProperty("Succeeded")
                    .GetBoolean());

            Assert.Equal(
                "Succeeded",
                result
                    .GetProperty("Transformation")
                    .GetProperty("Status")
                    .GetString());

            Assert.Equal(
                "Failed",
                result
                    .GetProperty("Validation")
                    .GetProperty("Status")
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_MultipleResults_WritesAllResults()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var first = CreateSuccessfulExecutionResult();

            var second = CreateSuccessfulExecutionResult(
                repositoryName: "account-service",
                commitSha:
                    "877e592eb09af302dde4aa149d48e94d67799d50");

            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [first, second],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                2,
                document.RootElement.GetArrayLength());

            Assert.Equal(
                "customer-service",
                document.RootElement[0]
                    .GetProperty("Transformation")
                    .GetProperty("RepositoryName")
                    .GetString());

            Assert.Equal(
                "account-service",
                document.RootElement[1]
                    .GetProperty("Transformation")
                    .GetProperty("RepositoryName")
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_EmptyResults_WritesEmptyArray()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [],
                outputPath);

            var json = await File.ReadAllTextAsync(outputPath);

            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                JsonValueKind.Array,
                document.RootElement.ValueKind);

            Assert.Empty(
                document.RootElement.EnumerateArray());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesOutputDirectory()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                root,
                "nested",
                "transformation-results.json");

            var writer = new TransformationResultsWriter();

            await writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                outputPath);

            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task WriteAsync_NullResults_ThrowsArgumentNullException()
    {
        var writer = new TransformationResultsWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                null!,
                "transformation-results.json"));
    }

    [Fact]
    public async Task WriteAsync_EmptyPath_ThrowsArgumentException()
    {
        var writer = new TransformationResultsWriter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => writer.WriteAsync(
                [CreateSuccessfulExecutionResult()],
                string.Empty));
    }

    private static TransformationExecutionResult CreateSuccessfulExecutionResult(
        string repositoryName = "customer-service",
        string commitSha = CommitSha)
    {
        return new TransformationExecutionResult(
            Transformation:
                CreateTransformationResult(
                    repositoryName,
                    commitSha),
            Validation:
                new TransformationValidationResult(
                    RepositoryName: repositoryName,
                    SourceCommitSha: commitSha,
                    Status: TransformationValidationStatus.Passed,
                    Checks:
                    [
                        new TransformationValidationCheckResult(
                            Check: "compile",
                            Passed: true,
                            StandardOutput: "Compile passed",
                            StandardError: null),

                        new TransformationValidationCheckResult(
                            Check: "unit-tests",
                            Passed: true,
                            StandardOutput: "Tests passed",
                            StandardError: null)
                    ]));
    }

    private static TransformationResult CreateTransformationResult(
        string repositoryName = "customer-service",
        string commitSha = CommitSha)
    {
        return new TransformationResult(
            RepositoryName: repositoryName,
            SourceCommitSha: commitSha,
            Status: TransformationStatus.Succeeded,
            AppliedRecipes:
            [
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                "org.openrewrite.java.migrate.UpgradeToJava21"
            ],
            ChangedFiles:
            [
                "pom.xml",
                "src/main/java/com/example/CustomerService.java"
            ],
            StandardOutput: "OpenRewrite completed.",
            StandardError: null);
    }

    private static string CreateTemporaryOutputPath()
    {
        var directory = CreateTemporaryDirectory();

        return Path.Combine(
            directory,
            "transformation-results.json");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);

        if (directory is not null)
        {
            DeleteDirectory(directory);
        }
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
}