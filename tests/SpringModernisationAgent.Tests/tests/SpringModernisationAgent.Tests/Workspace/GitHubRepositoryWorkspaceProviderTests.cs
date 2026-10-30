using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Workspace;

public class GitHubRepositoryWorkspaceProviderTests
{
    private const string CommitSha =
        "078718f4100ea1340e9a175c13c50bbf71806ae3";

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesWorkspace()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                HeadSha = CommitSha
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            var result = await provider.CreateAsync(
                "spring-modernisation-lab",
                "customer-service",
                CommitSha);

            Assert.Equal(
                "customer-service",
                result.RepositoryName);

            Assert.Equal(
                CommitSha,
                result.CommitSha);

            Assert.StartsWith(
                Path.GetFullPath(workspaceRoot),
                result.RepositoryPath);

            Assert.Equal(
                "https://github.com/spring-modernisation-lab/customer-service.git",
                gitClient.CloneRepositoryUrl);

            Assert.Equal(
                result.RepositoryPath,
                gitClient.CloneDestinationPath);

            Assert.Equal(
                result.RepositoryPath,
                gitClient.CheckoutRepositoryPath);

            Assert.Equal(
                CommitSha,
                gitClient.CheckoutCommitSha);

            Assert.Equal(
                result.RepositoryPath,
                gitClient.HeadRepositoryPath);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateAsync_UsesUniqueWorkspaceDirectory()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                HeadSha = CommitSha
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            var first = await provider.CreateAsync(
                "spring-modernisation-lab",
                "customer-service",
                CommitSha);

            var second = await provider.CreateAsync(
                "spring-modernisation-lab",
                "customer-service",
                CommitSha);

            Assert.NotEqual(
                first.RepositoryPath,
                second.RepositoryPath);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateAsync_HeadMatchesRequestedCommit_ReturnsRequestedCommit()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                HeadSha = CommitSha
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            var result = await provider.CreateAsync(
                "spring-modernisation-lab",
                "customer-service",
                CommitSha);

            Assert.Equal(
                CommitSha,
                result.CommitSha);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateAsync_HeadDoesNotMatchRequestedCommit_ThrowsInvalidOperationException()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                HeadSha =
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => provider.CreateAsync(
                        "spring-modernisation-lab",
                        "customer-service",
                        CommitSha));

            Assert.Contains(
                "instead of the requested commit",
                exception.Message);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("organisation/repository")]
    [InlineData("organisation repository")]
    [InlineData("organisation;command")]
    public async Task CreateAsync_InvalidOrganisation_ThrowsArgumentException(
        string organisation)
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var provider =
                new GitHubRepositoryWorkspaceProvider(
                    new FakeGitClient(),
                    workspaceRoot);

            await Assert.ThrowsAsync<ArgumentException>(
                () => provider.CreateAsync(
                    organisation,
                    "customer-service",
                    CommitSha));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Theory]
    [InlineData("../customer-service")]
    [InlineData("team/customer-service")]
    [InlineData("customer service")]
    [InlineData("customer-service;command")]
    public async Task CreateAsync_InvalidRepositoryName_ThrowsArgumentException(
        string repositoryName)
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var provider =
                new GitHubRepositoryWorkspaceProvider(
                    new FakeGitClient(),
                    workspaceRoot);

            await Assert.ThrowsAsync<ArgumentException>(
                () => provider.CreateAsync(
                    "spring-modernisation-lab",
                    repositoryName,
                    CommitSha));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("main")]
    [InlineData("HEAD")]
    [InlineData("--help")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task CreateAsync_InvalidCommitSha_ThrowsArgumentException(
        string commitSha)
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var provider =
                new GitHubRepositoryWorkspaceProvider(
                    new FakeGitClient(),
                    workspaceRoot);

            await Assert.ThrowsAsync<ArgumentException>(
                () => provider.CreateAsync(
                    "spring-modernisation-lab",
                    "customer-service",
                    commitSha));
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateAsync_CloneFails_DoesNotCheckoutRepository()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                CloneException =
                    new InvalidOperationException("Clone failed.")
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateAsync(
                    "spring-modernisation-lab",
                    "customer-service",
                    CommitSha));

            Assert.False(gitClient.CheckoutCalled);
            Assert.False(gitClient.GetHeadCalled);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task CreateAsync_CheckoutFails_DoesNotReadHead()
    {
        var workspaceRoot = CreateTemporaryDirectory();

        try
        {
            var gitClient = new FakeGitClient
            {
                CheckoutException =
                    new InvalidOperationException("Checkout failed.")
            };

            var provider = new GitHubRepositoryWorkspaceProvider(
                gitClient,
                workspaceRoot);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateAsync(
                    "spring-modernisation-lab",
                    "customer-service",
                    CommitSha));

            Assert.True(gitClient.CheckoutCalled);
            Assert.False(gitClient.GetHeadCalled);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            File.SetAttributes(
                file,
                FileAttributes.Normal);
        }

        Directory.Delete(
            path,
            recursive: true);
    }

    private sealed class FakeGitClient : IGitClient
    {
        public string HeadSha { get; set; } = CommitSha;

        public Exception? CloneException { get; set; }

        public Exception? CheckoutException { get; set; }

        public string? CloneRepositoryUrl { get; private set; }

        public string? CloneDestinationPath { get; private set; }

        public string? CheckoutRepositoryPath { get; private set; }

        public string? CheckoutCommitSha { get; private set; }

        public string? HeadRepositoryPath { get; private set; }

        public bool CheckoutCalled { get; private set; }

        public bool GetHeadCalled { get; private set; }

        public Task CloneAsync(
            string repositoryUrl,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            CloneRepositoryUrl = repositoryUrl;
            CloneDestinationPath = destinationPath;

            if (CloneException is not null)
            {
                throw CloneException;
            }

            Directory.CreateDirectory(destinationPath);

            return Task.CompletedTask;
        }

        public Task CheckoutAsync(
            string repositoryPath,
            string commitSha,
            CancellationToken cancellationToken = default)
        {
            CheckoutCalled = true;
            CheckoutRepositoryPath = repositoryPath;
            CheckoutCommitSha = commitSha;

            if (CheckoutException is not null)
            {
                throw CheckoutException;
            }

            return Task.CompletedTask;
        }

        public Task<string> GetHeadShaAsync(
            string repositoryPath,
            CancellationToken cancellationToken = default)
        {
            GetHeadCalled = true;
            HeadRepositoryPath = repositoryPath;

            return Task.FromResult(HeadSha);
        }
    }
}