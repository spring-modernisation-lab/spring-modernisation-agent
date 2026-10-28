using System.Text.Json;
using SpringModernisationAgent.Analysis;
using SpringModernisationAgent.Models;

namespace SpringModernisationAgent.Tests.Analysis;

public class ProjectAssessmentWriterTests
{
    [Fact]
    public async Task WriteAsync_CreatesProjectAssessmentJson()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var outputPath = Path.Combine(
            outputDirectory,
            "project-assessment.json");

        try
        {
            var assessment = new ProjectAssessment(
                Organisation: "spring-modernisation-lab",
                CreatedAt: new DateTimeOffset(
                    2026, 10, 28,
                    12, 0, 0,
                    TimeSpan.Zero),
                Repositories: [],
                DependencyGraph:
                    new RepositoryDependencyGraph(
                        Repositories: [],
                        Edges: []));

            var writer = new ProjectAssessmentWriter();

            await writer.WriteAsync(
                assessment,
                outputPath);

            Assert.True(File.Exists(outputPath));

            var json =
                await File.ReadAllTextAsync(outputPath);

            using var document =
                JsonDocument.Parse(json);

            var root = document.RootElement;

            Assert.Equal(
                "spring-modernisation-lab",
                root.GetProperty("Organisation").GetString());

            Assert.Equal(
                0,
                root.GetProperty("Repositories").GetArrayLength());

            Assert.Equal(
                0,
                root
                    .GetProperty("DependencyGraph")
                    .GetProperty("Edges")
                    .GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(
                    outputDirectory,
                    recursive: true);
            }
        }
    }
}