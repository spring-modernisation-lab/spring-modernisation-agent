using Octokit;

namespace SpringModernisationAgent.Publishing;

/// <summary>GitHub pull request operations through Octokit (token in the HTTP header).</summary>
public sealed class OctokitPullRequestApi : IPullRequestApi
{
    private readonly GitHubClient _client;

    public OctokitPullRequestApi(
        string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _client = new GitHubClient(new ProductHeaderValue("spring-modernisation-agent"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<string> GetDefaultBranchAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken = default) =>
        (await _client.Repository.Get(owner, repository)).DefaultBranch;

    public async Task<PullRequestInfo?> FindOpenAsync(
        string owner,
        string repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        var open = await _client.PullRequest.GetAllForRepository(
            owner,
            repository,
            new Octokit.PullRequestRequest
            {
                State = ItemStateFilter.Open,
                Head = $"{owner}:{branch}"
            });

        var match = open.FirstOrDefault();

        return match is null ? null : new PullRequestInfo(match.Number, match.HtmlUrl);
    }

    public async Task<PullRequestInfo> CreateDraftAsync(
        string owner,
        string repository,
        string branch,
        string baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var created = await _client.PullRequest.Create(
            owner,
            repository,
            new NewPullRequest(title, branch, baseBranch)
            {
                Body = body,
                Draft = true
            });

        return new PullRequestInfo(created.Number, created.HtmlUrl);
    }

    public async Task<PullRequestInfo> UpdateAsync(
        string owner,
        string repository,
        int number,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        var updated = await _client.PullRequest.Update(
            owner,
            repository,
            number,
            new PullRequestUpdate
            {
                Title = title,
                Body = body
            });

        return new PullRequestInfo(updated.Number, updated.HtmlUrl);
    }
}
