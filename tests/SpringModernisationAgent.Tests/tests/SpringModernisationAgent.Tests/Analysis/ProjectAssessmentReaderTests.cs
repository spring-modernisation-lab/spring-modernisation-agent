using System.Text.Json;
using SpringModernisationAgent.AI;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Maven;
using SpringModernisationAgent.Tests.AI;

namespace SpringModernisationAgent.Tests.Analysis;

public class ProjectAssessmentReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"project-assessment-reader-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadAsync_RoundTripsWriterOutput()
    {
        var original = MigrationAnalysisInputBuilderTests
            .CreateProjectAssessment(
                externalDependencies:
                [
                    new MavenDependency(
                        new MavenCoordinate(
                            "org.apache.httpcomponents",
                            "httpclient",
                            Version: null),
                        Scope: "compile",
                        Optional: false)
                ]);

        var path = Path.Combine(_directory, "project-assessment.json");

        await new ProjectAssessmentWriter().WriteAsync(original, path);

        var roundTripped = await new ProjectAssessmentReader().ReadAsync(path);

        // Everything downstream consumers use must survive the round trip;
        // comparing the derived AI input covers repositories, versions,
        // routes, dependencies, readiness and waves in one assertion.
        var builder = new MigrationAnalysisInputBuilder();

        Assert.Equal(
            JsonSerializer.Serialize(builder.Build(original, [])),
            JsonSerializer.Serialize(builder.Build(roundTripped, [])));

        Assert.Equal(original.Organisation, roundTripped.Organisation);
        Assert.Equal(original.CreatedAt, roundTripped.CreatedAt);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => new ProjectAssessmentReader().ReadAsync(
                Path.Combine(_directory, "missing.json")));
    }

    [Fact]
    public async Task ReadAsync_JsonNull_ThrowsInvalidOperationException()
    {
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, "null.json");

        await File.WriteAllTextAsync(path, "null");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ProjectAssessmentReader().ReadAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
