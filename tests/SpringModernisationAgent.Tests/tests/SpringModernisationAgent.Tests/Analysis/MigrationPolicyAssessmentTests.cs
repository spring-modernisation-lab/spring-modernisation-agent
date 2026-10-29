using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class MigrationPolicyAssessmentTests
{
    [Fact]
    public void Apply_AssignsMatchingMigrationRoute()
    {
        var repository = CreateRepositoryAssessment(
            "customer-service",
            springBootVersion: "2.7.18",
            javaVersion: "11");

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan([]));

        var policy = CreatePolicy();

        var selector = new MigrationRouteSelector();
        var policyAssessment = new MigrationPolicyAssessment(selector);

        var result = policyAssessment.Apply(assessment, policy);

        var assessedRepository = Assert.Single(result.Repositories);

        Assert.NotNull(assessedRepository.MigrationRoute);

        Assert.Equal(
            MigrationRouteSelectionStatus.Matched,
            assessedRepository.MigrationRoute.Status);

        Assert.NotNull(assessedRepository.MigrationRoute.Route);

        Assert.Equal(
            "spring-boot-2.7-to-3",
            assessedRepository.MigrationRoute.Route.Id);

        Assert.Equal(
            "3.5.x",
            assessedRepository.MigrationRoute.Route.Target.SpringBoot);

        Assert.Equal(
            "21",
            assessedRepository.MigrationRoute.Route.Target.Java);
    }

    [Fact]
    public void Apply_NoMatchingRoute_RecordsNoMatch()
    {
        var repository = CreateRepositoryAssessment(
            "customer-service",
            springBootVersion: "2.6.15",
            javaVersion: "11");

        var assessment = new ProjectAssessment(
            Organisation: "spring-modernisation-lab",
            CreatedAt: DateTimeOffset.UtcNow,
            Repositories: [repository],
            DependencyGraph: new RepositoryDependencyGraph(
                Repositories: ["customer-service"],
                Edges: []),
            MigrationWaves: new MigrationWavePlan([]));

        var policy = CreatePolicy();

        var selector = new MigrationRouteSelector();
        var policyAssessment = new MigrationPolicyAssessment(selector);

        var result = policyAssessment.Apply(assessment, policy);

        var assessedRepository = Assert.Single(result.Repositories);

        Assert.NotNull(assessedRepository.MigrationRoute);

        Assert.Equal(
            MigrationRouteSelectionStatus.NoMatch,
            assessedRepository.MigrationRoute.Status);

        Assert.Null(assessedRepository.MigrationRoute.Route);
    }

    private static RepositoryAssessment CreateRepositoryAssessment(
        string repositoryName,
        string springBootVersion,
        string javaVersion)
    {
        return new RepositoryAssessment(
            RepositoryName: repositoryName,
            Kind: RepositoryKind.SpringBootApplication,
            MavenProject: new MavenProject(
                Coordinate: new MavenCoordinate(
                    "com.example.services",
                    repositoryName,
                    "1.0.0"),
                Parent: null,
                Packaging: "jar",
                Name: repositoryName,
                Properties: new Dictionary<string, string>(),
                Dependencies: []),
            Spring: new SpringPlatform(
                SpringBootVersion: springBootVersion,
                JavaVersion: javaVersion,
                Starters: []),
            InternalDependencies: [],
            ExternalDependencies: []);
    }

    private static SpringMigrationPolicy CreatePolicy()
    {
        return new SpringMigrationPolicy
        {
            Routes =
            [
                new SpringMigrationRoute
                {
                    Id = "spring-boot-2.7-to-3",
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
                        Recipe = "<APPROVED_RECIPE>",
                        Artifact = "<APPROVED_ARTIFACT>",
                        Version = "<PINNED_VERSION>"
                    },
                    Requirements = new MigrationRequirements
                    {
                        BuildSystems = ["maven"],
                        BaselineBuildMustPass = true
                    },
                    Validation = ["compile", "unit-tests"]
                }
            ]
        };
    }
}
