namespace SpringModernisationAgent.Workspace;

public interface IGitClient
{
    Task CloneAsync(
        string repositoryUrl,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task CheckoutAsync(
        string repositoryPath,
        string commitSha,
        CancellationToken cancellationToken = default);

    Task<string> GetHeadShaAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);
}