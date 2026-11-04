using SpringModernisationAgent.Commands;
using SpringModernisationAgent.Publishing;

namespace SpringModernisationAgent.Tests.Publishing;

public class PublishJobSummaryTests
{
    [Fact]
    public void RenderJobSummary_ShowsEveryRepositoryIncludingPublishingFailures()
    {
        var markdown = PublishCommand.RenderJobSummary(
            [new PullRequestPublication("account-service", 3, "https://github.com/o/account-service/pull/3", Created: false, NeedsDecision: true)],
            [("platform-parent", "git push failed (exit code 1): remote: Internal Server Error\nremote: Request ID X")],
            ["orders-service"]);

        Assert.Contains("[#3](https://github.com/o/account-service/pull/3) updated — needs a decision", markdown);
        Assert.Contains("platform-parent: **publishing failed**: git push failed (exit code 1): remote: Internal Server Error", markdown);
        Assert.DoesNotContain("Request ID", markdown);
        Assert.Contains("orders-service: no pull request", markdown);
    }

    [Fact]
    public void RenderJobSummary_NothingToPublish_SaysSo()
    {
        Assert.Contains("No repositories to publish.", PublishCommand.RenderJobSummary([], [], []));
    }
}
