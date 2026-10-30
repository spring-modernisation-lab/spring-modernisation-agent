namespace SpringModernisationAgent.Workspace;

public interface IRepositoryWorkspaceProvider
{
    Task<RepositoryWorkspace> CreateAsync(
        string organisation,
        string repositoryName,
        string commitSha,
        CancellationToken cancellationToken = default);
}