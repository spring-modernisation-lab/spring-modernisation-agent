using System.Text;
using SpringModernisationAgent.Build;
using SpringModernisationAgent.Publishing;

namespace SpringModernisationAgent.Tests.Publishing;

public class GitBranchPusherTests : IDisposable
{
    private const string Token = "ghp_secretTokenValue123";
    private const string Sha = "ce42258123456789012345678901234567890abc";

    private readonly string _root = Directory.CreateTempSubdirectory("pusher-").FullName;

    [Fact]
    public async Task PushAsync_ClonesAppliesCommitsAndForcePushesOnlyTheMigrationBranch()
    {
        var git = new RecordingGit();

        await new GitBranchPusher(git, _root, Token).PushAsync(
            "spring-modernisation-lab",
            "orders-service",
            Sha,
            "/artifacts/patches/orders-service/cumulative.patch",
            "migration/spring-boot-3.5-java-21",
            "Migrate orders-service");

        var commands = git.Commands.Select(command => command.Arguments).ToList();

        Assert.Equal("clone", commands[0][0]);
        Assert.Contains("https://github.com/spring-modernisation-lab/orders-service.git", commands[0]);
        Assert.Equal(["checkout", "--detach", "--quiet", Sha], commands[1]);
        Assert.Equal(["apply", "--index", "--whitespace=nowarn", "/artifacts/patches/orders-service/cumulative.patch"], commands[2]);
        Assert.Contains("commit", commands[3]);
        Assert.Contains("Migrate orders-service", commands[3]);
        Assert.Equal(["push", "--force", "--quiet", "origin", "HEAD:refs/heads/migration/spring-boot-3.5-java-21"], commands[4]);
        Assert.Equal(5, commands.Count);
    }

    [Fact]
    public async Task PushAsync_TheTokenIsPassedOnlyThroughTheEnvironment()
    {
        var git = new RecordingGit();

        await new GitBranchPusher(git, _root, Token).PushAsync(
            "spring-modernisation-lab", "orders-service", Sha, "/p.patch", "migration/x", "m");

        Assert.All(git.Commands, command =>
            Assert.DoesNotContain(command.Arguments, argument => argument.Contains(Token, StringComparison.Ordinal)));

        var expected = "AUTHORIZATION: basic " +
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{Token}"));

        Assert.All(git.Commands, command =>
        {
            Assert.Equal("http.https://github.com/.extraheader", command.Environment["GIT_CONFIG_KEY_0"]);
            Assert.Equal(expected, command.Environment["GIT_CONFIG_VALUE_0"]);
            Assert.Equal("0", command.Environment["GIT_TERMINAL_PROMPT"]);
        });
    }

    [Fact]
    public async Task PushAsync_AGitFailure_ThrowsWithoutTheTokenAndCleansUp()
    {
        var git = new RecordingGit { FailOn = "apply", FailureMessage = $"error: patch failed (auth {Token})" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new GitBranchPusher(git, _root, Token).PushAsync(
                "spring-modernisation-lab", "orders-service", Sha, "/p.patch", "migration/x", "m"));

        Assert.Contains("git apply failed", exception.Message);
        Assert.DoesNotContain(Token, exception.Message);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Theory]
    [InlineData("spring-modernisation-lab", "orders service", Sha, "migration/x")]
    [InlineData("../evil", "orders-service", Sha, "migration/x")]
    [InlineData("spring-modernisation-lab", "orders-service", "not-a-sha", "migration/x")]
    [InlineData("spring-modernisation-lab", "orders-service", Sha, "main")]
    public async Task PushAsync_InvalidInput_IsRefusedBeforeRunningGit(
        string owner,
        string repository,
        string sha,
        string branch)
    {
        var git = new RecordingGit();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new GitBranchPusher(git, _root, Token).PushAsync(owner, repository, sha, "/p.patch", branch, "m"));

        Assert.Empty(git.Commands);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class RecordingGit : IGitCommandRunner
    {
        public List<(IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment)> Commands { get; } = [];

        public string? FailOn { get; init; }

        public string FailureMessage { get; init; } = "failed";

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((arguments.ToList(), environment));

            // A real clone creates the directory.
            if (arguments[0] == "clone")
            {
                Directory.CreateDirectory(arguments[^1]);
            }

            return Task.FromResult(arguments[0] == FailOn
                ? new MavenProcessResult(1, string.Empty, FailureMessage)
                : new MavenProcessResult(0, string.Empty, string.Empty));
        }
    }
}
