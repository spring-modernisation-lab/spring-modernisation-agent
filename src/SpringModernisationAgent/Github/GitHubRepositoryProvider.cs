using System.Text;
using Octokit;

namespace SpringModernisationAgent.GitHub;

public sealed class GitHubRepositoryProvider
{
    private readonly GitHubClient _client;

    public GitHubRepositoryProvider(string token)
    {
        _client = new GitHubClient(
            new ProductHeaderValue("spring-modernisation-agent"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<IReadOnlyList<Repository>> GetRepositoriesAsync(
        string organisation)
    {
        return await _client.Repository.GetAllForOrg(organisation);
    }

    public async Task<string?> GetFileContentAsync(
        string organisation,
        string repository,
        string path)
    {
        try
        {
            var contents = await _client.Repository.Content.GetAllContents(
                organisation,
                repository,
                path);

            var file = contents.SingleOrDefault();

            if (file is null)
            {
                return null;
            }

            return file.Content;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}
