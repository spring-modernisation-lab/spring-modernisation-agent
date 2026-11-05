using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Publishing;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Reporting;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

public class DecisionReportingTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Sha = "ce42258";

    private static readonly BehaviourRecord Baseline = Record(
        ("B-CTX-default", "starts"),
        ("B-SEC-default-GET /api/orders-VIEWER", "200"),
        ("B-SEC-default-GET /api/orders/-VIEWER", "200"),
        ("B-SEC-default-POST /api/orders/-VIEWER", "400"),
        ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"));

    private static readonly BehaviourRecord After = Record(
        ("B-CTX-default", "starts"),
        ("B-SEC-default-GET /api/orders-VIEWER", "200"),
        ("B-SEC-default-GET /api/orders/-VIEWER", "404"),
        ("B-SEC-default-POST /api/orders/-VIEWER", "404"),
        ("B-BEAN-default-HttpClient.connectionTimeToLive", BehaviourRecord.Unavailable));

    [Fact]
    public void Allowances_Retire_AllowsOnlyTheDifferencesItsSignatureMatches()
    {
        var report = Compared();

        var applied = DecisionAllowances.Apply(report, [Decision("retire")], Knowledge);

        var allowed = applied.Comparison!.Differences.Where(difference => difference.Status == ContractItemStatus.Allowed).ToList();
        Assert.Equal(["B-SEC-default-GET /api/orders/-VIEWER", "B-SEC-default-POST /api/orders/-VIEWER"], allowed.Select(difference => difference.Item));
        Assert.All(allowed, difference => Assert.Equal("D-orders-service-1", difference.Decision));

        // The TTL is not the trailing slash: a decision about one does not excuse the other.
        Assert.Contains(applied.Comparison.Differences, difference =>
            difference.Item.Contains("connectionTimeToLive", StringComparison.Ordinal) && difference.Status == ContractItemStatus.Unavailable);
        Assert.False(applied.Holds);
        Assert.Empty(applied.Explained);
    }

    [Fact]
    public void Allowances_WhenEveryDifferenceIsAllowed_TheContractHolds()
    {
        var only404s = Record(
            ("B-CTX-default", "starts"),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "404"),
            ("B-SEC-default-POST /api/orders/-VIEWER", "404"),
            ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"));
        var report = new BehaviourReport(Baseline, only404s, BehaviourContract.Compare(Baseline, only404s), null);

        Assert.True(DecisionAllowances.Apply(report, [Decision("retire")], Knowledge).Holds);
    }

    [Fact]
    public void Allowances_AKeepDecisionAllowsNothing_AndNoDecisionLeavesTheReportAsItWas()
    {
        var report = Compared();

        Assert.Same(report, DecisionAllowances.Apply(report, [Decision("keep")], Knowledge));
        Assert.Same(report, DecisionAllowances.Apply(report, [], Knowledge));
    }

    [Fact]
    public void Outcomes_SayWhatEachDecisionDid()
    {
        var accepted = new PortTaskRecord("D-orders-service-1", "Keep trailing-slash URLs", ["OrderController.java"], ["P2"], PortTaskStatus.Accepted, null, [4]);
        var remediation = new RemediationReport(RemediationOutcome.Fixed, []) { Tasks = [accepted] };

        Assert.Equal("applied: its port was kept", DecisionOutcomes.Of([Decision("keep")], remediation, null).Single().Result);

        var reverted = accepted with { Status = PortTaskStatus.Reverted, Reason = "the test still fails" };
        remediation = remediation with { Tasks = [reverted] };
        Assert.Equal("not applied: its port was reverted (the test still fails)", DecisionOutcomes.Of([Decision("keep")], remediation, null).Single().Result);

        var retired = DecisionAllowances.Apply(Compared(), [Decision("retire")], Knowledge);
        Assert.Equal("allows 2 differences", DecisionOutcomes.Of([Decision("retire")], null, retired).Single().Result);

        Assert.Equal("manual: the pipeline does not apply it", DecisionOutcomes.Of([Decision("manual", "ehcache")], null, null).Single().Result);

        var noted = new RemediationReport(RemediationOutcome.Fixed, []) { DecisionNotes = ["D-orders-service-1 (trailing-slash: keep): no problem left that it answers (S-HTTP-CONTRACT), so nothing was ported."] };
        Assert.StartsWith("nothing was ported: no problem left", DecisionOutcomes.Of([Decision("keep")], noted, null).Single().Result);
    }

    [Fact]
    public void Pr_ListsTheOptionsAndTheExactLineToCommit_ForADecisionNobodyHasMade()
    {
        var body = PullRequestContentBuilder.Build(Plan(), NeedsContractDecision(), [], null, playbooks: Knowledge.Decisions)!.Body;

        Assert.Contains("### Decisions you can record", body);
        Assert.Contains("Trailing-slash URLs", body);
        Assert.Contains("`trailing-slash: keep`: Keep the old URLs working (an explicit mapping)", body);
        Assert.Contains("`trailing-slash: retire`", body);
        Assert.Contains("`POST /api/orders/` was open to a VIEWER", body);
        Assert.Contains("decisions/orders-service.yaml", body);
        Assert.Contains("id: D-orders-service-1", body);
        Assert.Contains("subject: trailing-slash", body);
    }

    [Fact]
    public void Pr_ShowsADecisionMadeAndWhatItDid_AndNoLongerOffersIt()
    {
        var result = NeedsContractDecision() with
        {
            Decisions = [new DecisionOutcome("D-orders-service-1", "trailing-slash", "keep", "a-person", "2026-11-05", "not applied: its port was reverted (the test still fails)")]
        };

        var body = PullRequestContentBuilder.Build(Plan(), result, [], null, playbooks: Knowledge.Decisions)!.Body;

        Assert.Contains("### Decisions made", body);
        Assert.Contains("`D-orders-service-1` trailing-slash: keep, by a-person on 2026-11-05: not applied: its port was reverted (the test still fails)", body);
        Assert.DoesNotContain("### Decisions you can record", body);
    }

    [Fact]
    public void Summary_ListsTheDecisions()
    {
        var result = NeedsContractDecision() with
        {
            Decisions = [new DecisionOutcome("D-orders-service-1", "trailing-slash", "retire", "a-person", "2026-11-05", "allows 2 differences")]
        };

        var markdown = TransformationSummaryWriter.Render([result]);

        Assert.Contains("### orders-service: decisions", markdown);
        Assert.Contains("| `D-orders-service-1` | trailing-slash | retire | a-person | allows 2 differences |", markdown);
    }

    private static BehaviourReport Compared() =>
        new(Baseline, After, BehaviourContract.Compare(Baseline, After), null)
        {
            Explained = new Dictionary<string, string>
            {
                ["B-SEC-default-GET /api/orders/-VIEWER"] = "BS-TRAILING-SLASH",
                ["B-SEC-default-POST /api/orders/-VIEWER"] = "BS-TRAILING-SLASH"
            }
        };

    private static DecisionRecord Decision(
        string option,
        string subject = "trailing-slash")
    {
        var playbook = Knowledge.Decisions.Subject(subject)!;
        return new DecisionRecord("D-orders-service-1", subject, option, "a-person", "2026-11-05", playbook, playbook.Option(option)!);
    }

    private static TransformationPlan Plan() =>
        new("orders-service", Sha, 1, RepositoryKind.SpringBootApplication, "spring-boot-application-2.7-to-3",
            new TransformationTarget("3.5.x", "21"), [], ["compile"]);

    /// <summary>Run 27 after the ports: the code compiles and the trailing-slash test is the one failure left.</summary>
    private static TransformationExecutionResult NeedsContractDecision() =>
        new(
            new TransformationResult("orders-service", Sha, TransformationStatus.Succeeded, [], ["pom.xml"], null, null),
            new TransformationValidationResult("orders-service", Sha, TransformationValidationStatus.Failed,
                [new TransformationValidationCheckResult("compile", true, null, null), new TransformationValidationCheckResult("unit-tests", false, null, null)]))
        {
            Patches = [new PatchArtifact("cumulative", "patches/orders-service/cumulative.patch", "cafe", ["pom.xml"])],
            RemainingProblems =
            [
                new ClassifiedProblem("P2", ProblemKind.Contract, "a.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash",
                    "S-HTTP-CONTRACT", null, null, null, ["Status expected:<200> but was:<404>"])
            ]
        };

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", Sha, items.ToDictionary(item => item.Item, item => item.Value));
}
