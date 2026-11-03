using SpringModernisationAgent.Publishing;

namespace SpringModernisationAgent.Tests.Publishing;

public class DraftPullRequestPublisherTests
{
    private const string Owner = "spring-modernisation-lab";

    [Fact]
    public async Task PublishAsync_NoOpenPullRequest_PushesTheBranchThenOpensADraft()
    {
        var api = new FakeApi();
        var pusher = new FakePusher(api);

        var publication = await new DraftPullRequestPublisher(api, pusher)
            .PublishAsync(Owner, CreateContent(), "/artifacts/patches/orders-service/cumulative.patch");

        Assert.True(publication.Created);
        Assert.Equal(7, publication.Number);
        Assert.Equal("https://github.com/spring-modernisation-lab/orders-service/pull/7", publication.Url);

        var push = Assert.Single(pusher.Pushes);
        Assert.Equal(("orders-service", "ce42258", "migration/spring-boot-3.5-java-21", "/artifacts/patches/orders-service/cumulative.patch"),
            (push.Repository, push.SourceSha, push.Branch, push.PatchPath));
        Assert.StartsWith("Migrate orders-service to Spring Boot 3.5 and Java 21", push.Message);

        // The branch exists before the pull request is opened.
        Assert.Equal(["default-branch", "push", "find", "create"], api.Calls);
        Assert.Equal(("migration/spring-boot-3.5-java-21", "main", true), (api.CreatedHead, api.CreatedBase, api.CreatedDraft));
    }

    [Fact]
    public async Task PublishAsync_OpenPullRequestExists_UpdatesItInsteadOfOpeningAnother()
    {
        var api = new FakeApi { Existing = new PullRequestInfo(3, "https://github.com/x/orders-service/pull/3") };

        var publication = await new DraftPullRequestPublisher(api, new FakePusher(api))
            .PublishAsync(Owner, CreateContent() with { Title = "[Needs decision] Migrate" }, "/p.patch");

        Assert.False(publication.Created);
        Assert.Equal(3, publication.Number);
        Assert.Equal(["default-branch", "push", "find", "update"], api.Calls);
        Assert.Equal("[Needs decision] Migrate", api.UpdatedTitle);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("feature/upgrade")]
    [InlineData("migration/../main")]
    public async Task PublishAsync_BranchThatIsNotAMigrationBranch_IsRefusedBeforeAnythingIsPushed(
        string branch)
    {
        var api = new FakeApi();
        var pusher = new FakePusher(api);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DraftPullRequestPublisher(api, pusher)
                .PublishAsync(Owner, CreateContent() with { Branch = branch }, "/p.patch"));

        Assert.Empty(pusher.Pushes);
    }

    [Fact]
    public async Task PublishAsync_BranchIsTheDefaultBranch_IsRefused()
    {
        var api = new FakeApi { DefaultBranch = "migration/spring-boot-3.5-java-21" };
        var pusher = new FakePusher(api);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new DraftPullRequestPublisher(api, pusher).PublishAsync(Owner, CreateContent(), "/p.patch"));

        Assert.Contains("default branch", exception.Message);
        Assert.Empty(pusher.Pushes);
    }

    private static PullRequestContent CreateContent() =>
        new(
            RepositoryName: "orders-service",
            SourceCommitSha: "ce42258",
            Branch: "migration/spring-boot-3.5-java-21",
            Title: "Migrate orders-service to Spring Boot 3.5 and Java 21",
            Body: "# Summary",
            NeedsDecision: false);

    private sealed class FakeApi : IPullRequestApi
    {
        public List<string> Calls { get; } = [];

        public string DefaultBranch { get; init; } = "main";

        public PullRequestInfo? Existing { get; init; }

        public string? CreatedHead { get; private set; }

        public string? CreatedBase { get; private set; }

        public bool CreatedDraft { get; private set; }

        public string? UpdatedTitle { get; private set; }

        public Task<string> GetDefaultBranchAsync(string owner, string repository, CancellationToken cancellationToken = default)
        {
            Calls.Add("default-branch");
            return Task.FromResult(DefaultBranch);
        }

        public Task<PullRequestInfo?> FindOpenAsync(string owner, string repository, string branch, CancellationToken cancellationToken = default)
        {
            Calls.Add("find");
            return Task.FromResult(Existing);
        }

        public Task<PullRequestInfo> CreateDraftAsync(string owner, string repository, string branch, string baseBranch, string title, string body, CancellationToken cancellationToken = default)
        {
            Calls.Add("create");
            (CreatedHead, CreatedBase, CreatedDraft) = (branch, baseBranch, true);
            return Task.FromResult(new PullRequestInfo(7, $"https://github.com/{owner}/{repository}/pull/7"));
        }

        public Task<PullRequestInfo> UpdateAsync(string owner, string repository, int number, string title, string body, CancellationToken cancellationToken = default)
        {
            Calls.Add("update");
            UpdatedTitle = title;
            return Task.FromResult(Existing!);
        }
    }

    private sealed class FakePusher(FakeApi api) : IBranchPusher
    {
        public List<(string Repository, string SourceSha, string Branch, string PatchPath, string Message)> Pushes { get; } = [];

        public Task PushAsync(string owner, string repository, string sourceSha, string patchPath, string branch, string message, CancellationToken cancellationToken = default)
        {
            api.Calls.Add("push");
            Pushes.Add((repository, sourceSha, branch, patchPath, message));
            return Task.CompletedTask;
        }
    }
}
