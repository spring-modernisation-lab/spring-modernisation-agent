using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Reporting;

namespace SpringModernisationAgent.Tests.Behaviour;

public class BehaviourContractRecorderTests : IDisposable
{
    private const string Sha = "ce422587799bb8c49d2724f1e963e456b7e977d6";

    private static readonly BehaviourRecord Baseline = Record(
        ("B-CTX-default", "starts"),
        ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"),
        ("B-SEC-default-POST /api/orders/-VIEWER", "400"));

    private readonly string _workspace = Directory.CreateTempSubdirectory("contract-").FullName;

    [Fact]
    public async Task RecordBaselineAsync_ARepositoryWithNothingToProbe_HasNoContract()
    {
        var recorder = new BehaviourContractRecorder(new FakeRecorder(canRecord: false), new FakeRecorder());

        Assert.Null(await recorder.RecordBaselineAsync(Plan(), _workspace));
    }

    [Fact]
    public async Task RecordBaselineAsync_RemovesTheSourceJdksBuildOutputAndNeverThrows()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "target", "classes"));
        var recorder = new BehaviourContractRecorder(new FakeRecorder(fails: true), new FakeRecorder());

        var report = (await recorder.RecordBaselineAsync(Plan(), _workspace))!;

        Assert.Null(report.Baseline);
        Assert.Contains("could not be recorded", report.Note);
        Assert.False(Directory.Exists(Path.Combine(_workspace, "target")));
    }

    [Fact]
    public async Task CompareAsync_TheMigratedApplicationIsRecordedWithTheTargetRecorderAndCompared()
    {
        // Run 23's shape: the time-to-live is gone.
        var after = new FakeRecorder(record: Record(("B-CTX-default", "starts"), ("B-SEC-default-POST /api/orders/-VIEWER", "400")));
        var recorder = new BehaviourContractRecorder(new FakeRecorder(record: Baseline), after);
        var baseline = (await recorder.RecordBaselineAsync(Plan(), _workspace))!;

        var report = await recorder.CompareAsync(Plan(), _workspace, baseline, Passed());

        Assert.Equal(1, after.Calls);
        Assert.False(report.Holds);
        var difference = Assert.Single(report.Comparison!.Differences);
        Assert.Equal("B-BEAN-default-HttpClient.connectionTimeToLive", difference.Item);
        Assert.Equal(ContractItemStatus.Unavailable, difference.Status);
    }

    [Fact]
    public async Task CompareAsync_CodeThatDoesNotCompile_IsNotProbed()
    {
        var after = new FakeRecorder(record: Baseline);
        var recorder = new BehaviourContractRecorder(new FakeRecorder(record: Baseline), after);
        var baseline = (await recorder.RecordBaselineAsync(Plan(), _workspace))!;

        var report = await recorder.CompareAsync(Plan(), _workspace, baseline,
            new TransformationValidationResult("orders-service", Sha, TransformationValidationStatus.Failed,
                [new TransformationValidationCheckResult("compile", false, null, null)]));

        Assert.Equal(0, after.Calls);
        Assert.Null(report.Comparison);
        Assert.Contains("does not compile", report.Note);
    }

    [Fact]
    public async Task CompareAsync_AProbeFailureIsANoteNotAnException()
    {
        var recorder = new BehaviourContractRecorder(new FakeRecorder(record: Baseline), new FakeRecorder(fails: true));
        var baseline = (await recorder.RecordBaselineAsync(Plan(), _workspace))!;

        var report = await recorder.CompareAsync(Plan(), _workspace, baseline, Passed());

        Assert.Null(report.Comparison);
        Assert.False(report.Holds);
        Assert.Contains("not compared", report.Note);
    }

    [Fact]
    public void Verdict_MavenPassedAndTheContractHolds_Succeeds()
    {
        var result = Result(passed: true) with { Behaviour = new BehaviourReport(Baseline, Baseline, BehaviourContract.Compare(Baseline, Baseline), null) };

        Assert.True(result.Succeeded);
        Assert.False(result.NeedsDecision);
    }

    [Fact]
    public void Verdict_MavenPassedButBehaviourChanged_NeedsADecisionNotSuccess()
    {
        var after = Record(("B-CTX-default", "starts"), ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"), ("B-SEC-default-POST /api/orders/-VIEWER", "405"));
        var result = Result(passed: true) with { Behaviour = new BehaviourReport(Baseline, after, BehaviourContract.Compare(Baseline, after), null) };

        Assert.False(result.Succeeded);
        Assert.True(result.NeedsDecision);
    }

    [Fact]
    public void Verdict_MavenPassedButTheContractCouldNotBeChecked_NeedsADecision()
    {
        // Unknown is not "same".
        var result = Result(passed: true) with { Behaviour = new BehaviourReport(Baseline, null, null, "not compared: probe crashed") };

        Assert.False(result.Succeeded);
        Assert.True(result.NeedsDecision);
    }

    [Fact]
    public void Verdict_NoBaseline_LeavesTheVerdictToMaven()
    {
        // For example platform-parent: nothing to start.
        Assert.True(Result(passed: true).Succeeded);
        Assert.True((Result(passed: true) with { Behaviour = new BehaviourReport(null, null, null, "the baseline could not be recorded") }).Succeeded);
    }

    [Fact]
    public void Reporting_TheSummaryAndThePrListTheDifferences()
    {
        var after = Record(("B-CTX-default", "starts"), ("B-SEC-default-POST /api/orders/-VIEWER", "405"));
        var result = Result(passed: true) with { Behaviour = new BehaviourReport(Baseline, after, BehaviourContract.Compare(Baseline, after), null) };

        var summary = TransformationSummaryWriter.Render([result]);
        Assert.Contains("### orders-service: behaviour contract", summary);
        Assert.Contains("**Does not hold**: 2 differ from the Boot 2.7 application; 1 are unchanged.", summary);
        Assert.Contains("| `B-SEC-default-POST /api/orders/-VIEWER` | 400 | 405 | Changed |", summary);

        var body = PullRequestContentBuilder.Build(
            new TransformationPlan("orders-service", Sha, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
                new TransformationTarget("3.5.x", "21"), [], ["compile"]),
            result, [], null)!.Body;

        Assert.Contains("[Needs decision]", PullRequestContentBuilder.Build(
            new TransformationPlan("orders-service", Sha, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
                new TransformationTarget("3.5.x", "21"), [], ["compile"]), result, [], null)!.Title);
        Assert.Contains("## Behaviour compared with the Boot 2.7 application", body);
        Assert.Contains("Maven passed, but the migrated application behaves differently from the Boot 2.7 one in 2 ways", body);
        Assert.Contains("| `B-BEAN-default-HttpClient.connectionTimeToLive` | 30000ms | (not recorded) | Unavailable |", body);
    }

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", Sha, items.ToDictionary(item => item.Item, item => item.Value));

    private static TransformationPlan Plan() =>
        new("orders-service", Sha, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
            new TransformationTarget("3.5.x", "21"), [], ["compile"]);

    private static TransformationValidationResult Passed() =>
        new("orders-service", Sha, TransformationValidationStatus.Passed, [new TransformationValidationCheckResult("compile", true, null, null)]);

    private static TransformationExecutionResult Result(bool passed) =>
        new(
            new TransformationResult("orders-service", Sha, TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
            passed ? Passed() : new TransformationValidationResult("orders-service", Sha, TransformationValidationStatus.Failed, []))
        {
            Patches = [new PatchArtifact("cumulative", "patches/orders-service/cumulative.patch", "abc", ["pom.xml"])]
        };

    private sealed class FakeRecorder(bool canRecord = true, bool fails = false, BehaviourRecord? record = null) : IBehaviourRecorder
    {
        public int Calls { get; private set; }

        public bool CanRecord(string repositoryPath) => canRecord;

        public Task<BehaviourRecord> RecordAsync(string repositoryPath, string repository, string commit, CancellationToken cancellationToken = default)
        {
            Calls++;
            return fails ? throw new InvalidOperationException("probe crashed") : Task.FromResult(record ?? Baseline);
        }
    }
}
