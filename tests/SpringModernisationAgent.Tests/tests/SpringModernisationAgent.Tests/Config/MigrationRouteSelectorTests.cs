using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Config;

public class MigrationRouteSelectorTests
{
    [Fact]
    public void Select_SpringBoot2718_ReturnsMatchingRoute()
    {
        var policy = CreatePolicy();

        var selector = new MigrationRouteSelector();

        var result = selector.Select(
            RepositoryKind.SpringBootApplication,
            "2.7.18",
            policy);

        Assert.Equal(MigrationRouteSelectionStatus.Matched, result.Status);
        Assert.NotNull(result.Route);
        Assert.Equal("spring-boot-2.7-to-3", result.Route.Id);
        Assert.Equal("3.5.x", result.Route.Target.SpringBoot);
        Assert.Equal("21", result.Route.Target.Java);
    }

    [Fact]
    public void Select_SpringBoot2615_ReturnsNoMatch()
    {
        var policy = CreatePolicy();

        var selector = new MigrationRouteSelector();

        var result = selector.Select(RepositoryKind.SpringBootApplication, "2.6.15", policy);

        Assert.Equal(MigrationRouteSelectionStatus.NoMatch, result.Status);
        Assert.Null(result.Route);
    }

    [Fact]
    public void Select_UnknownVersion_ReturnsNoMatch()
    {
        var policy = CreatePolicy();

        var selector = new MigrationRouteSelector();

        var result = selector.Select(RepositoryKind.SpringBootApplication, null, policy);

        Assert.Equal(MigrationRouteSelectionStatus.NoMatch, result.Status);
        Assert.Null(result.Route);
        Assert.Equal("Spring Boot version is unknown.", result.Reason);
    }

    [Fact]
    public void Select_OverlappingRoutes_ReturnsAmbiguous()
    {
        var policy = CreatePolicy();

        policy.Routes.Add(CreateRoute(
            id: "another-2.7-route",
            minimum: "2.7.10",
            maximum: "2.7.x"));

        var selector = new MigrationRouteSelector();

        var result = selector.Select(RepositoryKind.SpringBootApplication, "2.7.18", policy);

        Assert.Equal(MigrationRouteSelectionStatus.Ambiguous, result.Status);
        Assert.Null(result.Route);
    }

    [Fact]
    public void Select_CorrectVersionButWrongRepositoryKind_ReturnsNoMatch()
    {
        var policy = CreatePolicy();
        var selector = new MigrationRouteSelector();

        var result = selector.Select(
            RepositoryKind.JavaLibrary,
            "2.7.18",
            policy);

        Assert.Equal(MigrationRouteSelectionStatus.NoMatch, result.Status);
        Assert.Null(result.Route);
    }

    private static SpringMigrationPolicy CreatePolicy()
    {
        return new SpringMigrationPolicy
        {
            Routes =
            [
                CreateRoute(
                    id: "spring-boot-2.7-to-3",
                    minimum: "2.7.0",
                    maximum: "2.7.x")
            ]
        };
    }

    private static SpringMigrationRoute CreateRoute(
        string id,
        string minimum,
        string maximum)
    {
        return new SpringMigrationRoute
        {
            Id = id,

            Source = new SpringSourceCriteria
            {
                RepositoryKinds =
                [
                    RepositoryKind.SpringBootApplication
                ],
                SpringBootMinimum = minimum,
                SpringBootMaximum = maximum
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

            Validation =
            [
                "compile",
                "unit-tests"
            ]
        };
    }
}
