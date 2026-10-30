using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class MigrationReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_MatchedRouteAndSatisfiedBaseline_ReturnsReady()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: BaselineBuildValidationStatus.Satisfied);

        repository = repository with
        {
            CommitProvenance = new CommitProvenance(
                    Status: CommitProvenanceStatus.Matched,
                    AssessedCommitSha: "abc123",
                    BuiltCommitSha: "abc123",
                    Reason: null)
        };

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Ready, result.Status);
        Assert.True(result.CanProceed);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Evaluate_NoMatchingRoute_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.NoMatch,
            baselineStatus: null);

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Blocked, result.Status);
        Assert.False(result.CanProceed);

        Assert.Contains(
            "No approved migration route matches this repository.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_AmbiguousRoute_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Ambiguous,
            baselineStatus: null);

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Blocked, result.Status);
        Assert.False(result.CanProceed);

        Assert.Contains(
            "Multiple approved migration routes match this repository.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_MigrationPolicyNotEvaluated_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: null,
            baselineStatus: null);

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Blocked, result.Status);
        Assert.False(result.CanProceed);

        Assert.Contains(
            "Migration policy has not been evaluated.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_BaselineValidationMissing_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: null);

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Blocked, result.Status);
        Assert.False(result.CanProceed);

        Assert.Contains(
            "The migration route requires baseline build validation, but no validation result is available.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_BaselineBuildFailed_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: BaselineBuildValidationStatus.Failed);

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(MigrationReadinessStatus.Blocked, result.Status);
        Assert.False(result.CanProceed);

        Assert.Contains(
            "The baseline build failed.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_BaselineBuildNotRequired_ReturnsReady()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: null,
            baselineBuildMustPass: false);

        repository = repository with
        {
            CommitProvenance = new CommitProvenance(
                Status: CommitProvenanceStatus.Matched,
                AssessedCommitSha: "abc123",
                BuiltCommitSha: "abc123",
                Reason: null)
        };

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(
            MigrationReadinessStatus.Ready,
            result.Status);

        Assert.True(result.CanProceed);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Evaluate_MissingCommitProvenance_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: BaselineBuildValidationStatus.Satisfied);

        repository = repository with
        {
            CommitProvenance = null
        };

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(
            MigrationReadinessStatus.Blocked,
            result.Status);

        Assert.False(result.CanProceed);

        Assert.Contains(
            "Commit provenance has not been validated.",
            result.Reasons);
    }

    [Fact]
    public void Evaluate_MismatchedCommitProvenance_ReturnsBlocked()
    {
        var repository = CreateRepositoryAssessment(
            routeStatus: MigrationRouteSelectionStatus.Matched,
            baselineStatus: BaselineBuildValidationStatus.Satisfied);

        repository = repository with
        {
            CommitProvenance = new CommitProvenance(
                Status: CommitProvenanceStatus.Mismatched,
                AssessedCommitSha: "abc123",
                BuiltCommitSha: "def456",
                Reason:
                    "The baseline build was run against a different commit from the assessed repository revision.")
        };

        var evaluator = new MigrationReadinessEvaluator();

        var result = evaluator.Evaluate(repository);

        Assert.Equal(
            MigrationReadinessStatus.Blocked,
            result.Status);

        Assert.False(result.CanProceed);

        Assert.Contains(
            "The baseline build was run against a different commit from the assessed repository revision.",
            result.Reasons);
    }

    private static RepositoryAssessment CreateRepositoryAssessment(
        MigrationRouteSelectionStatus? routeStatus,
        BaselineBuildValidationStatus? baselineStatus,
        bool baselineBuildMustPass = true)
    {
        MigrationRouteSelection? routeSelection = null;

        if (routeStatus is not null)
        {
            var route = routeStatus == MigrationRouteSelectionStatus.Matched
                ? CreateRoute(baselineBuildMustPass)
                : null;

            var reason = routeStatus switch
            {
                MigrationRouteSelectionStatus.NoMatch =>
                    "No approved migration route matches this repository.",

                MigrationRouteSelectionStatus.Ambiguous =>
                    "Multiple approved migration routes match this repository.",

                _ => null
            };

            routeSelection = new MigrationRouteSelection(
                routeStatus.Value,
                route,
                reason);
        }

        BaselineBuildValidation? baselineValidation = null;

        if (baselineStatus is not null)
        {
            var reason = baselineStatus switch
            {
                BaselineBuildValidationStatus.Failed =>
                    "The baseline build failed.",

                BaselineBuildValidationStatus.NotRun =>
                    "The baseline build has not been run.",

                _ => null
            };

            baselineValidation = new BaselineBuildValidation(
                baselineStatus.Value,
                reason);
        }

        return new RepositoryAssessment(
            RepositoryName: "customer-service",
            CommitSha: "abc1234",
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
                Starters: ["spring-boot-starter-web"]),
            InternalDependencies: [],
            ExternalDependencies: [],
            MigrationRoute: routeSelection,
            BaselineBuildValidation: baselineValidation);
    }

    private static SpringMigrationRoute CreateRoute(
        bool baselineBuildMustPass)
    {
        return new SpringMigrationRoute
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
                BaselineBuildMustPass = baselineBuildMustPass
            },
            Validation = ["compile", "unit-tests"]
        };
    }
}
