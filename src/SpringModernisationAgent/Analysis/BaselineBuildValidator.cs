using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class BaselineBuildValidator
{
    public BaselineBuildValidation Validate(
        SpringMigrationRoute route,
        BaselineBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(buildResult);

        if (!route.Requirements.BaselineBuildMustPass)
        {
            return new BaselineBuildValidation(
                BaselineBuildValidationStatus.NotRequired,
                Reason: null);
        }

        return buildResult.Status switch
        {
            BaselineBuildStatus.Passed =>
                new BaselineBuildValidation(
                    BaselineBuildValidationStatus.Satisfied,
                    Reason: null),

            BaselineBuildStatus.Failed =>
                new BaselineBuildValidation(
                    BaselineBuildValidationStatus.Failed,
                    "The baseline build failed."),

            BaselineBuildStatus.NotRun =>
                new BaselineBuildValidation(
                    BaselineBuildValidationStatus.NotRun,
                    "The migration route requires a successful baseline build, but the build has not been run."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(buildResult),
                buildResult.Status,
                "Unsupported baseline build status.")
        };
    }
}
