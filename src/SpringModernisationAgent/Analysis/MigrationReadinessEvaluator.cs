using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class MigrationReadinessEvaluator
{
    public MigrationReadiness Evaluate(RepositoryAssessment repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var reasons = new List<string>();

        EvaluateMigrationRoute(repository, reasons);
        EvaluateBaselineBuild(repository, reasons);
        EvaluateCommitProvenance(repository, reasons);

        return reasons.Count == 0
            ? new MigrationReadiness(
                MigrationReadinessStatus.Ready,
                Reasons: [])
            : new MigrationReadiness(
                MigrationReadinessStatus.Blocked,
                Reasons: reasons);
    }

    private static void EvaluateMigrationRoute(
        RepositoryAssessment repository,
        ICollection<string> reasons)
    {
        if (repository.MigrationRoute is null)
        {
            reasons.Add(
                "Migration policy has not been evaluated.");

            return;
        }

        switch (repository.MigrationRoute.Status)
        {
            case MigrationRouteSelectionStatus.Matched:
                return;

            case MigrationRouteSelectionStatus.NoMatch:
                reasons.Add(
                    repository.MigrationRoute.Reason
                    ?? "No approved migration route matched.");

                return;

            case MigrationRouteSelectionStatus.Ambiguous:
                reasons.Add(
                    repository.MigrationRoute.Reason
                    ?? "Multiple approved migration routes matched.");

                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(repository),
                    repository.MigrationRoute.Status,
                    "Unsupported migration route selection status.");
        }
    }

    private static void EvaluateBaselineBuild(
        RepositoryAssessment repository,
        ICollection<string> reasons)
    {
        var route = repository.MigrationRoute?.Route;

        if (route is null)
        {
            return;
        }

        if (!route.Requirements.BaselineBuildMustPass)
        {
            return;
        }

        if (repository.BaselineBuildValidation is null)
        {
            reasons.Add(
                "The migration route requires baseline build validation, but no validation result is available.");

            return;
        }

        if (!repository.BaselineBuildValidation.CanProceed)
        {
            reasons.Add(
                repository.BaselineBuildValidation.Reason
                ?? "The required baseline build validation did not pass.");
        }
    }

    private static void EvaluateCommitProvenance(
        RepositoryAssessment repository,
        ICollection<string> reasons)
    {
        if (repository.CommitProvenance is null)
        {
            reasons.Add(
                "Commit provenance has not been validated.");

            return;
        }

        if (!repository.CommitProvenance.IsValid)
        {
            reasons.Add(
                repository.CommitProvenance.Reason
                ?? "Commit provenance validation failed.");
        }
    }
}