using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.AI;

public sealed class MigrationAnalysisInputBuilder
{
    public MigrationAnalysisInput Build(
        ProjectAssessment projectAssessment,
        IReadOnlyList<TransformationExecutionResult> transformationResults)
    {
        ArgumentNullException.ThrowIfNull(projectAssessment);
        ArgumentNullException.ThrowIfNull(transformationResults);

        var resultsByRepository = transformationResults
            .ToDictionary(
                result => result.Transformation.RepositoryName,
                StringComparer.OrdinalIgnoreCase);

        var repositories = projectAssessment.Repositories
            .Select(repository =>
                CreateRepositoryInput(
                    repository,
                    resultsByRepository))
            .ToList();

        var waves = projectAssessment.MigrationWaves.Waves
            .OrderBy(wave => wave.Number)
            .Select(wave =>
                new MigrationAnalysisWaveInput(
                    Number: wave.Number,
                    Repositories: wave.Repositories.ToList()))
            .ToList();

        return new MigrationAnalysisInput(
            Organisation: projectAssessment.Organisation,
            Repositories: repositories,
            Waves: waves);
    }

    private static MigrationAnalysisRepositoryInput CreateRepositoryInput(
        RepositoryAssessment repository,
        IReadOnlyDictionary<string, TransformationExecutionResult>
            transformationResults)
    {
        var route = repository.MigrationRoute?.Route;

        if (route is null)
        {
            throw new InvalidOperationException(
                $"Repository '{repository.RepositoryName}' has no approved migration route.");
        }

        var internalDependencies = repository.InternalDependencies
            .Select(dependency => dependency.ToRepository)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                dependency => dependency,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        transformationResults.TryGetValue(
            repository.RepositoryName,
            out var transformationResult);

        return new MigrationAnalysisRepositoryInput(
            RepositoryName: repository.RepositoryName,
            CommitSha: repository.CommitSha,
            RepositoryKind: repository.Kind.ToString(),
            SpringBootVersion: repository.Spring.SpringBootVersion,
            JavaVersion: repository.Spring.JavaVersion,
            Target: new MigrationAnalysisTargetInput(
                SpringBootVersion: route.Target.SpringBoot,
                JavaVersion: route.Target.Java),
            MigrationRouteId: route.Id,
            InternalDependencies: internalDependencies,
            ReadinessStatus:
                repository.MigrationReadiness?.Status.ToString()
                ?? "NotEvaluated",
            ReadinessReasons:
                repository.MigrationReadiness?.Reasons.ToList()
                ?? [],
            Transformation:
                transformationResult is null
                    ? null
                    : CreateTransformationInput(
                        transformationResult));
    }

    private static MigrationAnalysisTransformationInput
        CreateTransformationInput(
            TransformationExecutionResult result)
    {
        return new MigrationAnalysisTransformationInput(
            Status: result.Transformation.Status.ToString(),
            AppliedRecipes:
                result.Transformation.AppliedRecipes.ToList(),
            ChangedFiles:
                result.Transformation.ChangedFiles.ToList(),
            ValidationStatus:
                result.Validation.Status.ToString(),
            ValidationChecks:
                result.Validation.Checks
                    .Select(check =>
                        new MigrationAnalysisValidationCheckInput(
                            Check: check.Check,
                            Passed: check.Passed,
                            StandardOutput: check.StandardOutput,
                            StandardError: check.StandardError))
                    .ToList());
    }
}