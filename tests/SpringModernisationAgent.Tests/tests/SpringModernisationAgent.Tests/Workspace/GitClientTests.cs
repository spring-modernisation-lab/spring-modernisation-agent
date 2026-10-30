using System.Diagnostics;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Workspace;

public class GitClientTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("HEAD")]
    [InlineData("main")]
    [InlineData("--help")]
    [InlineData("")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task CheckoutAsync_InvalidCommitSha_ThrowsArgumentException(
        string commitSha)
    {
        var testRoot = CreateTemporaryDirectory();

        try
        {
            var repositoryPath =
                Path.Combine(testRoot, "repository");

            Directory.CreateDirectory(repositoryPath);

            var client = new GitClient();

            await Assert.ThrowsAsync<ArgumentException>(
                () => client.CheckoutAsync(
                    repositoryPath,
                    commitSha));
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task CloneAsync_DestinationNotEmpty_ThrowsInvalidOperationException()
    {
        var testRoot = CreateTemporaryDirectory();

        try
        {
            var destinationPath =
                Path.Combine(testRoot, "destination");

            Directory.CreateDirectory(destinationPath);

            await File.WriteAllTextAsync(
                Path.Combine(destinationPath, "existing.txt"),
                "existing");

            var client = new GitClient();

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => client.CloneAsync(
                        "unused-repository",
                        destinationPath));

            Assert.Contains(
                "is not empty",
                exception.Message);
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task CheckoutAsync_RepositoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var client = new GitClient();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => client.CheckoutAsync(
                path,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    }

    [Fact]
    public async Task GetHeadShaAsync_RepositoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var client = new GitClient();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => client.GetHeadShaAsync(path));
    }

    [Fact]
    public async Task CloneAsync_EmptyRepositoryUrl_ThrowsArgumentException()
    {
        var client = new GitClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CloneAsync(
                string.Empty,
                Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task CloneAsync_EmptyDestinationPath_ThrowsArgumentException()
    {
        var client = new GitClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CloneAsync(
                "repository",
                string.Empty));
    }

    private static async Task<string> CreateGitRepositoryAsync(
        string repositoryPath)
    {
        Directory.CreateDirectory(repositoryPath);

        await RunGitAsync(
            repositoryPath,
            "init");

        await RunGitAsync(
            repositoryPath,
            "config",
            "user.name",
            "Spring Modernisation Tests");

        await RunGitAsync(
            repositoryPath,
            "config",
            "user.email",
            "tests@example.invalid");

        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "test.txt"),
            "baseline");

        await RunGitAsync(
            repositoryPath,
            "add",
            "test.txt");

        await RunGitAsync(
            repositoryPath,
            "commit",
            "-m",
            "Initial test commit");

        return await RunGitForOutputAsync(
            repositoryPath,
            "rev-parse",
            "HEAD");
    }

    private static async Task RunGitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        _ = await RunGitForOutputAsync(
            workingDirectory,
            arguments);
    }

    private static async Task<string> RunGitForOutputAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var standardOutput =
            await process.StandardOutput.ReadToEndAsync();

        var standardError =
            await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Test Git command failed: {standardError}");
        }

        return standardOutput.Trim();
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
        if (Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }
    }
}