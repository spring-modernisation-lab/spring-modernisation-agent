using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

public class PortAcceptanceTests
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Security = "src/main/java/a/config/SecurityConfiguration.java";
    private const string Http = "src/main/java/a/config/InventoryClientConfiguration.java";
    private const string Order = "src/main/java/a/domain/CustomerOrder.java";

    private static readonly PortTask HttpTask = new(
        Knowledge.Mappings["M-HTTPCLIENT-5-POOL"], [Http], [ApiMove("P3", Http, "M-HTTPCLIENT-5-POOL")], ["e"]);

    private static readonly PortTask SecurityTask = new(
        Knowledge.Mappings["M-SECURITY-6-FILTERCHAIN"], [Security], [ApiMove("P1", Security, "M-SECURITY-6-FILTERCHAIN")], ["e"]);

    private static readonly ClassifiedProblem[] Before =
    [
        ApiMove("P1", Security, "M-SECURITY-6-FILTERCHAIN"),
        ApiMove("P2", Order, "M-HIBERNATE-6-USERTYPE"),
        ApiMove("P3", Http, "M-HTTPCLIENT-5-POOL")
    ];

    [Fact]
    public void Judge_TheTasksEvidenceIsGoneAndNothingNewFails_IsAccepted()
    {
        var judgement = PortAcceptance.Judge(HttpTask, Failing("compile"), Before, Failing("compile"), Before[..2]);

        Assert.True(judgement.Accepted);
        Assert.Null(judgement.Reason);
    }

    [Fact]
    public void Judge_TheTasksEvidenceRemains_IsRejectedWithIt()
    {
        // A port's follow-up errors keep the mapping (the classifier inherits it).
        var followUp = ApiMove("P3", Http, "M-HTTPCLIENT-5-POOL") with { Evidence = ["cannot find symbol TimeValue"] };

        var judgement = PortAcceptance.Judge(HttpTask, Failing("compile"), Before, Failing("compile"), [.. Before[..2], followUp]);

        Assert.False(judgement.Accepted);
        Assert.Contains("still fails", judgement.Reason);
        Assert.Equal([followUp], judgement.Remaining);
    }

    [Fact]
    public void Judge_ANewUnknownFailureElsewhere_IsBlamedOnThePort()
    {
        var unknown = new ClassifiedProblem("P9", ProblemKind.Unknown, "src/main/java/a/web/OrderController.java", null, null, null, null, ["cannot find symbol InventoryClient"]);

        var judgement = PortAcceptance.Judge(HttpTask, Failing("compile"), Before, Failing("compile"), [.. Before[..2], unknown]);

        Assert.False(judgement.Accepted);
        Assert.Contains("new failure", judgement.Reason);
        Assert.Equal([unknown], judgement.Caused);
    }

    [Fact]
    public void Judge_AKnownProblemJavacHadNotReportedYet_IsNotBlamed()
    {
        // Fixing one file can let javac report another file's errors for the first time.
        var revealed = ApiMove("P4", "src/main/java/a/persistence/MoneyUserType.java", "M-HIBERNATE-6-USERTYPE");

        var judgement = PortAcceptance.Judge(HttpTask, Failing("compile"), Before, Failing("compile"), [.. Before[..2], revealed]);

        Assert.True(judgement.Accepted);
    }

    [Fact]
    public void Judge_TheBuildMovingOnToALaterCheck_IsProgress()
    {
        var test = new ClassifiedProblem("P1", ProblemKind.Unknown, "a.SomeTests.something", null, null, null, null, ["x"]);

        var judgement = PortAcceptance.Judge(HttpTask, Failing("compile"), [Before[2]], Failing("unit-tests"), [test]);

        Assert.True(judgement.Accepted);
    }

    [Fact]
    public void Judge_TheBuildFallingBackToAnEarlierCheck_IsRejected()
    {
        var judgement = PortAcceptance.Judge(HttpTask, Failing("unit-tests"), [], Failing("compile"), [ApiMove("P1", Order, "M-HIBERNATE-6-USERTYPE")]);

        Assert.False(judgement.Accepted);
        Assert.Contains("compile now fails", judgement.Reason);
    }

    [Fact]
    public void Judge_R24Attempt2AsATask_IsRejectedBecauseItsEvidenceRemains()
    {
        // R24 attempt 2: the servlet fix used jakarta.servlet.HttpServletRequest; the test still can't find it.
        var task = new PortTask(Knowledge.Mappings["M-SECURITY-6-FILTERCHAIN"], [Security],
            [ApiMove("P1", Security, "M-SECURITY-6-FILTERCHAIN")], ["e"]);
        var stillMissing = ApiMove("P1", Security, "M-SECURITY-6-FILTERCHAIN") with
        {
            Evidence = ["root cause: java.lang.ClassNotFoundException: jakarta.servlet.HttpServletRequest"]
        };

        var judgement = PortAcceptance.Judge(task, Failing("compile"), [Before[0]], Failing("compile"), [stillMissing]);

        Assert.False(judgement.Accepted);
        Assert.Contains("HttpServletRequest", string.Join("\n", judgement.Remaining.SelectMany(problem => problem.Evidence)));
    }

    [Fact]
    public void Obligations_Run26_TheTtlIsBrokenAndTheTrailingSlashIsThePlatforms()
    {
        var baseline = Record(
            ("B-CTX-default", "starts"),
            ("B-CTX-production", "starts"),
            ("B-BEAN-default-HttpClient.connectionTimeToLive", "30000ms"),
            ("B-BEAN-default-HttpClient.maxTotal", "50"),
            ("B-BEAN-production-HttpClient.maxTotal", "50"),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "200"));
        var after = Record(
            ("B-CTX-default", "starts"),
            ("B-CTX-production", "fails: NoSuchBeanDefinitionException: cacheManager"),
            ("B-BEAN-default-HttpClient.connectionTimeToLive", BehaviourRecord.Unavailable),
            ("B-BEAN-default-HttpClient.maxTotal", "50"),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "404"));

        var results = PortObligations.Check([SecurityTask, HttpTask], baseline, BehaviourContract.Compare(baseline, after), Knowledge);

        var security = results[0];
        Assert.True(security.Holds);
        Assert.Equal(["B-SEC-default-GET /api/orders-VIEWER"], security.Kept);
        Assert.Equal("BS-TRAILING-SLASH", Assert.Single(security.Explained).Signature.Id);

        var http = results[1];
        Assert.False(http.Holds);
        Assert.Equal("B-BEAN-default-HttpClient.connectionTimeToLive", Assert.Single(http.Broken).Item);
        Assert.Equal(["B-BEAN-default-HttpClient.maxTotal"], http.Kept);
        Assert.Equal(["B-BEAN-production-HttpClient.maxTotal"], http.Unchecked);
    }

    [Fact]
    public void Obligations_AMappingWithNoneHoldsWithNothingChecked()
    {
        var hibernate = new PortTask(Knowledge.Mappings["M-HIBERNATE-6-USERTYPE"], [Order], [Before[1]], ["e"]);
        var baseline = Record(("B-CTX-default", "starts"));

        var result = PortObligations.Check([hibernate], baseline, BehaviourContract.Compare(baseline, baseline), Knowledge).Single();

        Assert.True(result.Holds);
        Assert.Empty(result.Kept);
    }

    private static TransformationValidationResult Failing(
        string check)
    {
        string[] order = ["compile", "unit-tests"];
        var failedAt = Array.IndexOf(order, check);

        return new TransformationValidationResult("orders-service", "abc", TransformationValidationStatus.Failed,
            order.Take(failedAt + 1).Select((name, index) => new TransformationValidationCheckResult(name, index < failedAt, null, null)).ToList());
    }

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", "abc", items.ToDictionary(item => item.Item, item => item.Value));

    private static ClassifiedProblem ApiMove(
        string id,
        string subject,
        string mapping) =>
        new(id, ProblemKind.ApiMove, subject, "S-TEST", mapping, null, null, [$"error in {subject}"]);
}
