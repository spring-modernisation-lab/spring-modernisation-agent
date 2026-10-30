using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class BaselineBuildAssessmentTests
{
    [Fact]
    public void Apply_PassedBuildAndRequiredBaseline_ReturnsSatisfied()
    {
        var repository = CreateRepositoryAssessment();

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan(
            [
                new MigrationWave(
                    1,
                    ["customer-service"])
            ]));

        var buildResults = new PortfolioBaselineBuildResults(
        [
            new PortfolioBaselineBuildResult(
                RepositoryName: "customer-service",
                CommitSha: "acb1234",
                Wave: 1,
                Operation: BaselineBuildOperation.Verify,
                Status: BaselineBuildStatus.Passed)
        ]);

        var validator = new BaselineBuildValidator();
        var provenanceValidator = new CommitProvenanceValidator();
        var baselineAssessment = new BaselineBuildAssessment(validator, provenanceValidator);

        var result = baselineAssessment.Apply(
            assessment,
            buildResults);

        var assessedRepository = Assert.Single(
            result.Repositories);

        Assert.NotNull(
            assessedRepository.BaselineBuild);

        Assert.Equal(
            BaselineBuildStatus.Passed,
            assessedRepository.BaselineBuild.Status);

        Assert.NotNull(
            assessedRepository.BaselineBuildValidation);

        Assert.Equal(
            BaselineBuildValidationStatus.Satisfied,
            assessedRepository.BaselineBuildValidation.Status);

        Assert.True(
            assessedRepository.BaselineBuildValidation.CanProceed);
    }

    private static RepositoryAssessment CreateRepositoryAssessment()
    {
        var route = new SpringMigrationRoute
        {
            Id = "spring-boot-application-2.7-to-3",

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

            OpenRewrite = new OpenRewriteConfiguration
            {
                Recipes =
                [
                    new OpenRewriteRecipeConfiguration
                    {
                        Recipe = "<APPROVED_APPLICATION_RECIPE>",
                        Artifact = "<APPROVED_ARTIFACT>",
                        Version = "<PINNED_VERSION>"
                    }
                ]
            },

            Requirements = new MigrationRequirements
            {
                BuildSystems = ["maven"],
                BaselineBuildMustPass = true
            },

            Validation =
            [
                "compile",
                "unit-tests"
            ]
        };

        return new RepositoryAssessment(
            RepositoryName: "customer-service",
            CommitSha: "abc123",
            Kind: RepositoryKind.SpringBootApplication,

            MavenProject: new MavenProject(
                Coordinate: new MavenCoordinate(
                    "com.example.services",
                    "customer-service",
                    "1.0.0"),
                Parent: null,
                Packaging: "jar",
                Name: "Customer Service",
                Properties: new Dictionary<string, string>(),
                Dependencies: []),

            Spring: new SpringPlatform(
                SpringBootVersion: "2.7.18",
                JavaVersion: "11",
                Starters:
                [
                    "spring-boot-starter-web"
                ]),

            InternalDependencies: [],
            ExternalDependencies: [],

            MigrationRoute: new MigrationRouteSelection(
                MigrationRouteSelectionStatus.Matched,
                route,
                Reason: null));
    }

    [Fact]
    public void Apply_FailedBuildAndRequiredBaseline_PreventsMigration()
    {
        var repository = CreateRepositoryAssessment();

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan(
            [
                new MigrationWave(
                1,
                ["customer-service"])
            ]));

        var buildResults = new PortfolioBaselineBuildResults(
        [
            new PortfolioBaselineBuildResult(
                RepositoryName: "customer-service",
                CommitSha: "acb1234",
                Wave: 1,
                Operation: BaselineBuildOperation.Verify,
                Status: BaselineBuildStatus.Failed)
        ]);

        var validator = new BaselineBuildValidator();
        var provenanceValidator = new CommitProvenanceValidator();
        var baselineAssessment = new BaselineBuildAssessment(validator, provenanceValidator);

        var result = baselineAssessment.Apply(
            assessment,
            buildResults);

        var assessedRepository = Assert.Single(
            result.Repositories);

        Assert.Equal(
            BaselineBuildStatus.Failed,
            assessedRepository.BaselineBuild!.Status);

        Assert.Equal(
            BaselineBuildValidationStatus.Failed,
            assessedRepository.BaselineBuildValidation!.Status);

        Assert.False(
            assessedRepository.BaselineBuildValidation.CanProceed);
    }

    [Fact]
    public void Apply_MissingBuildResult_RecordsMissingCommitProvenance()
    {
        var repository = CreateRepositoryAssessment();

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan(
            [
                new MigrationWave(
                1,
                ["customer-service"])
            ]));

        var buildResults = new PortfolioBaselineBuildResults([]);

        var buildValidator = new BaselineBuildValidator();
        var provenanceValidator = new CommitProvenanceValidator();

        var baselineAssessment = new BaselineBuildAssessment(
            buildValidator,
            provenanceValidator);

        var result = baselineAssessment.Apply(
            assessment,
            buildResults);

        var assessedRepository = Assert.Single(result.Repositories);

        Assert.Null(assessedRepository.BaselineBuild);
        Assert.NotNull(assessedRepository.CommitProvenance);

        Assert.Equal(
            CommitProvenanceStatus.Missing,
            assessedRepository.CommitProvenance.Status);

        Assert.False(
            assessedRepository.CommitProvenance.IsValid);

        Assert.Equal(
            repository.CommitSha,
            assessedRepository.CommitProvenance.AssessedCommitSha);

        Assert.Null(
            assessedRepository.CommitProvenance.BuiltCommitSha);
    }

    [Fact]
    public void Apply_DifferentBuildCommit_RecordsMismatchedCommitProvenance()
    {
        var repository = CreateRepositoryAssessment();

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan(
            [
                new MigrationWave(
                1,
                ["customer-service"])
            ]));

        var buildResults = new PortfolioBaselineBuildResults(
        [
            new PortfolioBaselineBuildResult(
            RepositoryName: "customer-service",
            CommitSha: "def456",
            Wave: 1,
            Operation: BaselineBuildOperation.Verify,
            Status: BaselineBuildStatus.Passed)
        ]);

        var buildValidator = new BaselineBuildValidator();
        var provenanceValidator = new CommitProvenanceValidator();

        var baselineAssessment = new BaselineBuildAssessment(
            buildValidator,
            provenanceValidator);

        var result = baselineAssessment.Apply(
            assessment,
            buildResults);

        var assessedRepository = Assert.Single(result.Repositories);

        Assert.NotNull(assessedRepository.CommitProvenance);

        Assert.Equal(
            CommitProvenanceStatus.Mismatched,
            assessedRepository.CommitProvenance.Status);

        Assert.False(
            assessedRepository.CommitProvenance.IsValid);

        Assert.Equal(
            "abc123",
            assessedRepository.CommitProvenance.AssessedCommitSha);

        Assert.Equal(
            "def456",
            assessedRepository.CommitProvenance.BuiltCommitSha);
    }

}
