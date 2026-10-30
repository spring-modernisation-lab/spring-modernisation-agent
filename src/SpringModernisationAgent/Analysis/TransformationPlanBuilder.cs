using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class TransformationPlanBuilder
{
    private readonly OpenRewriteConfigurationValidator _openRewriteValidator;

    public TransformationPlanBuilder(
        OpenRewriteConfigurationValidator openRewriteValidator)
    {
        _openRewriteValidator = openRewriteValidator;
    }

    public TransformationPlan Build(
        RepositoryAssessment repository,
        MigrationWavePlan migrationWaves)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(migrationWaves);

        EnsureReady(repository);

        var route = repository.MigrationRoute?.Route
            ?? throw new InvalidOperationException(
                $"Repository '{repository.RepositoryName}' has no approved migration route.");

        _openRewriteValidator.Validate(route.OpenRewrite);

        if (string.IsNullOrWhiteSpace(repository.CommitSha))
        {
            throw new InvalidOperationException(
                $"Repository '{repository.RepositoryName}' has no source commit SHA.");
        }

        var migrationWave = FindMigrationWave(
            repository.RepositoryName,
            migrationWaves);

        return new TransformationPlan(
            RepositoryName: repository.RepositoryName,
            SourceCommitSha: repository.CommitSha,
            MigrationWave: migrationWave,
            RepositoryKind: repository.Kind,
            MigrationRouteId: route.Id,
            Target: new TransformationTarget(
                SpringBootVersion: route.Target.SpringBoot,
                JavaVersion: route.Target.Java),
            OpenRewrite: route.OpenRewrite.Recipes
                .Select(recipe => new OpenRewriteTransformation(
                    Recipe: recipe.Recipe,
                    Artifact: recipe.Artifact,
                    Version: recipe.Version))
                .ToList(),
            Validation: route.Validation.ToList());
    }

    private static void EnsureReady(
        RepositoryAssessment repository)
    {
        if (repository.MigrationReadiness?.CanProceed == true)
        {
            return;
        }

        var reasons = repository.MigrationReadiness?.Reasons ?? [];

        var reasonText = reasons.Count > 0
            ? string.Join(" ", reasons)
            : "Migration readiness has not been established.";

        throw new InvalidOperationException(
            $"Repository '{repository.RepositoryName}' is not ready for transformation. {reasonText}");
    }

    private static int FindMigrationWave(
        string repositoryName,
        MigrationWavePlan migrationWaves)
    {
        var matchingWaves = migrationWaves.Waves
            .Where(wave =>
                wave.Repositories.Contains(
                    repositoryName,
                    StringComparer.OrdinalIgnoreCase))
            .ToList();

        return matchingWaves.Count switch
        {
            1 => matchingWaves[0].Number,

            0 => throw new InvalidOperationException(
                $"Repository '{repositoryName}' does not belong to a migration wave."),

            _ => throw new InvalidOperationException(
                $"Repository '{repositoryName}' belongs to multiple migration waves.")
        };
    }
}
