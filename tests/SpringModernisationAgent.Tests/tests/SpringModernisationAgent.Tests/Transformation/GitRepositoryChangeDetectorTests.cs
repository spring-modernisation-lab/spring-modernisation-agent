using SpringModernisationAgent.Transformation;
using SpringModernisationAgent.Workspace;

namespace SpringModernisationAgent.Tests.Transformation;

public class GitRepositoryChangeDetectorTests
{
    [Fact]
    public async Task GetChangedFilesAsync_TrackedAndUntrackedFiles_MergesAndSortsFiles()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                new GitProcessResult(
                    ExitCode: 0,
                    StandardOutput:
                        $"src/Changed.java{Environment.NewLine}pom.xml",
                    StandardError: string.Empty),

                new GitProcessResult(
                    ExitCode: 0,
                    StandardOutput:
                        $"rewrite.yml{Environment.NewLine}src/New.java",
                    StandardError: string.Empty)
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            var result = await detector.GetChangedFilesAsync(
                repositoryPath);

            Assert.Equal(
                [
                    "pom.xml",
                    "rewrite.yml",
                    "src/Changed.java",
                    "src/New.java"
                ],
                result);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_DuplicateFiles_ReturnsDistinctFiles()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                new GitProcessResult(
                    ExitCode: 0,
                    StandardOutput:
                        $"pom.xml{Environment.NewLine}src/Changed.java",
                    StandardError: string.Empty),

                new GitProcessResult(
                    ExitCode: 0,
                    StandardOutput:
                        $"pom.xml{Environment.NewLine}rewrite.yml",
                    StandardError: string.Empty)
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            var result = await detector.GetChangedFilesAsync(
                repositoryPath);

            Assert.Equal(
                [
                    "pom.xml",
                    "rewrite.yml",
                    "src/Changed.java"
                ],
                result);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_NoChanges_ReturnsEmptyList()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                SuccessfulResult(),
                SuccessfulResult()
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            var result = await detector.GetChangedFilesAsync(
                repositoryPath);

            Assert.Empty(result);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_UsesExpectedGitOperations()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                SuccessfulResult(),
                SuccessfulResult()
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            await detector.GetChangedFilesAsync(repositoryPath);

            Assert.Collection(
                processRunner.Calls,
                call =>
                {
                    Assert.Equal(
                        Path.GetFullPath(repositoryPath),
                        call.WorkingDirectory);

                    Assert.Equal(
                        [
                            "diff",
                            "--name-only",
                            "HEAD"
                        ],
                        call.Arguments);
                },
                call =>
                {
                    Assert.Equal(
                        Path.GetFullPath(repositoryPath),
                        call.WorkingDirectory);

                    Assert.Equal(
                        [
                            "ls-files",
                            "--others",
                            "--exclude-standard"
                        ],
                        call.Arguments);
                });
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_TrackedChangesFail_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                new GitProcessResult(
                    ExitCode: 1,
                    StandardOutput: string.Empty,
                    StandardError: "Tracked change detection failed.")
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => detector.GetChangedFilesAsync(
                        repositoryPath));

            Assert.Contains(
                "could not detect tracked repository changes",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);

            Assert.Contains(
                "Tracked change detection failed.",
                exception.Message);

            Assert.Single(processRunner.Calls);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_UntrackedChangesFail_ThrowsInvalidOperationException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var processRunner = new FakeGitProcessRunner(
            [
                SuccessfulResult(),

                new GitProcessResult(
                    ExitCode: 1,
                    StandardOutput: string.Empty,
                    StandardError: "Untracked change detection failed.")
            ]);

            var detector = new GitRepositoryChangeDetector(
                processRunner);

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => detector.GetChangedFilesAsync(
                        repositoryPath));

            Assert.Contains(
                "could not detect untracked repository files",
                exception.Message,
                StringComparison.OrdinalIgnoreCase);

            Assert.Contains(
                "Untracked change detection failed.",
                exception.Message);

            Assert.Equal(2, processRunner.Calls.Count);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_RepositoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var detector = new GitRepositoryChangeDetector(
            new FakeGitProcessRunner());

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => detector.GetChangedFilesAsync(
                repositoryPath));
    }

    [Fact]
    public async Task GetChangedFilesAsync_EmptyRepositoryPath_ThrowsArgumentException()
    {
        var detector = new GitRepositoryChangeDetector(
            new FakeGitProcessRunner());

        await Assert.ThrowsAsync<ArgumentException>(
            () => detector.GetChangedFilesAsync(
                string.Empty));
    }

    [Fact]
    public void Constructor_NullProcessRunner_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GitRepositoryChangeDetector(null!));
    }

    private static GitProcessResult SuccessfulResult()
    {
        return new GitProcessResult(
            ExitCode: 0,
            StandardOutput: string.Empty,
            StandardError: string.Empty);
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

    private sealed class FakeGitProcessRunner(
        IEnumerable<GitProcessResult>? results = null)
        : IGitProcessRunner
    {
        private readonly Queue<GitProcessResult> _results =
            new(results ?? []);

        public List<GitProcessCall> Calls { get; } = [];

        public Task<GitProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(
                new GitProcessCall(
                    workingDirectory,
                    arguments.ToList(),
                    cancellationToken));

            if (!_results.TryDequeue(out var result))
            {
                throw new InvalidOperationException(
                    "No fake Git process result was configured.");
            }

            return Task.FromResult(result);
        }
    }

    private sealed record GitProcessCall(
        string WorkingDirectory,
        IReadOnlyList<string> Arguments,
        CancellationToken CancellationToken);
}