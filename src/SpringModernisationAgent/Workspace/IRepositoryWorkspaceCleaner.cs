namespace SpringModernisationAgent.Workspace;

public interface IRepositoryWorkspaceCleaner
{
    Task DeleteAsync(
        RepositoryWorkspace workspace,
        CancellationToken cancellationToken = default);
}