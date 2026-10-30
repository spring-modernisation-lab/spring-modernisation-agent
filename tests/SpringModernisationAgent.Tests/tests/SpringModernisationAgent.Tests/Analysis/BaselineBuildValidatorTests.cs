using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class BaselineBuildValidatorTests
{
    [Fact]
    public void Validate_BuildRequiredAndPassed_ReturnsSatisfied()
    {
        var route = CreateRoute(baselineBuildMustPass: true);
        var buildResult = CreateBuildResult(BaselineBuildStatus.Passed, exitCode: 0);
        var validator = new BaselineBuildValidator();

        var result = validator.Validate(route, buildResult);

        Assert.Equal(BaselineBuildValidationStatus.Satisfied, result.Status);
        Assert.True(result.CanProceed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Validate_BuildRequiredAndFailed_ReturnsFailed()
    {
        var route = CreateRoute(baselineBuildMustPass: true);
        var buildResult = CreateBuildResult(BaselineBuildStatus.Failed, exitCode: 1);
        var validator = new BaselineBuildValidator();

        var result = validator.Validate(route, buildResult);

        Assert.Equal(BaselineBuildValidationStatus.Failed, result.Status);
        Assert.False(result.CanProceed);
        Assert.Equal("The baseline build failed.", result.Reason);
    }

    [Fact]
    public void Validate_BuildRequiredButNotRun_ReturnsNotRun()
    {
        var route = CreateRoute(baselineBuildMustPass: true);
        var buildResult = CreateBuildResult(BaselineBuildStatus.NotRun, exitCode: null);
        var validator = new BaselineBuildValidator();

        var result = validator.Validate(route, buildResult);

        Assert.Equal(BaselineBuildValidationStatus.NotRun, result.Status);
        Assert.False(result.CanProceed);

        Assert.Equal(
            "The migration route requires a successful baseline build, but the build has not been run.",
            result.Reason);
    }

    [Fact]
    public void Validate_BuildNotRequired_ReturnsNotRequired()
    {
        var route = CreateRoute(baselineBuildMustPass: false);
        var buildResult = CreateBuildResult(BaselineBuildStatus.NotRun, exitCode: null);
        var validator = new BaselineBuildValidator();

        var result = validator.Validate(route, buildResult);

        Assert.Equal(BaselineBuildValidationStatus.NotRequired, result.Status);
        Assert.True(result.CanProceed);
        Assert.Null(result.Reason);
    }

    private static SpringMigrationRoute CreateRoute(bool baselineBuildMustPass)
    {
        return new SpringMigrationRoute
        {
            Id = "spring-boot-application-2.7-to-3",
            Source = new SpringSourceCriteria
            {
                RepositoryKinds = [RepositoryKind.SpringBootApplication],
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

    private static BaselineBuildResult CreateBuildResult(
        BaselineBuildStatus status,
        int? exitCode)
    {
        return new BaselineBuildResult(
            RepositoryName: "customer-service",
            Status: status,
            ExitCode: exitCode,
            StartedAt: null,
            CompletedAt: null,
            StandardOutput: null,
            StandardError: null);
    }
}
