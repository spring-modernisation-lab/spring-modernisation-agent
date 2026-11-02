using System.Text.Json;
using SpringModernisationAgent.AI;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.AI;

public class MigrationAnalysisResultWriterTests
{
    [Fact]
    public async Task WriteAsync_ValidResult_WritesJsonFile()
    {
        var outputDirectory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                outputDirectory,
                "migration-analysis.json");

            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            Assert.True(File.Exists(outputPath));

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            Assert.Equal(
                JsonValueKind.Object,
                document.RootElement.ValueKind);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidResult_WritesStrategy()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            var strategy =
                document.RootElement
                    .GetProperty("Strategy");

            Assert.Equal(
                "Migrate shared components before dependent applications.",
                strategy
                    .GetProperty("Summary")
                    .GetString());

            var waves =
                strategy.GetProperty("Waves");

            Assert.Equal(
                2,
                waves.GetArrayLength());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidResult_WritesMigrationWaveDetails()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            var wave =
                document.RootElement
                    .GetProperty("Strategy")
                    .GetProperty("Waves")[0];

            Assert.Equal(
                1,
                wave
                    .GetProperty("Number")
                    .GetInt32());

            Assert.Equal(
                "platform-parent",
                wave
                    .GetProperty("Repositories")[0]
                    .GetString());

            Assert.Equal(
                "Modernise the shared parent.",
                wave
                    .GetProperty("Objective")
                    .GetString());

            Assert.Equal(
                "Dependent repositories inherit from it.",
                wave
                    .GetProperty("Rationale")
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidResult_WritesRisksAndRecommendations()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            var strategy =
                document.RootElement
                    .GetProperty("Strategy");

            Assert.Equal(
                "Shared dependency changes may affect downstream applications.",
                strategy
                    .GetProperty("KeyRisks")[0]
                    .GetString());

            Assert.Equal(
                "Validate each migration wave before starting the next.",
                strategy
                    .GetProperty("Recommendations")[0]
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidResult_WritesRepositoryAnalyses()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            var analyses =
                document.RootElement
                    .GetProperty("RepositoryAnalyses");

            Assert.Equal(
                2,
                analyses.GetArrayLength());

            Assert.Equal(
                "platform-parent",
                analyses[0]
                    .GetProperty("RepositoryName")
                    .GetString());

            Assert.Equal(
                "customer-service",
                analyses[1]
                    .GetProperty("RepositoryName")
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidResult_WritesRepositoryFindingsAndActions()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            var json = await File.ReadAllTextAsync(
                outputPath);

            using var document =
                JsonDocument.Parse(json);

            var analysis =
                document.RootElement
                    .GetProperty("RepositoryAnalyses")[1];

            Assert.Equal(
                "Application migration follows the shared platform components.",
                analysis
                    .GetProperty("Summary")
                    .GetString());

            Assert.Equal(
                "The application depends on shared-security.",
                analysis
                    .GetProperty("Findings")[0]
                    .GetString());

            Assert.Equal(
                "Validate the application after its upstream dependencies are migrated.",
                analysis
                    .GetProperty("RecommendedActions")[0]
                    .GetString());
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesOutputDirectory()
    {
        var rootDirectory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                rootDirectory,
                "nested",
                "migration-analysis.json");

            var writer = new MigrationAnalysisResultWriter();

            await writer.WriteAsync(
                CreateResult(),
                outputPath);

            Assert.True(
                File.Exists(outputPath));
        }
        finally
        {
            DeleteDirectory(rootDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_NullResult_ThrowsArgumentNullException()
    {
        var writer = new MigrationAnalysisResultWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                null!,
                "migration-analysis.json"));
    }

    [Fact]
    public async Task WriteAsync_EmptyPath_ThrowsArgumentException()
    {
        var writer = new MigrationAnalysisResultWriter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => writer.WriteAsync(
                CreateResult(),
                string.Empty));
    }

    private static MigrationAnalysisResult CreateResult()
    {
        return new MigrationAnalysisResult(
            Strategy:
                new MigrationStrategy(
                    Summary:
                        "Migrate shared components before dependent applications.",

                    Waves:
                    [
                        new MigrationStrategyWave(
                            Number: 1,
                            Repositories:
                            [
                                "platform-parent"
                            ],
                            Objective:
                                "Modernise the shared parent.",
                            Rationale:
                                "Dependent repositories inherit from it."),

                        new MigrationStrategyWave(
                            Number: 2,
                            Repositories:
                            [
                                "customer-service"
                            ],
                            Objective:
                                "Modernise the application.",
                            Rationale:
                                "The shared platform components are migrated first.")
                    ],

                    KeyRisks:
                    [
                        "Shared dependency changes may affect downstream applications."
                    ],

                    Recommendations:
                    [
                        "Validate each migration wave before starting the next."
                    ]),

            RepositoryAnalyses:
            [
                new MigrationAnalysis(
                    RepositoryName:
                        "platform-parent",
                    Summary:
                        "The shared parent is migrated first.",
                    Findings:
                    [
                        "The parent provides shared platform configuration."
                    ],
                    RecommendedActions:
                    [
                        "Validate the parent before migrating dependants."
                    ]),

                new MigrationAnalysis(
                    RepositoryName:
                        "customer-service",
                    Summary:
                        "Application migration follows the shared platform components.",
                    Findings:
                    [
                        "The application depends on shared-security."
                    ],
                    RecommendedActions:
                    [
                        "Validate the application after its upstream dependencies are migrated."
                    ])
            ]);
    }

    private static string CreateTemporaryOutputPath()
    {
        var directory =
            CreateTemporaryDirectory();

        return Path.Combine(
            directory,
            "migration-analysis.json");
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteOutputDirectory(
        string outputPath)
    {
        var directory =
            Path.GetDirectoryName(outputPath);

        if (directory is not null)
        {
            DeleteDirectory(directory);
        }
    }

    private static void DeleteDirectory(
        string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }
}
