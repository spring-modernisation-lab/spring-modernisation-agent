using SpringModernisationAgent.Behaviour;
using SpringModernisationAgent.Models;
using SpringModernisationAgent.Porting;
using SpringModernisationAgent.Remediation;
using SpringModernisationAgent.Triage;

namespace SpringModernisationAgent.Tests.Porting;

public class DecisionTasksTests : IDisposable
{
    private static readonly ProblemKnowledge Knowledge =
        ProblemKnowledge.LoadDefault(UnsupportedLibraries.All.Select(library => library.Name).ToList());

    private const string Controller = "src/main/java/a/web/OrderController.java";

    private static readonly BehaviourRecord Baseline = Record(
        ("B-CTX-default", "starts"),
        ("B-SEC-default-GET /api/orders-VIEWER", "200"),
        ("B-SEC-default-GET /api/orders/-VIEWER", "200"),
        ("B-SEC-default-POST /api/orders/-VIEWER", "400"),
        ("B-SEC-default-GET /api/orders/probe/-VIEWER", "404"));

    // Run 27: the only test left failing on the trailing slash.
    private static readonly ClassifiedProblem Contract = new(
        "P2", ProblemKind.Contract, "a.web.OrderControllerTests.legacyClientsCanListOrdersUsingTrailingSlash",
        "S-HTTP-CONTRACT", null, null, null, ["FAILED TEST ... Status expected:<200> but was:<404>"]);

    private readonly string _repository = Directory.CreateTempSubdirectory("decision-tasks-").FullName;

    public DecisionTasksTests()
    {
        Write(Controller, "@RestController\n@RequestMapping(\"/api/orders\")\nclass OrderController {}\n");
        Write("src/main/java/a/web/Other.java", "class Other { String x = \"/api/orders\"; }\n");
        Write("src/main/java/a/config/Security.java", "@RequestMapping(\"/api/other\")\nclass Security {}\n");
    }

    [Fact]
    public void Plan_Keep_IsOnePortOverTheControllerThatMapsThePath_JudgedByTheContractProblem()
    {
        var plan = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, [Contract]);

        var task = Assert.Single(plan.Tasks);
        Assert.Equal("D-orders-service-1", task.Id);
        Assert.Equal([Controller], task.Files);
        Assert.Equal(["S-HTTP-CONTRACT"], task.EvidenceSignatures);
        Assert.False(task.ExcusePlatformChanges);
        Assert.Equal(Contract.Evidence, task.Evidence);
        Assert.Equal("Keep trailing-slash URLs", task.Mapping.Name);
        Assert.Contains(task.Mapping.Notes, note => note.Contains("@RequestMapping", StringComparison.Ordinal));

        // The obligations are what the baseline served on a trailing slash, nothing else.
        Assert.True(task.Obliges("B-SEC-default-GET /api/orders/-VIEWER"));
        Assert.True(task.Obliges("B-SEC-default-POST /api/orders/-VIEWER"));
        Assert.False(task.Obliges("B-SEC-default-GET /api/orders-VIEWER"));
        Assert.False(task.Obliges("B-SEC-default-GET /api/orders/probe/-VIEWER")); // the baseline's own 404
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void Plan_NothingToAnswer_WhenTheTestNoLongerFails()
    {
        var plan = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, []);

        Assert.Empty(plan.Tasks);
        Assert.Contains("no problem left that it answers", Assert.Single(plan.Notes));
    }

    [Fact]
    public void Plan_WithoutABaseline_CannotDeriveItsObligations()
    {
        var plan = DecisionTasks.Plan([Decision("keep")], null, _repository, [Contract]);

        Assert.Empty(plan.Tasks);
        Assert.Contains("needs the behaviour baseline", Assert.Single(plan.Notes));
    }

    [Fact]
    public void Plan_NoControllerMapsThePath_IsANote_NotAGuess()
    {
        File.Delete(Path.Combine(_repository, Controller));

        var plan = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, [Contract]);

        Assert.Empty(plan.Tasks);
        Assert.Contains("no production file maps", Assert.Single(plan.Notes));
    }

    [Theory]
    [InlineData("trailing-slash", "retire")]
    [InlineData("ehcache", "manual")]
    public void Plan_OptionsWithNoPort_CreateNoTask(
        string subject,
        string option)
    {
        var plan = DecisionTasks.Plan([Decision(option, subject)], Baseline, _repository, [Contract]);

        Assert.Empty(plan.Tasks);
        Assert.Empty(plan.Notes);
    }

    [Fact]
    public void Judge_ADecisionPortIsJudgedByItsEvidenceSignature_NotByFilesOrMapping()
    {
        var task = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, [Contract]).Tasks[0];

        var still = PortAcceptance.Judge(task, Failing("unit-tests"), [Contract], Failing("unit-tests"), [Contract]);
        var gone = PortAcceptance.Judge(task, Failing("unit-tests"), [Contract], Passing(), []);

        Assert.False(still.Accepted);
        Assert.Equal([Contract], still.Remaining);
        Assert.True(gone.Accepted);
    }

    [Fact]
    public void Obligations_ADecisionPortThatLeavesThe404s_IsNotExcusedByThePlatformSignature()
    {
        // BS-TRAILING-SLASH explains these 404s for a security port; for the port that exists to remove them it must not.
        var task = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, [Contract]).Tasks[0];
        var after = Record(
            ("B-CTX-default", "starts"),
            ("B-SEC-default-GET /api/orders-VIEWER", "200"),
            ("B-SEC-default-GET /api/orders/-VIEWER", "404"),
            ("B-SEC-default-POST /api/orders/-VIEWER", "404"),
            ("B-SEC-default-GET /api/orders/probe/-VIEWER", "404"));

        var result = PortObligations.Check([task], Baseline, BehaviourContract.Compare(Baseline, after), Knowledge).Single();

        Assert.False(result.Holds);
        Assert.Equal(2, result.Broken.Count);
        Assert.Empty(result.Explained);
    }

    [Fact]
    public void Obligations_ADecisionPortThatRestoresThePaths_Holds()
    {
        var task = DecisionTasks.Plan([Decision("keep")], Baseline, _repository, [Contract]).Tasks[0];

        var result = PortObligations.Check([task], Baseline, BehaviourContract.Compare(Baseline, Baseline), Knowledge).Single();

        Assert.True(result.Holds);
        Assert.Equal(2, result.Kept.Count);
    }

    public void Dispose() => Directory.Delete(_repository, recursive: true);

    private DecisionRecord Decision(
        string option,
        string subject = "trailing-slash")
    {
        var playbook = Knowledge.Decisions.Subject(subject)!;
        return new DecisionRecord("D-orders-service-1", subject, option, "a-person", "2026-11-05", playbook, playbook.Option(option)!);
    }

    private static TransformationValidationResult Failing(
        string check) =>
        new("orders-service", "abc", TransformationValidationStatus.Failed,
            [new TransformationValidationCheckResult("compile", true, null, null), new TransformationValidationCheckResult(check, false, null, null)]);

    private static TransformationValidationResult Passing() =>
        new("orders-service", "abc", TransformationValidationStatus.Passed,
            [new TransformationValidationCheckResult("compile", true, null, null), new TransformationValidationCheckResult("unit-tests", true, null, null)]);

    private static BehaviourRecord Record(params (string Item, string Value)[] items) =>
        new(BehaviourRecord.CurrentSchema, "orders-service", "abc", items.ToDictionary(item => item.Item, item => item.Value));

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_repository, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
