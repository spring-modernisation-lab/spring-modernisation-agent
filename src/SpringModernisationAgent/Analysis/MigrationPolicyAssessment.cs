using SpringModernisationAgent.Config;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Analysis;

public sealed class MigrationPolicyAssessment
{
    private readonly MigrationRouteSelector _selector;

    public MigrationPolicyAssessment(MigrationRouteSelector selector)
    {
        _selector = selector;
    }

    public ProjectAssessment Apply(
        ProjectAssessment assessment,
        SpringMigrationPolicy policy)
    {
        var repositories = assessment.Repositories
            .Select(repository =>
            {
                var selection = _selector.Select(
                    repository.Kind,
                    repository.Spring.SpringBootVersion,
                    policy);

                return repository with
                {
                    MigrationRoute = selection
                };
            })
            .ToList();

        return assessment with
        {
            Repositories = repositories
        };
    }
}
