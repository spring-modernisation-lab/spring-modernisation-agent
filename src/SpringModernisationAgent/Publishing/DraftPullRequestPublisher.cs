using System.Text.RegularExpressions;

namespace SpringModernisationAgent.Publishing;

public sealed record PullRequestInfo(
    int Number,
    string Url);

/// <summary>The few GitHub pull request operations publishing needs.</summary>
public interface IPullRequestApi
{
    Task<string> GetDefaultBranchAsync(string owner, string repository, CancellationToken cancellationToken = default);

    Task<PullRequestInfo?> FindOpenAsync(string owner, string repository, string branch, CancellationToken cancellationToken = default);

    Task<PullRequestInfo> CreateDraftAsync(string owner, string repository, string branch, string baseBranch, string title, string body, CancellationToken cancellationToken = default);

    Task<PullRequestInfo> UpdateAsync(string owner, string repository, int number, string title, string body, CancellationToken cancellationToken = default);
}

/// <summary>
/// Puts the migrated code on a branch: a fresh clone of the assessed commit,
/// the cumulative patch applied, one commit, force-pushed to that branch.
/// </summary>
public interface IBranchPusher
{
    Task PushAsync(string owner, string repository, string sourceSha, string patchPath, string branch, string message, CancellationToken cancellationToken = default);
}

/// <param name="Created">True if a new draft was opened, false if an open one was updated.</param>
public sealed record PullRequestPublication(
    string RepositoryName,
    int Number,
    string Url,
    bool Created,
    bool NeedsDecision);

/// <summary>
/// Opens or updates a draft pull request for a migrated repository. Only a
/// <c>migration/</c> branch that is not the default branch is ever pushed,
/// so this can never write to the default branch; nothing is merged.
/// Re-running updates the same branch and pull request.
/// </summary>
public sealed partial class DraftPullRequestPublisher
{
    private readonly IPullRequestApi _api;
    private readonly IBranchPusher _pusher;

    public DraftPullRequestPublisher(
        IPullRequestApi api,
        IBranchPusher pusher)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(pusher);

        _api = api;
        _pusher = pusher;
    }

    public async Task<PullRequestPublication> PublishAsync(
        string owner,
        PullRequestContent content,
        string cumulativePatchPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(cumulativePatchPath);

        if (!MigrationBranch().IsMatch(content.Branch))
        {
            throw new InvalidOperationException(
                $"Refusing to publish to '{content.Branch}': only migration/ branches are written.");
        }

        var defaultBranch = await _api.GetDefaultBranchAsync(owner, content.RepositoryName, cancellationToken);

        if (string.Equals(defaultBranch, content.Branch, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to publish to '{content.Branch}': it is the default branch of {content.RepositoryName}.");
        }

        await _pusher.PushAsync(
            owner,
            content.RepositoryName,
            content.SourceCommitSha,
            cumulativePatchPath,
            content.Branch,
            $"{content.Title.Replace("[Needs decision] ", string.Empty, StringComparison.Ordinal)}\n\n" +
            "Applied by the Spring modernisation pipeline (OpenRewrite and the Remediation Agent).\n" +
            $"Source commit: {content.SourceCommitSha}",
            cancellationToken);

        var existing = await _api.FindOpenAsync(owner, content.RepositoryName, content.Branch, cancellationToken);

        var pullRequest = existing is null
            ? await _api.CreateDraftAsync(owner, content.RepositoryName, content.Branch, defaultBranch, content.Title, content.Body, cancellationToken)
            : await _api.UpdateAsync(owner, content.RepositoryName, existing.Number, content.Title, content.Body, cancellationToken);

        return new PullRequestPublication(
            content.RepositoryName,
            pullRequest.Number,
            pullRequest.Url,
            Created: existing is null,
            content.NeedsDecision);
    }

    // migration/<segment>[/<segment>...], no "..", no leading dash.
    [GeneratedRegex(@"^migration/[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*$")]
    private static partial Regex MigrationBranch();
}
