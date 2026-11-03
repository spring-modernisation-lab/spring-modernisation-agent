using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class TransformationResultsReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"transformation-results-reader-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsync_RoundTripsWriterOutput()
    {
        var path = Path.Combine(_directory, "transformation-results.json");

        var original = new TransformationExecutionResult(
            Transformation: new TransformationResult(
                RepositoryName: "orders-service",
                SourceCommitSha: "b39866542351f9c79f05df7558e84aa9799ef33f",
                Status: TransformationStatus.Succeeded,
                AppliedRecipes: ["recipe"],
                ChangedFiles: ["pom.xml"],
                StandardOutput: "out",
                StandardError: null),
            Validation: new TransformationValidationResult(
                RepositoryName: "orders-service",
                SourceCommitSha: "b39866542351f9c79f05df7558e84aa9799ef33f",
                Status: TransformationValidationStatus.Failed,
                Checks:
                [
                    new TransformationValidationCheckResult(
                        Check: "compile",
                        Passed: false,
                        StandardOutput: "[ERROR] x",
                        StandardError: null)
                ]));

        await new TransformationResultsWriter().WriteAsync([original], path);

        var result = Assert.Single(
            await new TransformationResultsReader().ReadAsync(path));

        Assert.Equal("orders-service", result.Transformation.RepositoryName);
        Assert.Equal(TransformationStatus.Succeeded, result.Transformation.Status);
        Assert.Equal(["pom.xml"], result.Transformation.ChangedFiles);
        Assert.Equal(TransformationValidationStatus.Failed, result.Validation.Status);
        Assert.False(result.Succeeded);

        var check = Assert.Single(result.Validation.Checks);
        Assert.Equal("[ERROR] x", check.StandardOutput);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(
            await new TransformationResultsReader().ReadAsync(
                Path.Combine(_directory, "missing.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
