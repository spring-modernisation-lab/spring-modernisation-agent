using SpringModernisationAgent.AI;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.AI;

public class MigrationAnalysisInputBuilderTests
{
    private const string CustomerCommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public void Build_MapsRepositoryAssessment()
    {
        var assessment = CreateProjectAssessment();

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            []);

        var repository = Assert.Single(
            result.Repositories);

        Assert.Equal(
            "spring-modernisation-lab",
            result.Organisation);

        Assert.Equal(
            "customer-service",
            repository.RepositoryName);

        Assert.Equal(
            CustomerCommitSha,
            repository.CommitSha);

        Assert.Equal(
            "SpringBootApplication",
            repository.RepositoryKind);

        Assert.Equal(
            "2.7.18",
            repository.SpringBootVersion);

        Assert.Equal(
            "11",
            repository.JavaVersion);

        Assert.Equal(
            "3.5.x",
            repository.Target.SpringBootVersion);

        Assert.Equal(
            "21",
            repository.Target.JavaVersion);

        Assert.Equal(
            "spring-boot-application-2.7-to-3",
            repository.MigrationRouteId);
    }

    [Fact]
    public void Build_MapsInternalDependencies()
    {
        var assessment = CreateProjectAssessment();

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            []);

        var repository = Assert.Single(
            result.Repositories);

        Assert.Equal(
            [
                "platform-parent",
                "shared-security"
            ],
            repository.InternalDependencies);
    }

    [Fact]
    public void Build_MapsReadinessEvidence()
    {
        var assessment = CreateProjectAssessment();

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            []);

        var repository = Assert.Single(
            result.Repositories);

        Assert.Equal(
            "Ready",
            repository.ReadinessStatus);

        Assert.Empty(
            repository.ReadinessReasons);
    }

    [Fact]
    public void Build_MapsTransformationEvidence()
    {
        var assessment = CreateProjectAssessment();

        var transformationResult =
            CreateSuccessfulTransformationResult();

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            [transformationResult]);

        var repository = Assert.Single(
            result.Repositories);

        Assert.NotNull(
            repository.Transformation);

        Assert.Equal(
            "Succeeded",
            repository.Transformation.Status);

        Assert.Equal(
            [
                "org.openrewrite.java.spring.boot3.UpgradeSpringBoot_3_5",
                "org.openrewrite.java.migrate.UpgradeToJava21"
            ],
            repository.Transformation.AppliedRecipes);

        Assert.Equal(
            [
                "pom.xml",
                "src/main/java/com/example/CustomerService.java"
            ],
            repository.Transformation.ChangedFiles);

        Assert.Equal(
            "Passed",
            repository.Transformation.ValidationStatus);
    }

    [Fact]
    public void Build_MapsFailedValidationDiagnostics()
    {
        var assessment = CreateProjectAssessment();

        var transformationResult =
            new TransformationExecutionResult(
                Transformation:
                    CreateTransformationResult(),
                Validation:
                    new TransformationValidationResult(
                        RepositoryName: "customer-service",
                        SourceCommitSha: CustomerCommitSha,
                        Status:
                            TransformationValidationStatus.Failed,
                        Checks:
                        [
                            new TransformationValidationCheckResult(
                                Check: "compile",
                                Passed: true,
                                StandardOutput: "Compile passed.",
                                StandardError: null),

                            new TransformationValidationCheckResult(
                                Check: "unit-tests",
                                Passed: false,
                                StandardOutput: "Test output.",
                                StandardError: "Tests failed.")
                        ]));

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            [transformationResult]);

        var repository = Assert.Single(
            result.Repositories);

        var transformation =
            Assert.IsType<MigrationAnalysisTransformationInput>(
                repository.Transformation);

        Assert.Equal(
            "Failed",
            transformation.ValidationStatus);

        Assert.Equal(
            2,
            transformation.ValidationChecks.Count);

        var failedCheck =
            transformation.ValidationChecks[1];

        Assert.Equal(
            "unit-tests",
            failedCheck.Check);

        Assert.False(
            failedCheck.Passed);

        Assert.Equal(
            "Test output.",
            failedCheck.StandardOutput);

        Assert.Equal(
            "Tests failed.",
            failedCheck.StandardError);
    }

    [Fact]
    public void Build_NoTransformationResult_TransformationIsNull()
    {
        var assessment = CreateProjectAssessment();

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            []);

        var repository = Assert.Single(
            result.Repositories);

        Assert.Null(
            repository.Transformation);
    }

    [Fact]
    public void Build_PreservesMigrationWaveOrdering()
    {
        var assessment = CreateProjectAssessment() with
        {
            MigrationWaves = new MigrationWavePlan(
            [
                new MigrationWave(
                    Number: 3,
                    Repositories:
                    [
                        "account-service",
                        "customer-service"
                    ]),

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
                        "shared-security"
                    ])
            ])
        };

        var builder = new MigrationAnalysisInputBuilder();

        var result = builder.Build(
            assessment,
            []);

        Assert.Equal(
            [1, 2, 3],
            result.Waves.Select(
                wave => wave.Number));

        Assert.Equal(
            ["platform-parent"],
            result.Waves[0].Repositories);

        Assert.Equal(
            ["shared-security"],
            result.Waves[1].Repositories);

        Assert.Equal(
            [
                "account-service",
                "customer-service"
            ],
            result.Waves[2].Repositories);
    }

    [Fact]
    public void Build_DuplicateTransformationResults_ThrowsArgumentException()
    {
        var assessment = CreateProjectAssessment();

        var transformationResult =
            CreateSuccessfulTransformationResult();

        var builder = new MigrationAnalysisInputBuilder();

        Assert.Throws<ArgumentException>(
            () => builder.Build(
                assessment,
                [
                    transformationResult,
                    transformationResult
                ]));
    }

    [Fact]
    public void Build_NullProjectAssessment_ThrowsArgumentNullException()
    {
        var builder = new MigrationAnalysisInputBuilder();

        Assert.Throws<ArgumentNullException>(
            () => builder.Build(
                null!,
                []));
    }

    [Fact]
    public void Build_NullTransformationResults_ThrowsArgumentNullException()
    {
        var builder = new MigrationAnalysisInputBuilder();

        Assert.Throws<ArgumentNullException>(
            () => builder.Build(
                CreateProjectAssessment(),
                null!));
    }

    private static ProjectAssessment CreateProjectAssessment()
    {
        var route = CreateMigrationRoute();

        var repository = new RepositoryAssessment(
            RepositoryName: "customer-service",
            CommitSha: CustomerCommitSha,
            Kind:
                RepositoryKind.SpringBootApplication,

            MavenProject: new MavenProject(
                Coordinate: new MavenCoordinate(
                    "com.example.services",
                    "customer-service",
                    "1.0.0"),
                Parent: null,
                Packaging: "jar",
                Name: "Customer Service",
                Properties:
                    new Dictionary<string, string>(),
                Dependencies: []),

            Spring: new SpringPlatform(
                SpringBootVersion: "2.7.18",
                JavaVersion: "11",
                Starters:
                [
                    "spring-boot-starter-web"
                ]),

            InternalDependencies:
            [
                new RepositoryDependencyEdge(
                    FromRepository: "customer-service",
                    ToRepository: "shared-security",
                    Type:
                        RepositoryDependencyType.Compile,
                    Version: "1.0.0"),

                new RepositoryDependencyEdge(
                    FromRepository: "customer-service",
                    ToRepository: "platform-parent",
                    Type:
                        RepositoryDependencyType.Parent,
                    Version: "1.0.0")
            ],

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

        return new ProjectAssessment(
            Organisation:
                "spring-modernisation-lab",
            CreatedAt:
                DateTimeOffset.UtcNow,
            Repositories:
            [
                repository
            ],

            DependencyGraph:
                new RepositoryDependencyGraph(
                    Repositories:
                    [
                        "customer-service"
                    ],
                    Edges:
                    [
                        ..repository.InternalDependencies
                    ]),

            MigrationWaves:
                new MigrationWavePlan(
                [
                    new MigrationWave(
                        Number: 3,
                        Repositories:
                        [
                            "customer-service"
                        ])
                ]));
    }

    private static SpringMigrationRoute CreateMigrationRoute()
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
                new OpenRewriteConfiguration
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
                },

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

    private static TransformationExecutionResult
        CreateSuccessfulTransformationResult()
    {
        return new TransformationExecutionResult(
            Transformation:
                CreateTransformationResult(),

            Validation:
                new TransformationValidationResult(
                    RepositoryName: "customer-service",
                    SourceCommitSha: CustomerCommitSha,
                    Status:
                        TransformationValidationStatus.Passed,
                    Checks:
                    [
                        new TransformationValidationCheckResult(
                            Check: "compile",
                            Passed: true,
                            StandardOutput: "Compile passed.",
                            StandardError: null),

                        new TransformationValidationCheckResult(
                            Check: "unit-tests",
                            Passed: true,
                            StandardOutput: "Tests passed.",
                            StandardError: null)
                    ]));
    }

    private static TransformationResult
        CreateTransformationResult()
    {
        return new TransformationResult(
            RepositoryName: "customer-service",
            SourceCommitSha: CustomerCommitSha,
            Status:
                TransformationStatus.Succeeded,

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

            StandardOutput:
                "OpenRewrite completed.",

            StandardError: null);
    }
}