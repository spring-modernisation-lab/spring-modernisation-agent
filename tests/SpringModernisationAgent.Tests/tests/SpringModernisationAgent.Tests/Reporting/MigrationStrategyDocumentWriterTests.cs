using DocumentFormat.OpenXml.Packaging;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Tests.Reporting;

public class MigrationStrategyDocumentWriterTests
{
    private const string PlatformCommitSha =
        "eb55eeab4022820c4029a6c21060c29872bda3eb";

    private const string CustomerCommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task WriteAsync_ValidInputs_CreatesReadableWordDocument()
    {
        var outputDirectory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                outputDirectory,
                "migration-strategy.docx");

            var writer =
                new MigrationStrategyDocumentWriter();

            await writer.WriteAsync(
                CreateProjectAssessment(),
                CreateMigrationAnalysis(),
                CreateTransformationPlans(),
                outputPath);

            Assert.True(File.Exists(outputPath));

            using var document =
                WordprocessingDocument.Open(
                    outputPath,
                    false);

            Assert.NotNull(
                document.MainDocumentPart);

            Assert.NotNull(
                document.MainDocumentPart!.Document.Body);
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsTitleAndOrganisation()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Spring Modernisation Migration Strategy",
                text);

            Assert.Contains(
                "spring-modernisation-lab",
                text);

            Assert.Contains(
                "Spring Boot 2.7 / Java 11",
                text);

            Assert.Contains(
                "Spring Boot 3.5 / Java 21",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsDeterministicRepositoryEvidence()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "platform-parent",
                text);

            Assert.Contains(
                "customer-service",
                text);

            Assert.Contains(
                PlatformCommitSha,
                text);

            Assert.Contains(
                CustomerCommitSha,
                text);

            Assert.Contains(
                "SpringBootApplication",
                text);

            Assert.Contains(
                "ParentPom",
                text);

            Assert.Contains(
                "Ready",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsDeterministicDependencyEvidence()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Dependency Overview",
                text);

            Assert.Contains(
                "platform-parent",
                text);

            Assert.Contains(
                "customer-service",
                text);

            Assert.Contains(
                "Parent",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsTransformationEvidence()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "spring-parent-2.7-to-3",
                text);

            Assert.Contains(
                "spring-boot-application-2.7-to-3",
                text);

            Assert.Contains(
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                text);

            Assert.Contains(
                "org.openrewrite.java.migrate.UpgradeToJava21",
                text);

            Assert.Contains(
                "org.openrewrite.recipe:rewrite-spring:6.43.0",
                text);

            Assert.Contains(
                "org.openrewrite.recipe:rewrite-migrate-java:3.47.0",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsValidationRequirements()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Validation Approach",
                text);

            Assert.Contains(
                "effective-pom",
                text);

            Assert.Contains(
                "dependency-resolution",
                text);

            Assert.Contains(
                "compile",
                text);

            Assert.Contains(
                "unit-tests",
                text);

            Assert.Contains(
                "application-context-test",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsAiGeneratedStrategyContent()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Migrate the shared platform components before dependent applications.",
                text);

            Assert.Contains(
                "Modernise the shared parent.",
                text);

            Assert.Contains(
                "Dependent repositories inherit from the shared parent.",
                text);

            Assert.Contains(
                "Major platform changes may expose compatibility issues.",
                text);

            Assert.Contains(
                "Validate each migration wave before starting the next.",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsRepositoryAnalysis()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Repository-Level Analysis",
                text);

            Assert.Contains(
                "The parent establishes the shared migration baseline.",
                text);

            Assert.Contains(
                "The application follows its upstream dependencies.",
                text);

            Assert.Contains(
                "The application depends on platform-parent.",
                text);

            Assert.Contains(
                "Migrate only after the parent has been validated.",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_UsesDeterministicWaveMembership()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Wave 1",
                text);

            Assert.Contains(
                "Repositories: platform-parent",
                text);

            Assert.Contains(
                "Wave 2",
                text);

            Assert.Contains(
                "Repositories: customer-service",
                text);
        }
        finally
        {
            DeleteOutputDirectory(outputPath);
        }
    }

    [Fact]
    public async Task WriteAsync_ValidInputs_ContainsExecutionControls()
    {
        var outputPath = CreateTemporaryOutputPath();

        try
        {
            await WriteDocumentAsync(outputPath);

            var text = ReadDocumentText(outputPath);

            Assert.Contains(
                "Execution Controls",
                text);

            Assert.Contains(
                "Baseline-build provenance must match the assessed commit.",
                text);

            Assert.Contains(
                "Only approved and pinned OpenRewrite recipes can execute.",
                text);

            Assert.Contains(
                "AI output cannot alter deterministic migration waves",
                text);
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
                "reports",
                "migration-strategy.docx");

            var writer =
                new MigrationStrategyDocumentWriter();

            await writer.WriteAsync(
                CreateProjectAssessment(),
                CreateMigrationAnalysis(),
                CreateTransformationPlans(),
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
    public async Task WriteAsync_NullProjectAssessment_ThrowsArgumentNullException()
    {
        var writer =
            new MigrationStrategyDocumentWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                null!,
                CreateMigrationAnalysis(),
                CreateTransformationPlans(),
                "migration-strategy.docx"));
    }

    [Fact]
    public async Task WriteAsync_NullMigrationAnalysis_ThrowsArgumentNullException()
    {
        var writer =
            new MigrationStrategyDocumentWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                CreateProjectAssessment(),
                null!,
                CreateTransformationPlans(),
                "migration-strategy.docx"));
    }

    [Fact]
    public async Task WriteAsync_NullTransformationPlans_ThrowsArgumentNullException()
    {
        var writer =
            new MigrationStrategyDocumentWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.WriteAsync(
                CreateProjectAssessment(),
                CreateMigrationAnalysis(),
                null!,
                "migration-strategy.docx"));
    }

    [Fact]
    public async Task WriteAsync_EmptyPath_ThrowsArgumentException()
    {
        var writer =
            new MigrationStrategyDocumentWriter();

        await Assert.ThrowsAsync<ArgumentException>(
            () => writer.WriteAsync(
                CreateProjectAssessment(),
                CreateMigrationAnalysis(),
                CreateTransformationPlans(),
                string.Empty));
    }

    [Fact]
    public async Task WriteAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var outputDirectory = CreateTemporaryDirectory();

        try
        {
            var outputPath = Path.Combine(
                outputDirectory,
                "migration-strategy.docx");

            using var cancellationTokenSource =
                new CancellationTokenSource();

            cancellationTokenSource.Cancel();

            var writer =
                new MigrationStrategyDocumentWriter();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => writer.WriteAsync(
                    CreateProjectAssessment(),
                    CreateMigrationAnalysis(),
                    CreateTransformationPlans(),
                    outputPath,
                    cancellationTokenSource.Token));
        }
        finally
        {
            DeleteDirectory(outputDirectory);
        }
    }

    private static async Task WriteDocumentAsync(
        string outputPath)
    {
        var writer =
            new MigrationStrategyDocumentWriter();

        await writer.WriteAsync(
            CreateProjectAssessment(),
            CreateMigrationAnalysis(),
            CreateTransformationPlans(),
            outputPath);
    }

    private static string ReadDocumentText(
        string path)
    {
        using var document =
            WordprocessingDocument.Open(
                path,
                false);

        return document.MainDocumentPart!
            .Document
            .Body!
            .InnerText;
    }

    private static ProjectAssessment CreateProjectAssessment()
    {
        var platformParent =
            CreateRepositoryAssessment(
                repositoryName: "platform-parent",
                commitSha: PlatformCommitSha,
                kind: RepositoryKind.ParentPom,
                groupId: "com.example.platform",
                artifactId: "platform-parent",
                dependencyEdges: [],
                route: CreateParentRoute());

        var customerService =
            CreateRepositoryAssessment(
                repositoryName: "customer-service",
                commitSha: CustomerCommitSha,
                kind: RepositoryKind.SpringBootApplication,
                groupId: "com.example.services",
                artifactId: "customer-service",
                dependencyEdges:
                [
                    new RepositoryDependencyEdge(
                        FromRepository: "customer-service",
                        ToRepository: "platform-parent",
                        Type: RepositoryDependencyType.Parent,
                        Version: "1.0.0")
                ],
                route: CreateApplicationRoute());

        return new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: new DateTimeOffset(
                2026,
                10,
                30,
                11,
                25,
                0,
                TimeSpan.Zero),
            Repositories:
            [
                platformParent,
                customerService
            ],
            DependencyGraph:
                new RepositoryDependencyGraph(
                    Repositories:
                    [
                        "platform-parent",
                        "customer-service"
                    ],
                    Edges:
                    [
                        ..customerService.InternalDependencies
                    ]),
            MigrationWaves:
                new MigrationWavePlan(
                [
                    new MigrationWave(
                        Number: 1,
                        Repositories:
                        [
                            "platform-parent"
                        ]),
                    new MigrationWave(
                        Number: 2,
                        Repositories:
                        [
                            "customer-service"
                        ])
                ]));
    }

    private static RepositoryAssessment CreateRepositoryAssessment(
        string repositoryName,
        string commitSha,
        RepositoryKind kind,
        string groupId,
        string artifactId,
        IReadOnlyList<RepositoryDependencyEdge> dependencyEdges,
        SpringMigrationRoute route)
    {
        return new RepositoryAssessment(
            RepositoryName: repositoryName,
            CommitSha: commitSha,
            Kind: kind,
            MavenProject:
                new MavenProject(
                    Coordinate:
                        new MavenCoordinate(
                            groupId,
                            artifactId,
                            "1.0.0"),
                    Parent: null,
                    Packaging:
                        kind == RepositoryKind.ParentPom
                            ? "pom"
                            : "jar",
                    Name: repositoryName,
                    Properties:
                        new Dictionary<string, string>(),
                    Dependencies: []),
            Spring:
                new SpringPlatform(
                    SpringBootVersion: "2.7.18",
                    JavaVersion: "11",
                    Starters: []),
            InternalDependencies:
                dependencyEdges.ToList(),
            ExternalDependencies: [],
            MigrationRoute:
                new MigrationRouteSelection(
                    Status:
                        MigrationRouteSelectionStatus.Matched,
                    Route: route,
                    Reason: null),
            MigrationReadiness:
                new MigrationReadiness(
                    Status:
                        MigrationReadinessStatus.Ready,
                    Reasons: []));
    }

    private static SpringMigrationRoute CreateParentRoute()
    {
        return new SpringMigrationRoute
        {
            Id = "spring-parent-2.7-to-3",

            Source = new SpringSourceCriteria
            {
                RepositoryKinds =
                [
                    RepositoryKind.ParentPom
                ],
                SpringBootMinimum = "2.7.0",
                SpringBootMaximum = "2.7.x"
            },

            Target = new SpringTarget
            {
                SpringBoot = "3.5.x",
                Java = "21"
            },

            OpenRewrite =
                CreateOpenRewriteConfiguration(),

            Requirements =
                new MigrationRequirements
                {
                    BuildSystems = ["maven"],
                    BaselineBuildMustPass = true
                },

            Validation =
            [
                "effective-pom",
                "dependency-resolution"
            ]
        };
    }

    private static SpringMigrationRoute CreateApplicationRoute()
    {
        return new SpringMigrationRoute
        {
            Id =
                "spring-boot-application-2.7-to-3",

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

            OpenRewrite =
                CreateOpenRewriteConfiguration(),

            Requirements =
                new MigrationRequirements
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
    }

    private static OpenRewriteConfiguration
        CreateOpenRewriteConfiguration()
    {
        return new OpenRewriteConfiguration
        {
            Recipes =
            [
                new OpenRewriteRecipeConfiguration
                {
                    Recipe =
                        "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                    Artifact =
                        "org.openrewrite.recipe:rewrite-spring",
                    Version = "6.43.0"
                },

                new OpenRewriteRecipeConfiguration
                {
                    Recipe =
                        "org.openrewrite.java.migrate.UpgradeToJava21",
                    Artifact =
                        "org.openrewrite.recipe:rewrite-migrate-java",
                    Version = "3.47.0"
                }
            ]
        };
    }

    private static IReadOnlyList<TransformationPlan>
        CreateTransformationPlans()
    {
        return
        [
            new TransformationPlan(
                RepositoryName: "platform-parent",
                SourceCommitSha: PlatformCommitSha,
                MigrationWave: 1,
                RepositoryKind: RepositoryKind.ParentPom,
                MigrationRouteId:
                    "spring-parent-2.7-to-3",
                Target:
                    new TransformationTarget(
                        SpringBootVersion: "3.5.x",
                        JavaVersion: "21"),
                OpenRewrite:
                [
                    ..CreateOpenRewriteTransformations()
                ],
                Validation:
                [
                    "effective-pom",
                    "dependency-resolution"
                ]),

            new TransformationPlan(
                RepositoryName: "customer-service",
                SourceCommitSha: CustomerCommitSha,
                MigrationWave: 2,
                RepositoryKind:
                    RepositoryKind.SpringBootApplication,
                MigrationRouteId:
                    "spring-boot-application-2.7-to-3",
                Target:
                    new TransformationTarget(
                        SpringBootVersion: "3.5.x",
                        JavaVersion: "21"),
                OpenRewrite:
                [
                    ..CreateOpenRewriteTransformations()
                ],
                Validation:
                [
                    "compile",
                    "unit-tests",
                    "application-context-test"
                ])
        ];
    }

    private static IEnumerable<OpenRewriteTransformation>
        CreateOpenRewriteTransformations()
    {
        yield return new OpenRewriteTransformation(
            Recipe:
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
            Artifact:
                "org.openrewrite.recipe:rewrite-spring",
            Version: "6.43.0");

        yield return new OpenRewriteTransformation(
            Recipe:
                "org.openrewrite.java.migrate.UpgradeToJava21",
            Artifact:
                "org.openrewrite.recipe:rewrite-migrate-java",
            Version: "3.47.0");
    }

    private static MigrationAnalysisResult
        CreateMigrationAnalysis()
    {
        return new MigrationAnalysisResult(
            Strategy:
                new MigrationStrategy(
                    Summary:
                        "Migrate the shared platform components before dependent applications.",

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
                                "Dependent repositories inherit from the shared parent."),

                        new MigrationStrategyWave(
                            Number: 2,
                            Repositories:
                            [
                                "customer-service"
                            ],
                            Objective:
                                "Modernise the application after its upstream dependency.",
                            Rationale:
                                "The application depends on the shared parent and should follow it.")
                    ],

                    KeyRisks:
                    [
                        "Major platform changes may expose compatibility issues."
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
                        "The parent establishes the shared migration baseline.",
                    Findings:
                    [
                        "The parent is migrated before downstream repositories."
                    ],
                    RecommendedActions:
                    [
                        "Validate the parent before progressing to later waves."
                    ]),

                new MigrationAnalysis(
                    RepositoryName:
                        "customer-service",
                    Summary:
                        "The application follows its upstream dependencies.",
                    Findings:
                    [
                        "The application depends on platform-parent."
                    ],
                    RecommendedActions:
                    [
                        "Migrate only after the parent has been validated."
                    ])
            ]);
    }

    private static string CreateTemporaryOutputPath()
    {
        var directory =
            CreateTemporaryDirectory();

        return Path.Combine(
            directory,
            "migration-strategy.docx");
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
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(
                filePath,
                FileAttributes.Normal);
        }

        Directory.Delete(
            path,
            recursive: true);
    }
}