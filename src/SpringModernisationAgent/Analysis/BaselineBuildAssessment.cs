using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class BaselineBuildAssessment
{
    private readonly BaselineBuildValidator _buildValidator;
    private readonly CommitProvenanceValidator _provenanceValidator;

    public BaselineBuildAssessment(
        BaselineBuildValidator buildValidator,
        CommitProvenanceValidator provenanceValidator)
    {
        _buildValidator = buildValidator;
        _provenanceValidator = provenanceValidator;
    }

    public ProjectAssessment Apply(
        ProjectAssessment assessment,
        PortfolioBaselineBuildResults buildResults)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(buildResults);

        var resultsByRepository = buildResults.Repositories.ToDictionary(
            result => result.RepositoryName,
            StringComparer.OrdinalIgnoreCase);

        var repositories = assessment.Repositories
            .Select(repository => Apply(repository, resultsByRepository))
            .ToList();

        return assessment with
        {
            Repositories = repositories
        };
    }

    private RepositoryAssessment Apply(
        RepositoryAssessment repository,
        IReadOnlyDictionary<string, PortfolioBaselineBuildResult> buildResults)
    {
        if (!buildResults.TryGetValue(repository.RepositoryName, out var buildResult))
        {
            var missingProvenance = _provenanceValidator.Validate(
                                        repository.CommitSha,
                                        builtCommitSha: null);

            return repository with
            {
                CommitProvenance = missingProvenance
            };
        }

        var route = repository.MigrationRoute?.Route;

        var provenance = _provenanceValidator.Validate(
                                repository.CommitSha,
                                buildResult.CommitSha);

        if (route is null)
        {
            return repository with
            {
                BaselineBuild = buildResult,
                CommitProvenance = provenance
            };
        }

        var baselineBuildResult = new BaselineBuildResult(
            RepositoryName: buildResult.RepositoryName,
            Status: buildResult.Status,
            ExitCode: null,
            StartedAt: null,
            CompletedAt: null,
            StandardOutput: null,
            StandardError: null);

        var validation = _buildValidator.Validate(
            route,
            baselineBuildResult);

        return repository with
        {
            BaselineBuild = buildResult,
            BaselineBuildValidation = validation,
            CommitProvenance = provenance
        };
    }
}