using System.Text.Json;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class PortfolioBaselineBuildResultsReaderTests
{
    [Fact]
    public async Task ReadAsync_ValidResults_DeserializesSuccessfully()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "platform-parent",
                  "CommitSha": "abc1234",
                  "Wave": 1,
                  "Operation": "Install",
                  "Status": "Passed"
                },
                {
                  "RepositoryName": "shared-security",
                  "CommitSha": "def5678",
                  "Wave": 2,
                  "Operation": "Install",
                  "Status": "Passed"
                },
                {
                  "RepositoryName": "account-service",
                   "CommitSha": "ghi9012",
                  "Wave": 3,
                  "Operation": "Verify",
                  "Status": "Passed"
                },
                {
                  "RepositoryName": "customer-service",
                  "CommitSha": "jkl3456",
                  "Wave": 3,
                  "Operation": "Verify",
                  "Status": "Passed"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            var result = await reader.ReadAsync(path);

            Assert.Equal(4, result.Repositories.Count);
            Assert.True(result.Passed);

            var platformParent = result.Repositories[0];

            Assert.Equal(
                "platform-parent",
                platformParent.RepositoryName);

            Assert.Equal(1, platformParent.Wave);

            Assert.Equal(
                BaselineBuildOperation.Install,
                platformParent.Operation);

            Assert.Equal(
                BaselineBuildStatus.Passed,
                platformParent.Status);

            var customerService = result.Repositories[3];

            Assert.Equal(
                "customer-service",
                customerService.RepositoryName);

            Assert.Equal(
                "jkl3456",
                customerService.CommitSha);

            Assert.Equal(3, customerService.Wave);

            Assert.Equal(
                BaselineBuildOperation.Verify,
                customerService.Operation);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_FailedRepository_PortfolioHasNotPassed()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "platform-parent",
                  "CommitSha": "abc1234",
                  "Wave": 1,
                  "Operation": "Install",
                  "Status": "Passed"
                },
                {
                  "RepositoryName": "shared-security",
                  "CommitSha": "def5678",
                  "Wave": 2,
                  "Operation": "Install",
                  "Status": "Failed"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            var result = await reader.ReadAsync(path);

            Assert.False(result.Passed);

            var failedRepository = result.Repositories.Single(
                repository =>
                    repository.Status == BaselineBuildStatus.Failed);

            Assert.Equal(
                "shared-security",
                failedRepository.RepositoryName);

            Assert.Equal(
                "def5678",
                failedRepository.CommitSha);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString(),
            "baseline-build-results.json");

        var reader = new PortfolioBaselineBuildResultsReader();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => reader.ReadAsync(path));
    }

    [Fact]
    public async Task ReadAsync_EmptyRepositoryList_ThrowsInvalidOperationException()
    {
        const string json = """
            {
              "Repositories": []
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reader.ReadAsync(path));

            Assert.Equal(
                "Portfolio baseline build results contain no repositories.",
                exception.Message);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_DuplicateRepository_ThrowsInvalidOperationException()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "customer-service",
                  "Wave": 3,
                  "Operation": "Verify",
                  "Status": "Passed"
                },
                {
                  "RepositoryName": "customer-service",
                  "Wave": 3,
                  "Operation": "Verify",
                  "Status": "Passed"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reader.ReadAsync(path));

            Assert.Contains(
                "duplicate repository 'customer-service'",
                exception.Message);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidWave_ThrowsInvalidOperationException()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "customer-service",
                  "Wave": 0,
                  "Operation": "Verify",
                  "Status": "Passed"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => reader.ReadAsync(path));

            Assert.Contains(
                "invalid migration wave '0'",
                exception.Message);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidStatus_ThrowsJsonException()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "customer-service",
                  "Wave": 3,
                  "Operation": "Verify",
                  "Status": "Bananas"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            await Assert.ThrowsAsync<JsonException>(
                () => reader.ReadAsync(path));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact]
    public async Task ReadAsync_InvalidOperation_ThrowsJsonException()
    {
        const string json = """
            {
              "Repositories": [
                {
                  "RepositoryName": "customer-service",
                  "Wave": 3,
                  "Operation": "Deploy",
                  "Status": "Passed"
                }
              ]
            }
            """;

        var path = await CreateTemporaryResultsFileAsync(json);

        try
        {
            var reader = new PortfolioBaselineBuildResultsReader();

            await Assert.ThrowsAsync<JsonException>(
                () => reader.ReadAsync(path));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    private static async Task<string> CreateTemporaryResultsFileAsync(
        string contents)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            "baseline-build-results.json");

        await File.WriteAllTextAsync(path, contents);

        return path;
    }

    private static void DeleteFile(string path)
    {
        var directory = Path.GetDirectoryName(path);

        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}