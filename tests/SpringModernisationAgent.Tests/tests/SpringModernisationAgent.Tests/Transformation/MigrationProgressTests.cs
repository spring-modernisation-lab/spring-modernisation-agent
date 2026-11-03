using SpringModernisationAgent.Build;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Transformation;

namespace SpringModernisationAgent.Tests.Transformation;

public class MigrationProgressTests
{
    [Fact]
    public void ConsoleProgress_WritesTimestampedRepositoryLines()
    {
        var writer = new StringWriter();

        var progress = new ConsoleMigrationProgress(
            writer,
            () => new DateTimeOffset(2026, 11, 3, 12, 41, 5, TimeSpan.Zero));

        progress.Report("orders-service", "running OpenRewrite (2 recipes)");

        Assert.Equal(
            "12:41:05 orders-service: running OpenRewrite (2 recipes)" + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public async Task Validator_ReportsEachCheckAndItsOutcome()
    {
        var directory = Directory.CreateTempSubdirectory("validator-progress-").FullName;

        try
        {
            var progress = new RecordingProgress();

            var validator = new TransformationValidator(
                new QueueMavenRunner(new MavenProcessResult(0, "", ""), new MavenProcessResult(1, "", "")),
                progress);

            await validator.ValidateAsync(CreatePlan(), directory);

            Assert.Collection(
                progress.Messages,
                message => Assert.Equal("orders-service: check compile: running", message),
                message => Assert.StartsWith("orders-service: check compile: passed (", message),
                message => Assert.Equal("orders-service: check unit-tests: running", message),
                message => Assert.StartsWith("orders-service: check unit-tests: FAILED (", message));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static TransformationPlan CreatePlan() =>
        new(
            RepositoryName: "orders-service",
            SourceCommitSha: "b39866542351f9c79f05df7558e84aa9799ef33f",
            MigrationWave: 1,
            RepositoryKind: RepositoryKind.SpringBootApplication,
            MigrationRouteId: "spring-boot-application-2.7-to-3",
            Target: new TransformationTarget("3.5.x", "21"),
            OpenRewrite: [],
            Validation: ["compile", "unit-tests", "application-context-test"]);

    internal sealed class RecordingProgress : IMigrationProgress
    {
        public List<string> Messages { get; } = [];

        public void Report(string repositoryName, string message) =>
            Messages.Add($"{repositoryName}: {message}");
    }

    private sealed class QueueMavenRunner(params MavenProcessResult[] results) : IMavenProcessRunner
    {
        private readonly Queue<MavenProcessResult> _results = new(results);

        public Task<MavenProcessResult> RunAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_results.Dequeue());
    }
}
