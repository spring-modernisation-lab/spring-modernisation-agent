using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Build;

public class MavenRunnerTests
{
    [Fact]
    public async Task RunBaselineBuildAsync_RepositoryDoesNotExist_ReturnsNotRun()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var runner = new MavenRunner();

        var result = await runner.RunBaselineBuildAsync(
            "customer-service",
            repositoryPath);

        Assert.Equal("customer-service", result.RepositoryName);
        Assert.Equal(BaselineBuildStatus.NotRun, result.Status);
        Assert.Null(result.ExitCode);
        Assert.False(result.Passed);

        Assert.Contains(
            "Repository directory was not found",
            result.StandardError);

        Assert.Null(result.StartedAt);
        Assert.Null(result.CompletedAt);
        Assert.Null(result.StandardOutput);
    }

    [Fact]
    public async Task RunBaselineBuildAsync_MavenWrapperDoesNotExist_ReturnsNotRun()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.Equal(BaselineBuildStatus.NotRun, result.Status);
            Assert.Null(result.ExitCode);
            Assert.False(result.Passed);

            Assert.Equal(
                "The repository does not contain mvnw or mvnw.cmd.",
                result.StandardError);

            Assert.Null(result.StartedAt);
            Assert.Null(result.CompletedAt);
            Assert.Null(result.StandardOutput);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_WrapperReturnsZero_ReturnsPassed()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            CreateFakeMavenWrapper(
                repositoryPath,
                exitCode: 0,
                standardOutput: "Fake Maven build succeeded.");

            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.Equal(BaselineBuildStatus.Passed, result.Status);
            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Passed);

            Assert.NotNull(result.StartedAt);
            Assert.NotNull(result.CompletedAt);

            Assert.Contains(
                "Fake Maven build succeeded.",
                result.StandardOutput);

            Assert.Contains(
                "-B clean verify",
                result.StandardOutput);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_WrapperReturnsNonZero_ReturnsFailed()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            CreateFakeMavenWrapper(
                repositoryPath,
                exitCode: 1,
                standardOutput: "Fake Maven build failed.");

            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.Equal(BaselineBuildStatus.Failed, result.Status);
            Assert.Equal(1, result.ExitCode);
            Assert.False(result.Passed);

            Assert.NotNull(result.StartedAt);
            Assert.NotNull(result.CompletedAt);

            Assert.Contains(
                "Fake Maven build failed.",
                result.StandardOutput);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_UsesApprovedBaselineBuildArguments()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            CreateFakeMavenWrapper(
                repositoryPath,
                exitCode: 0,
                standardOutput: "Arguments received:");

            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.Equal(BaselineBuildStatus.Passed, result.Status);

            Assert.Contains(
                "-B clean verify",
                result.StandardOutput);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_WrapperWritesStandardError_CapturesError()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            CreateFakeMavenWrapper(
                repositoryPath,
                exitCode: 1,
                standardOutput: "Build output",
                standardError: "Compilation failed");

            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.Equal(BaselineBuildStatus.Failed, result.Status);
            Assert.Equal(1, result.ExitCode);

            Assert.Contains(
                "Build output",
                result.StandardOutput);

            Assert.Contains(
                "Compilation failed",
                result.StandardError);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_WrapperRuns_RecordsExecutionTimes()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            CreateFakeMavenWrapper(
                repositoryPath,
                exitCode: 0,
                standardOutput: "Build completed");

            var runner = new MavenRunner();

            var result = await runner.RunBaselineBuildAsync(
                "customer-service",
                repositoryPath);

            Assert.NotNull(result.StartedAt);
            Assert.NotNull(result.CompletedAt);

            Assert.True(
                result.CompletedAt >= result.StartedAt);
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_RepositoryNameIsEmpty_ThrowsArgumentException()
    {
        var repositoryPath = CreateTemporaryDirectory();

        try
        {
            var runner = new MavenRunner();

            await Assert.ThrowsAsync<ArgumentException>(
                () => runner.RunBaselineBuildAsync(
                    string.Empty,
                    repositoryPath));
        }
        finally
        {
            DeleteDirectory(repositoryPath);
        }
    }

    [Fact]
    public async Task RunBaselineBuildAsync_RepositoryPathIsEmpty_ThrowsArgumentException()
    {
        var runner = new MavenRunner();

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunBaselineBuildAsync(
                "customer-service",
                string.Empty));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(path);

        return path;
    }

    private static void CreateFakeMavenWrapper(
    string repositoryPath,
    int exitCode,
    string standardOutput = "",
    string standardError = "")
    {
        if (OperatingSystem.IsWindows())
        {
            var wrapperPath = Path.Combine(repositoryPath, "mvnw.cmd");

            File.WriteAllText(
                wrapperPath,
                $"""
            @echo off
            echo {standardOutput}
            echo %*
            echo {standardError} 1>&2
            exit /b {exitCode}
            """);

            return;
        }

        var unixWrapperPath = Path.Combine(repositoryPath, "mvnw");

        File.WriteAllText(
            unixWrapperPath,
            $"""
        #!/bin/sh
        echo "{standardOutput}"
        echo "$@"
        echo "{standardError}" >&2
        exit {exitCode}
        """);

        File.SetUnixFileMode(
            unixWrapperPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
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