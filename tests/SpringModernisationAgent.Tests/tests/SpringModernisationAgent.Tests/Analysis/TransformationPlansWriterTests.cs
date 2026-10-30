using System.Text.Json;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class TransformationPlansWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesTransformationPlansJson()
    {
        var outputDirectory = CreateTemporaryDirectory();

        var outputPath = Path.Combine(
            outputDirectory,
            "transformation-plans.json");

        try
        {
            var plans = new[]
            {
                CreateTransformationPlan()
            };

            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                plans,
                outputPath);

            Assert.True(File.Exists(outputPath));

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;

            Assert.Equal(
                JsonValueKind.Array,
                root.ValueKind);

            Assert.Equal(
                1,
                root.GetArrayLength());

            var plan = root[0];

            Assert.Equal(
                "customer-service",
                plan.GetProperty("RepositoryName").GetString());

            Assert.Equal(
                "abc123",
                plan.GetProperty("SourceCommitSha").GetString());

            Assert.Equal(
                3,
                plan.GetProperty("MigrationWave").GetInt32());

            Assert.Equal(
                "SpringBootApplication",
                plan.GetProperty("RepositoryKind").GetString());

            Assert.Equal(
                "spring-boot-application-2.7-to-3",
                plan.GetProperty("MigrationRouteId").GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_WritesTargetConfiguration()
    {
        var outputDirectory = CreateTemporaryDirectory();

        var outputPath = Path.Combine(
            outputDirectory,
            "transformation-plans.json");

        try
        {
            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                [CreateTransformationPlan()],
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document = JsonDocument.Parse(json);

            var target = document.RootElement[0]
                .GetProperty("Target");

            Assert.Equal(
                "3.5.x",
                target
                    .GetProperty("SpringBootVersion")
                    .GetString());

            Assert.Equal(
                "21",
                target
                    .GetProperty("JavaVersion")
                    .GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_WritesOpenRewriteConfiguration()
    {
        var outputDirectory = CreateTemporaryDirectory();

        var outputPath = Path.Combine(
            outputDirectory,
            "transformation-plans.json");

        try
        {
            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                [CreateTransformationPlan()],
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document = JsonDocument.Parse(json);

            var openRewrite = document.RootElement[0]
                .GetProperty("OpenRewrite");

            Assert.Equal(
                JsonValueKind.Array,
                openRewrite.ValueKind);

            Assert.Equal(
                2,
                openRewrite.GetArrayLength());

            Assert.Equal(
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                openRewrite[0]
                    .GetProperty("Recipe")
                    .GetString());

            Assert.Equal(
                "org.openrewrite.recipe:rewrite-spring",
                openRewrite[0]
                    .GetProperty("Artifact")
                    .GetString());

            Assert.Equal(
                "6.43.0",
                openRewrite[0]
                    .GetProperty("Version")
                    .GetString());

            Assert.Equal(
                "org.openrewrite.java.migrate.UpgradeToJava21",
                openRewrite[1]
                    .GetProperty("Recipe")
                    .GetString());

            Assert.Equal(
                "org.openrewrite.recipe:rewrite-migrate-java",
                openRewrite[1]
                    .GetProperty("Artifact")
                    .GetString());

            Assert.Equal(
                "3.47.0",
                openRewrite[1]
                    .GetProperty("Version")
                    .GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_WritesValidationSteps()
    {
        var outputDirectory = CreateTemporaryDirectory();

        var outputPath = Path.Combine(
            outputDirectory,
            "transformation-plans.json");

        try
        {
            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                [CreateTransformationPlan()],
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document = JsonDocument.Parse(json);

            var validation = document.RootElement[0]
                .GetProperty("Validation");

            Assert.Equal(3, validation.GetArrayLength());

            Assert.Equal(
                "compile",
                validation[0].GetString());

            Assert.Equal(
                "unit-tests",
                validation[1].GetString());

            Assert.Equal(
                "application-context-test",
                validation[2].GetString());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesOutputDirectory()
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var outputPath = Path.Combine(
            rootDirectory,
            "nested",
            "transformation-plans.json");

        try
        {
            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                [CreateTransformationPlan()],
                outputPath);

            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_EmptyPlanList_WritesEmptyArray()
    {
        var outputDirectory = CreateTemporaryDirectory();

        var outputPath = Path.Combine(
            outputDirectory,
            "transformation-plans.json");

        try
        {
            var writer = new TransformationPlansWriter();

            await writer.WriteAsync(
                [],
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document = JsonDocument.Parse(json);

            Assert.Equal(
                JsonValueKind.Array,
                document.RootElement.ValueKind);

            Assert.Equal(
                0,
                document.RootElement.GetArrayLength());
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_NullPlans_ThrowsArgumentNullException()
    {
        var writer = new TransformationPlansWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                null!,
                "transformation-plans.json"));
    }

    [Fact]
    public async Task WriteAsync_EmptyPath_ThrowsArgumentException()
    {
        var writer = new TransformationPlansWriter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => writer.WriteAsync(
                [CreateTransformationPlan()],
                string.Empty));
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
                    Recipe: "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact: "org.openrewrite.recipe:rewrite-spring",
                    Version: "6.43.0"),
                new OpenRewriteTransformation(
                    Recipe: "org.openrewrite.java.migrate.UpgradeToJava21",
                    Artifact: "org.openrewrite.recipe:rewrite-migrate-java",
                    Version: "3.47.0")
            ],
            Validation:
            [
                "compile",
                "unit-tests",
                "application-context-test"
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
}
