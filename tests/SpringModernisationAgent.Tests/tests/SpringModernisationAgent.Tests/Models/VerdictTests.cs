using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Models;

/// <summary>One verdict, from facts, with reasons (docs/system-design.md §8.2).</summary>
public class VerdictTests
{
    private const string Sha = "ce42258";

    private static readonly BehaviourRecord Baseline = Record(("B-CTX-default", "starts"), ("B-SEC-default-GET /api/orders/-VIEWER", "200"));

    [Fact]
    public void MavenPassed_NothingLeft_Succeeds_WithNoReasons()
    {
        var verdict = Result(mavenPassed: true).Verdict;

        Assert.Equal(VerdictState.Succeeded, verdict.State);
        Assert.Empty(verdict.Reasons);
    }

    [Fact]
    public void OpenRewriteFailed_IsNotRun()
    {
        var result = Result(mavenPassed: false) with
        {
            Transformation = new TransformationResult("orders-service", Sha, TransformationStatus.Failed, [], [], null, "plugin error")
        };

        var verdict = result.Verdict;

        Assert.Equal(VerdictState.NotRun, verdict.State);
        Assert.Equal("F-OPENREWRITE", Assert.Single(verdict.Reasons).Id);
    }

    [Fact]
    public void MavenFailed_WithNothingForAPerson_Fails()
    {
        var verdict = (Result(mavenPassed: false) with
        {
            RemainingProblems = [Problem(ProblemKind.Unknown, "build")]
        }).Verdict;

        Assert.Equal(VerdictState.Failed, verdict.State);
        Assert.Equal(["F-MAVEN", "K-UNKNOWN"], verdict.Reasons.Select(reason => reason.Id));
        Assert.All(verdict.Reasons, reason => Assert.False(reason.NeedsAPerson));
    }

    [Theory]
    [InlineData(ProblemKind.PlatformDropped, "K-PLATFORM-DROPPED")]
    [InlineData(ProblemKind.Contract, "K-CONTRACT")]
    public void MavenFailed_OnlyOnDecisions_NeedsADecision(
        ProblemKind kind,
        string reason)
    {
        var verdict = (Result(mavenPassed: false) with { RemainingProblems = [Problem(kind, "a.Tests.t")] }).Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        Assert.Contains(verdict.Reasons, entry => entry.Id == reason && entry.NeedsAPerson);
        Assert.Contains(verdict.Reasons, entry => entry.Id == "F-MAVEN");
    }

    [Fact]
    public void MavenPassed_WithAnUnsupportedLibrary_NeedsADecision()
    {
        // I-UNSUPPORTED-NEVER-SUCCEEDS (R23: "Fixed" with the library still in the POM).
        var verdict = (Result(mavenPassed: true) with { UnsupportedLibraries = ["Ehcache 2"] }).Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        var reason = Assert.Single(verdict.Reasons);
        Assert.Equal("I-UNSUPPORTED-NEVER-SUCCEEDS", reason.Id);
        Assert.Contains("Ehcache 2", reason.Text);
    }

    [Fact]
    public void MavenPassed_ButBehaviourChanged_NeedsADecision()
    {
        var after = Record(("B-CTX-default", "starts"), ("B-SEC-default-GET /api/orders/-VIEWER", "404"));
        var verdict = (Result(mavenPassed: true) with
        {
            Behaviour = new BehaviourReport(Baseline, after, BehaviourContract.Compare(Baseline, after), null)
            {
                Explained = new Dictionary<string, string> { ["B-SEC-default-GET /api/orders/-VIEWER"] = "BS-TRAILING-SLASH" }
            }
        }).Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        var reason = Assert.Single(verdict.Reasons);
        Assert.Equal("I-CONTRACT-MATCHES", reason.Id);
        Assert.Contains("1 behaviour differs from Boot 2.7 (1 a known platform change)", reason.Text);
    }

    [Fact]
    public void MavenPassed_ButTheContractCouldNotBeChecked_NeedsADecision()
    {
        var verdict = (Result(mavenPassed: true) with
        {
            Behaviour = new BehaviourReport(Baseline, null, null, "not compared: probe crashed")
        }).Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        Assert.Contains("not compared: probe crashed", Assert.Single(verdict.Reasons).Text);
    }

    [Fact]
    public void MavenFailedToCompile_TheUncomparedContractIsAConsequence_NotADecision()
    {
        var verdict = (Result(mavenPassed: false) with
        {
            Behaviour = new BehaviourReport(Baseline, null, null, "not compared: the migrated code does not compile")
        }).Verdict;

        Assert.Equal(VerdictState.Failed, verdict.State);
        Assert.DoesNotContain(verdict.Reasons, reason => reason.Id == "I-CONTRACT-MATCHES");
    }

    [Theory]
    [InlineData(PortTaskStatus.Reverted, VerdictState.NeedsDecision)]
    [InlineData(PortTaskStatus.CannotFix, VerdictState.NeedsDecision)]
    [InlineData(PortTaskStatus.Error, VerdictState.Failed)]
    public void APortNotKept_IsNamedByItsMapping(
        PortTaskStatus status,
        VerdictState expected)
    {
        var task = new PortTaskRecord("M-HTTPCLIENT-5-POOL", "HttpClient 5", ["A.java"], ["P3"], status, "it changed kept behaviour", [1, 2]);
        var verdict = (Result(mavenPassed: false) with
        {
            Remediation = new RemediationReport(RemediationOutcome.NeedsHumanDecision, []) { Tasks = [task] },
            // The task's own problem is still there: it is reported once, as the port.
            RemainingProblems = [Problem(ProblemKind.ApiMove, "src/main/java/A.java", "M-HTTPCLIENT-5-POOL")]
        }).Verdict;

        Assert.Equal(expected, verdict.State);
        Assert.Contains(verdict.Reasons, reason => reason.Id == "M-HTTPCLIENT-5-POOL" && reason.Text.Contains("it changed kept behaviour", StringComparison.Ordinal));
        Assert.DoesNotContain(verdict.Reasons, reason => reason.Id == "K-API-MOVE");
    }

    [Fact]
    public void AnApiMoveNoPortWorked_FailsAsWorkLeft()
    {
        // AI ports off: a known API move is still work, not a decision.
        var verdict = (Result(mavenPassed: false) with
        {
            RemainingProblems = [Problem(ProblemKind.ApiMove, "src/main/java/A.java", "M-HTTPCLIENT-5-POOL")]
        }).Verdict;

        Assert.Equal(VerdictState.Failed, verdict.State);
        Assert.Contains(verdict.Reasons, reason => reason.Id == "K-API-MOVE");
    }

    [Fact]
    public void ALegacyResult_WhereTheAIAskedForADecision_StillNeedsOne()
    {
        // Runs 22 and 24 were recorded before scoped ports: the agent's decision has no task.
        var verdict = (Result(mavenPassed: false) with
        {
            Remediation = new RemediationReport(RemediationOutcome.NeedsHumanDecision,
                [new RemediationAttemptRecord(1, "compile", null, false, null, null, null, null, [], null, null)])
        }).Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        Assert.Contains(verdict.Reasons, reason => reason.Id == "AI-DECISION");
    }

    [Fact]
    public void AScopedRunWithNoTasks_IsNotMistakenForAnAIDecision()
    {
        // The port runner reports NeedsHumanDecision whenever Maven still fails; that alone is not a decision.
        var verdict = (Result(mavenPassed: false) with
        {
            Remediation = new RemediationReport(RemediationOutcome.NeedsHumanDecision, [])
        }).Verdict;

        Assert.Equal(VerdictState.Failed, verdict.State);
    }

    [Fact]
    public void Run27_NeedsADecision_ForExactlyItsTwoDecisionsAndTheLibrary()
    {
        var after = Record(("B-CTX-default", "starts"), ("B-SEC-default-GET /api/orders/-VIEWER", "404"));
        var accepted = new PortTaskRecord("M-HTTPCLIENT-5-POOL", "HttpClient 5", ["A.java"], ["P3"], PortTaskStatus.Accepted, null, [3]);
        var result = Result(mavenPassed: false) with
        {
            UnsupportedLibraries = ["Ehcache 2"],
            Remediation = new RemediationReport(RemediationOutcome.NeedsHumanDecision, []) { Tasks = [accepted] },
            RemainingProblems =
            [
                Problem(ProblemKind.PlatformDropped, "a.inventory.InventoryCachingTests.availabilityIsCachedAndRefreshedAfterItsTimeToLive", library: "Ehcache 2"),
                Problem(ProblemKind.Contract, "a.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash")
            ],
            Behaviour = new BehaviourReport(Baseline, after, BehaviourContract.Compare(Baseline, after), null)
        };

        var verdict = result.Verdict;

        Assert.Equal(VerdictState.NeedsDecision, verdict.State);
        Assert.Equal(
            ["I-UNSUPPORTED-NEVER-SUCCEEDS", "K-PLATFORM-DROPPED", "K-CONTRACT", "I-CONTRACT-MATCHES", "F-MAVEN"],
            verdict.Reasons.Select(reason => reason.Id));
        Assert.True(result.NeedsDecision);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void TheSummary_ShowsEachRepositorysReasons()
    {
        var result = Result(mavenPassed: true) with { UnsupportedLibraries = ["Ehcache 2"] };

        var markdown = TransformationSummaryWriter.Render([result]);

        Assert.Contains("### orders-service: why it needs a decision", markdown);
        Assert.Contains("- `I-UNSUPPORTED-NEVER-SUCCEEDS` (a person): Ehcache 2", markdown);
    }

    private static TransformationExecutionResult Result(
        bool mavenPassed) =>
        new(
            new TransformationResult("orders-service", Sha, TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
            new TransformationValidationResult("orders-service", Sha,
                mavenPassed ? TransformationValidationStatus.Passed : TransformationValidationStatus.Failed,
                [
                    new TransformationValidationCheckResult("compile", true, null, null),
                    new TransformationValidationCheckResult("unit-tests", mavenPassed, null, null)
                ]));

    private static ClassifiedProblem Problem(
        ProblemKind kind,
        string subject,
        string? mapping = null,
        string? library = null) =>
        new("P1", kind, subject, "S-TEST", mapping, null, library, ["e"]);

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", Sha, items.ToDictionary(item => item.Item, item => item.Value));
}
