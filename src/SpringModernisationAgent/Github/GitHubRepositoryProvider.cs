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
        string path,
        string commitSha)
    {
        try
        {
            var contents = await _client.Repository.Content.GetAllContentsByRef(
                organisation,
                repository,
                path,
                commitSha);

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

    public async Task<string> GetCommitShaAsync(
        string organisation,
        string repository,
        string branch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organisation);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        var reference = await _client.Git.Reference.Get(
            organisation,
            repository,
            $"heads/{branch}");

        return reference.Object.Sha;
    }
}
